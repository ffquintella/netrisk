using System.Globalization;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    private static readonly IReadOnlySet<string> UnitTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        RiskIndexEntityTypes.OrganizationUnit, RiskIndexEntityTypes.SubOrganizationUnit
    };

    /// <summary>
    /// The drill-down's children (S40 §5.6.1): the parent's children along one dimension, each with its value, provenance
    /// and comparability group. A child with no visible object is omitted, and every count is over the visible objects
    /// only — no "n of N" with a hidden N (S40 §5.6.5, §11.8).
    /// </summary>
    public async Task<RiskIndexNodeChildrenDto> GetNodesAsync(string? parent, RiskIndexDimension dimension, string? filters,
        DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (!Enum.IsDefined(dimension))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidDimension,
                "The dimension is Unit, Process, Activity, Application, AssetClass, Source, Criticality or Environment.");

        var childFilters = RiskIndexNodeKey.Org;
        if (!string.IsNullOrWhiteSpace(filters) &&
            (!RiskIndexNodeKey.TryParse(filters, out childFilters) || childFilters.EntityId is not null ||
             childFilters.Unassigned || childFilters.IsScopeRoot))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidFilter, "Filters are c:, s:, k: and a: terms only.");

        var day = await ResolveDayAsync(date, ct);
        var parentKey = ParseKey(parent);
        if (parentKey.IsScopeRoot) return await ScopeChildrenAsync(day, caller, ct);

        var resolved = await ResolveNodeAsync(parentKey, day, ct);
        var extra = string.Create(CultureInfo.InvariantCulture, $"{(int)dimension}|{childFilters}");
        return await cache.GetOrCreateAsync(CacheKey("nodes", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            () => ChildrenAsync(resolved, day, dimension, childFilters, caller, ct));
    }

    /// <summary>"Meu escopo": its children are the entities the claims name.</summary>
    private async Task<RiskIndexNodeChildrenDto> ScopeChildrenAsync(Day day, RiskIndexCaller caller, CancellationToken ct)
    {
        var claimed = day.Scope.IsUnrestricted ? [] : day.Scope.ClaimedEntityIds.Where(day.Map.Contains).Distinct().ToList();
        var keys = claimed.Select(RiskIndexNodeKey.ForEntity).ToList();
        var rows = await RowsAsync(keys, day, caller, ct);
        return Children(new RiskIndexNodeRef(RiskIndexNodeKey.Scope, "My scope", "scope"), RiskIndexDimension.Unit, rows, 0);
    }

    private async Task<RiskIndexNodeChildrenDto> ChildrenAsync(Node parent, Day day, RiskIndexDimension dimension,
        RiskIndexNodeKey childFilters, RiskIndexCaller caller, CancellationToken ct)
    {
        List<RiskIndexNodeKey> keys;
        var outside = 0;
        var closure = parent.IsRoot ? day.Map.Ids.ToHashSet() : parent.Closure ?? new HashSet<int>();

        switch (dimension)
        {
            case RiskIndexDimension.Unit:
                // "Unassigned" has no entity: a null parent must not match the units that have none.
                keys = day.Map.Ids.Where(id => UnitTypes.Contains(day.Map.DefinitionOf(id) ?? "") &&
                                               (parent.IsRoot
                                                   ? IsTopLevel(day, id)
                                                   : parent.EntityId is { } parentId && day.Map.Tree.ParentOf(id) == parentId))
                    .Order().Select(RiskIndexNodeKey.ForEntity).ToList();
                if (parent.IsRoot) keys.Add(Unassigned());
                break;
            case RiskIndexDimension.Process:
                keys = day.Map.Ids.Where(id => day.Map.DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess &&
                                               (parent.IsRoot || day.Map.UnitsOfProcess(id).Any(closure.Contains) ||
                                                day.Map.Tree.ParentOf(id) is { } p && closure.Contains(p)) &&
                                               id != parent.EntityId)
                    .Order().Select(RiskIndexNodeKey.ForEntity).ToList();
                break;
            case RiskIndexDimension.Activity:
                keys = day.Map.Ids.Where(id => day.Map.DefinitionOf(id) == RiskIndexEntityTypes.Activity &&
                                               (parent.IsRoot || day.Map.Tree.ParentOf(id) is { } p && closure.Contains(p)))
                    .Order().Select(RiskIndexNodeKey.ForEntity).ToList();
                break;
            case RiskIndexDimension.Application:
                var listed = closure.Where(id => day.Map.DefinitionOf(id) == RiskIndexEntityTypes.BusinessProcess)
                    .SelectMany(day.Map.ApplicationsOfProcess);
                keys = day.Map.Ids.Where(id => day.Map.DefinitionOf(id) == RiskIndexEntityTypes.Application &&
                                               (parent.IsRoot || closure.Contains(id) || listed.Contains(id)) &&
                                               id != parent.EntityId)
                    .Order().Select(RiskIndexNodeKey.ForEntity).ToList();
                break;
            default:
            {
                // The parent ∩ each value of the dimension present among its visible objects.
                var composition = await ComposeAsync(parent, day, caller, ct);
                var rows = composition.Rows;
                keys = dimension switch
                {
                    RiskIndexDimension.AssetClass => rows.Where(r => r.AssetClass is not null).Select(r => r.AssetClass!.Value).Distinct()
                        .Order().Select(c => parent.Key.WithAssetClass(c)).ToList(),
                    RiskIndexDimension.Source => RiskIndexSourceBits.Sources
                        .Where(s => rows.Any(r => r.Category == RiskIndexCategory.Exp && RiskIndexSourceBits.Has(r.SourceMask, s)))
                        .Select(s => parent.Key.WithSource(s)).ToList(),
                    RiskIndexDimension.Criticality => rows.Select(r => r.Criticality).Where(k => k is >= 1 and <= 5).Distinct().Order()
                        .Select(k => parent.Key.WithCriticality(k)).ToList(),
                    _ => rows.Where(r => r.Environment is not null).Select(r => r.Environment!).Distinct(StringComparer.Ordinal)
                        .Where(e => RiskIndexNodeKey.TryParse("a:" + e, out _)).Order(StringComparer.Ordinal)
                        .Select(e => parent.Key.WithEnvironment(e)).ToList()
                };
                if (dimension == RiskIndexDimension.Environment)
                    outside = rows.Where(r => r.Environment is null).Select(r => r.Key).Distinct().Count();
                break;
            }
        }

        // Each entity child carries the parent's filter terms and the Explore chips (S40 §5.6.2).
        keys = keys.Select(k => k.WithFiltersOf(parent.Key).WithFiltersOf(childFilters)).Distinct().ToList();
        var children = await RowsAsync(keys, day, caller, ct);
        return Children(parent.Ref, dimension, children, outside);

        RiskIndexNodeKey Unassigned()
        {
            RiskIndexNodeKey.TryParse(RiskIndexNodeKey.UnassignedTerm, out var u);
            return u;
        }
    }

    /// <summary>A unit at the top of the map: no parent, or the organization entity as parent.</summary>
    private static bool IsTopLevel(Day day, int id) =>
        day.Map.Tree.ParentOf(id) is not { } parent || day.Map.DefinitionOf(parent) == RiskIndexEntityTypes.Organization ||
        !day.Map.Contains(parent);

    private sealed record ChildRow(RiskIndexNodeRowDto Row, IReadOnlySet<IcrObjectKey> Objects);

    /// <summary>The rows of the children: official ones from their snapshot rows, the rest composed in one budget slot.</summary>
    private async Task<List<ChildRow>> RowsAsync(IReadOnlyList<RiskIndexNodeKey> keys, Day day, RiskIndexCaller caller,
        CancellationToken ct)
    {
        var nodes = new List<Node>();
        foreach (var key in keys)
        {
            try
            {
                nodes.Add(await ResolveNodeAsync(key, day, ct));
            }
            catch (RiskIndexException ex) when (ex.Status is 403 or 404)
            {
                // A child the caller may not read at all is not listed (and not counted).
            }
        }

        if (nodes.Count == 0) return [];

        return await budget.RunAsync(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            var set = await RiskIndexStoredDays.LoadSetAsync(db, day.Date, day.Profile.Id, day.P, day.Map, token);
            var result = new List<ChildRow>();
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                var objects = NodeKeys(node, set.Rows, set.Map);
                if (objects.Count == 0) continue;

                NodeValues values;
                int? delta = null;
                if (node.Official &&
                    await OfficialValuesAsync(db, day.Date, day.Profile.Id, node.OfficialKind, node.OfficialRef, day.P, token) is { } v)
                {
                    values = v;
                    delta = (await Delta7dAsync(db, node, day, v, token)).Points;
                }
                else values = DerivedValues(Compose(node, day, set), day.P, DateTime.UtcNow);

                var applicable = values.Categories
                    .Where(c => c.State is RiskIndexCategoryState.Available or RiskIndexCategoryState.Unavailable)
                    .Select(c => c.Category).ToList();
                var group = string.Join('|', node.Ref.NodeType, string.Join(',', applicable.Select(c => (int)c)), (int)node.Provenance);
                var band = BandIndex(values.Displayed, values.State, day.P);
                var hasChildren = node.EntityId is { } id && day.Map.Tree.ChildrenOf(id).Count > 0;
                result.Add(new ChildRow(new RiskIndexNodeRowDto(node.Ref, values.Displayed, band is { } b ? BandKey(b) : null,
                    values.State, node.Provenance, delta, values.Coverage, objects.Count, 0, hasChildren, group,
                    values.IntervalLow, values.IntervalHigh, null), objects));
            }

            return result;
        }, ct);
    }

    private static RiskIndexNodeChildrenDto Children(RiskIndexNodeRef parent, RiskIndexDimension dimension,
        IReadOnlyList<ChildRow> children, int outside)
    {
        // Shared objects (S39 §10.3): those of a child that also belong to a sibling.
        var counts = children.SelectMany(c => c.Objects).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var rows = children.Select(c => c.Row with { SharedObjects = c.Objects.Count(k => counts[k] > 1) }).ToList();

        // Only nodes of one type, one set of applicable categories and one provenance compare (S40 §5.6.4); partial groups
        // last, and inside a group indicative and no-data nodes after the classified ones.
        var groups = rows.GroupBy(r => r.ComparabilityGroup)
            .Select(g => new RiskIndexComparabilityGroupDto(g.Key, g.First().Node.NodeType,
                g.First().ComparabilityGroup.Split('|')[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => (RiskIndexCategory)int.Parse(x, CultureInfo.InvariantCulture)).ToList(),
                g.First().Provenance,
                g.First().Provenance == RiskIndexProvenance.PartialRestricted ? "not_comparable_partial" : null))
            .OrderBy(g => g.Provenance == RiskIndexProvenance.PartialRestricted).ThenBy(g => g.NodeType, StringComparer.Ordinal)
            .ToList();
        var order = groups.Select((g, i) => (g.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var sorted = rows.OrderBy(r => order[r.ComparabilityGroup])
            .ThenBy(r => r.State is RiskIndexDisplayState.Indicative or RiskIndexDisplayState.NoData)
            .ThenBy(r => r.Node.Label, StringComparer.CurrentCulture).ToList();
        return new RiskIndexNodeChildrenDto(parent, dimension, groups, sorted, outside);
    }
}
