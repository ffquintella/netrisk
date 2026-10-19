using System.Globalization;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    /// <summary>The filters <c>GET /RiskIndex/Objects</c> accepts (S40 §8.2), and nothing else.</summary>
    public static readonly IReadOnlySet<string> ObjectFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "category", "state", "kind", "assetClass", "source", "criticality", "environment", "label", "isPresumed", "seal"
    };

    /// <summary>
    /// The sorts it accepts (S40 §8.2). There is deliberately no sort by sensitivity or by Δ (S39 §1.3): the index ranks
    /// nothing to work on, and <c>RiskIndexObjectSortMapTest</c> pins that neither is declared.
    /// </summary>
    public static readonly IReadOnlySet<string> ObjectSortKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "label", "r", "contribution", "criticality", "lastReading"
    };

    public async Task<(IReadOnlyList<RiskIndexObjectRowDto> Rows, int Total)> GetObjectsAsync(string? node, DateOnly? date,
        RiskIndexObjectQuery query, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (query.Page < 1 || query.PageSize is < 1 or > RiskIndexObjectQuery.MaxPageSize)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidPage,
                $"Page starts at 1 and PageSize is 1 to {RiskIndexObjectQuery.MaxPageSize}.");
        var filters = ParseFilters(query.Filters);
        var sorts = ParseSorts(query.Sorts);

        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        var all = await cache.GetOrCreateAsync(CacheKey("objects", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () => new Box<IReadOnlyList<ObjectRow>>(await ObjectRowsAsync(resolved, day, caller, ct)));

        IEnumerable<ObjectRow> rows = all.Value.Where(r => filters.All(f => f(r)));
        IOrderedEnumerable<ObjectRow>? ordered = null;
        foreach (var (sortKey, descending) in sorts)
        {
            Func<ObjectRow, object?> selector = sortKey.ToLowerInvariant() switch
            {
                "label" => r => r.Dto.Object.Label,
                "r" => r => r.Dto.R,
                "contribution" => r => r.Dto.ContributionPoints,
                "criticality" => r => r.Dto.Criticality,
                _ => r => r.Dto.LastReadingUtc
            };
            ordered = ordered is null
                ? descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector)
                : descending ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
        }

        // The default order is the contribution, largest first — a composition, not a work list.
        var list = (ordered ?? rows.OrderByDescending(r => r.Dto.ContributionPoints)).ThenBy(r => r.Dto.Object.Object.Kind)
            .ThenBy(r => r.Dto.Object.Object.Ref, StringComparer.Ordinal).ThenBy(r => r.Dto.Category).ToList();
        return (list.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(r => r.Dto).ToList(), list.Count);
    }

    /// <summary>One listed row with what the filters read beside the DTO.</summary>
    internal sealed record ObjectRow(RiskIndexObjectRowDto Dto, RiskIndexScenarioInputs? Scenario);

    private async Task<IReadOnlyList<ObjectRow>> ObjectRowsAsync(Node node, Day day, RiskIndexCaller caller, CancellationToken ct)
    {
        var composition = await ComposeAsync(node, day, caller, ct);
        var result = composition.Result;
        var gains = result.Categories.Where(c => c.Status == IcrCategoryStatus.Available && c.Value > 0)
            .ToDictionary(c => c.Category, c => result.CategoryContributions.GetValueOrDefault(c.Category) / c.Value);
        var parts = result.Categories.SelectMany(c => c.Parts.Select(p => (c.Category, p.Key, p.Part)))
            .GroupBy(x => (x.Category, x.Key)).ToDictionary(g => g.Key, g => g.Sum(x => x.Part));

        await using var db = dalService.GetContext();
        var labels = await RiskIndexLabels.ResolveAsync(db, composition.Rows.Select(r => r.Key), caller, ct);
        var readings = await LastReadingsAsync(db, day, composition.Rows.Select(r => r.Key).ToHashSet(), ct);

        return composition.Rows.Select(r =>
        {
            var points = gains.GetValueOrDefault(r.Category) * parts.GetValueOrDefault((r.Category, r.Key));
            var sources = RiskIndexSourceBits.Sources.Where(s => RiskIndexSourceBits.Has(r.SourceMask, s)).ToList();
            readings.TryGetValue((r.Key, r.Category), out var reading);
            return new ObjectRow(new RiskIndexObjectRowDto(labels[r.Key], r.AssetClass?.ToString(), r.Category, r.R, r.Criticality,
                r.CriticalitySource, r.State, r.Freshness, reading, points, r.State == RiskIndexObjectState.Presumed,
                Math.Max(0, r.Attribution.Count - 1), sources, r.Environment), r.ScenarioInputs);
        }).ToList();
    }

    /// <summary>The freshest reading of each (object, category) of the node, from the stored factors.</summary>
    private static async Task<Dictionary<(IcrObjectKey, RiskIndexCategory), DateTime?>> LastReadingsAsync(
        DAL.Context.NRDbContext db, Day day, IReadOnlySet<IcrObjectKey> keys, CancellationToken ct)
    {
        var rows = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == day.Date && o.ProfileId == day.Profile.Id)
            .Select(o => new { o.ObjectKind, o.ObjectRef, o.Category, o.Factors })
            .ToListAsync(ct);
        var result = new Dictionary<(IcrObjectKey, RiskIndexCategory), DateTime?>();
        foreach (var row in rows)
        {
            var key = new IcrObjectKey(row.ObjectKind, row.ObjectRef);
            if (!keys.Contains(key)) continue;
            var factors = RiskIndexStoredJson.Deserialize<List<StoredFactor>>(row.Factors) ?? [];
            result[(key, row.Category)] = factors.Select(f => f.ReadAt).Where(d => d is not null).Max();
        }

        return result;
    }

    private static List<Func<ObjectRow, bool>> ParseFilters(string? text)
    {
        var filters = new List<Func<ObjectRow, bool>>();
        if (string.IsNullOrWhiteSpace(text)) return filters;
        if (text.Length > 512) throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidFilter, "The filter is too long.");

        foreach (var term in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = term.IndexOf('=');
            if (eq <= 0)
                throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidFilter, $"'{Trim(term)}' is not key=value.");
            var name = term[..eq].Trim().TrimEnd('*', '^', '!');
            var value = term[(eq + 1)..].Trim();
            if (!ObjectFilterKeys.Contains(name))
                throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidFilter, $"'{Trim(name)}' is not a filter of this list.");

            filters.Add(name.ToLowerInvariant() switch
            {
                "category" => Enum<RiskIndexCategory>(value, out var c) ? r => r.Dto.Category == c : Invalid(name),
                "state" => Enum<RiskIndexObjectState>(value, out var s) ? r => r.Dto.State == s : Invalid(name),
                "kind" => Enum<RiskIndexObjectKind>(value, out var k) ? r => r.Dto.Object.Object.Kind == k : Invalid(name),
                "assetclass" => Enum<RiskIndexAssetClass>(value, out var a) ? r => r.Dto.AssetClass == a.ToString() : Invalid(name),
                "source" => r => r.Dto.Sources.Contains(value, StringComparer.Ordinal),
                "criticality" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? r => r.Dto.Criticality == n
                    : Invalid(name),
                "environment" => r => string.Equals(r.Dto.Environment, value, StringComparison.Ordinal),
                // Matches the label the caller sees: a redacted scenario's title never matches (S40 §8.2).
                "label" => r => r.Dto.Object.Label.Contains(value, StringComparison.CurrentCultureIgnoreCase),
                "ispresumed" => bool.TryParse(value, out var b) ? r => r.Dto.IsPresumed == b : Invalid(name),
                _ => value switch
                {
                    "above_appetite" => r => r.Scenario is { AboveCeiling: true, AcceptedAboveCeiling: false },
                    "accepted_above_appetite" => r => r.Scenario is { AcceptedAboveCeiling: true },
                    "acceptance_expiring" => r => r.Scenario is { AcceptanceExpiringSoon: true },
                    _ => Invalid(name)
                }
            });
        }

        return filters;

        static Func<ObjectRow, bool> Invalid(string name) =>
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidFilter, $"The value of '{Trim(name)}' is not valid.");

        static bool Enum<T>(string value, out T parsed) where T : struct, System.Enum =>
            System.Enum.TryParse(value, ignoreCase: true, out parsed) && System.Enum.IsDefined(parsed);
    }

    private static List<(string Key, bool Descending)> ParseSorts(string? text)
    {
        var sorts = new List<(string, bool)>();
        if (string.IsNullOrWhiteSpace(text)) return sorts;
        foreach (var term in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var descending = term.StartsWith('-') || term.EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
            var key = term.TrimStart('-', '+').Split(' ')[0];
            if (!ObjectSortKeys.Contains(key))
                throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidSort, $"'{Trim(key)}' is not a sort of this list.");
            sorts.Add((key, descending));
        }

        return sorts;
    }

    private static string Trim(string value) => value.Length <= 40 ? value : value[..40];

    // ------------------------------------------------------------------------------------------------------------------
    // One object (S40 §5.9.2)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexObjectDetailDto> GetObjectAsync(RiskIndexObjectKind kind, string reference, string? node,
        DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(reference) || reference.Length > 64)
            throw RiskIndexException.NotFound(RiskIndexErrors.ObjectNotFound, "The object does not exist.");
        if (node is not null) ParseKey(node);

        var day = await ResolveDayAsync(date, ct);
        await using var db = dalService.GetContext();
        // Read through the caller's scope: an object out of scope is a 404, as if it did not exist (S40 §8.2).
        var rows = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == day.Date && o.ProfileId == day.Profile.Id && o.ObjectKind == kind && o.ObjectRef == reference)
            .OrderBy(o => o.Category)
            .ToListAsync(ct);
        if (rows.Count == 0) throw RiskIndexException.NotFound(RiskIndexErrors.ObjectNotFound, "The object does not exist.");

        var key = new IcrObjectKey(kind, reference);
        var label = (await RiskIndexLabels.ResolveAsync(db, [key], caller, ct))[key];
        var first = rows[0];

        string? author = null;
        if (first.CriticalityAuthorId is { } authorId)
            author = await db.Users.AsNoTracking().Where(u => u.Value == authorId).Select(u => u.Name).FirstOrDefaultAsync(ct);

        var categories = rows.Select(r =>
        {
            var factors = RiskIndexStoredJson.Deserialize<List<StoredFactor>>(r.Factors) ?? [];
            var signals = RiskIndexStoredJson.Deserialize<List<StoredSignal>>(r.Signals) ?? [];
            var m = IcrMath.Modulator(r.Criticality, day.P.MA);
            return new RiskIndexObjectCategoryDto(r.Category, r.RValue, r.State, r.Population.ToString(), (double)r.Weight,
                r.State == RiskIndexObjectState.Presumed ? RiskIndexContributionBranch.Presumption : RiskIndexContributionBranch.Mean,
                factors.Select(f =>
                {
                    var modulator = f.Type == RiskIndexFactorType.V ? m : 1;
                    var visible = RiskIndexRedactionPolicy.Holds(RiskIndexRedactionPolicy.SignalPermission(f.Factor), caller.IsAdmin,
                        caller.Permissions);
                    return new RiskIndexFactorDto(f.Factor, f.Type.ToString(), f.X, f.V, f.U,
                        f.U * IcrMath.EffectiveFactor(f.X * modulator, f.V, day.P.PiPrior, modulator), f.ReadAt, f.Source ?? "",
                        signals.Where(s => s.Factor == f.Factor)
                            .Select(s => new RiskIndexSignalDto(s.Value, s.Flags ?? [], visible ? s.Id : null, null)).ToList());
                }).ToList());
        }).ToList();

        // The register's inputs and decisions are the risk module's: only with riskmanagement (S40 §11.3).
        var mayReadRegister = RiskIndexRedactionPolicy.Holds(RiskIndexModulePermissions.RiskManagement, caller.IsAdmin,
            caller.Permissions);
        RiskIndexScenarioInputsDto? scenario = null;
        if (kind == RiskIndexObjectKind.Scenario && mayReadRegister &&
            RiskIndexStoredJson.ParseScenarioInputs(rows.FirstOrDefault(r => r.Category == RiskIndexCategory.Reg)?.ScenarioInputs) is { } s)
            scenario = new RiskIndexScenarioInputsDto(s.ImpactLevel ?? 0, s.Rank, s.RankedItems, s.Residual, s.Inherent, s.CadenceDays,
                (int)Math.Round(s.DelayDays, MidpointRounding.AwayFromZero), s.Credit, s.SigmaRef, s.EffectiveResidual);

        var attribution = RiskIndexStoredJson.ParseAttribution(first.Attribution).Where(day.Map.Contains).ToList();
        var names = await RiskIndexLabels.EntityNamesAsync(db, attribution, ct);
        var nodes = attribution.Select(e => new RiskIndexNodeRef(RiskIndexNodeKey.ForEntity(e).ToString(),
            names.GetValueOrDefault(e) ?? "#" + e.ToString(CultureInfo.InvariantCulture), day.Map.DefinitionOf(e) ?? "entity")).ToList();

        int? entityOfObject = kind is RiskIndexObjectKind.Application or RiskIndexObjectKind.TargetEntity &&
                              int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid) ? eid : null;
        var criticality = new RiskIndexCriticalityDto(null, null, 0, first.Criticality, first.CriticalitySource, author, null, false);

        return new RiskIndexObjectDetailDto(label, first.AssetClass?.ToString(), day.Date, criticality,
            entityOfObject is { } entity ? day.Map.InternetFacing(entity) : null, categories, scenario, null, nodes, [],
            Navigation(kind, reference, caller));
    }

    /// <summary>Only the destinations the caller can open (S40 §8.3, §10.3).</summary>
    private static IReadOnlyList<RiskIndexNavigationTargetDto> Navigation(RiskIndexObjectKind kind, string reference,
        RiskIndexCaller caller)
    {
        if (!int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return [];
        bool Has(string p) => RiskIndexRedactionPolicy.Holds(p, caller.IsAdmin, caller.Permissions);
        var targets = new List<RiskIndexNavigationTargetDto>();
        switch (kind)
        {
            case RiskIndexObjectKind.Scenario when Has(RiskIndexModulePermissions.RiskManagement):
                targets.Add(new(RiskIndexNavigationModule.Risk, id, "RiskNav_Risk"));
                break;
            case RiskIndexObjectKind.Host:
                if (Has(RiskIndexModulePermissions.Hosts)) targets.Add(new(RiskIndexNavigationModule.Host, id, "RiskNav_Host"));
                if (Has(RiskIndexModulePermissions.Vulnerabilities))
                    targets.Add(new(RiskIndexNavigationModule.HostFindings, id, "RiskNav_HostFindings"));
                break;
            case RiskIndexObjectKind.Application:
                if (Has(RiskIndexModulePermissions.Vulnerabilities))
                    targets.Add(new(RiskIndexNavigationModule.ApplicationFindings, id, "RiskNav_ApplicationFindings"));
                targets.Add(new(RiskIndexNavigationModule.Entity, id, "RiskNav_Entity"));
                break;
            case RiskIndexObjectKind.TargetEntity:
                targets.Add(new(RiskIndexNavigationModule.Entity, id, "RiskNav_Entity"));
                break;
        }

        return targets;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Inventory visibility (S40 §5.5.1)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The exposure sources an inventory tab can read, by tab (S39 §3.2; S40 §5.5).</summary>
    private static IReadOnlyList<string> TabSources(RiskIndexTabGroup tab) => tab switch
    {
        RiskIndexTabGroup.InternetFacing => [RiskIndexSources.SscTotal, RiskIndexSources.V1InternetFacing],
        RiskIndexTabGroup.Identities => [RiskIndexSources.V1Accounts],
        RiskIndexTabGroup.Clouds => [RiskIndexSources.V1Cloud],
        RiskIndexTabGroup.Applications => [RiskIndexSources.Importers, RiskIndexSources.V1LocalApps],
        _ =>
        [
            RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.TenableNessus, RiskIndexSources.TenableVm,
            RiskIndexSources.TenableAes, RiskIndexSources.Importers
        ]
    };

    public async Task<RiskIndexInventoryDto> GetInventoryAsync(string? node, RiskIndexTabGroup? tab, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (tab is not { } group || !Enum.IsDefined(group))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidTab,
                "The tab is Devices, InternetFacing, Identities, Applications or Clouds.");

        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        return await cache.GetOrCreateAsync(CacheKey("inventory", resolved, day, caller, ((int)group).ToString(CultureInfo.InvariantCulture)),
            RiskIndexReadCache.SnapshotTimeToLive, async () =>
            {
                var composition = await ComposeAsync(resolved, day, caller, ct);
                var exposure = composition.Rows.Where(r => r.Category == RiskIndexCategory.Exp && r.AssetClass is { } c &&
                                                           RiskIndexTabGroups.Of(c) == group).ToList();
                var keys = exposure.Select(r => r.Key).ToHashSet();

                await using var db = dalService.GetContext();
                var factors = (await db.RiskIndexObjectDays.AsNoTracking()
                        .Where(o => o.SnapshotDate == day.Date && o.ProfileId == day.Profile.Id && o.Category == RiskIndexCategory.Exp)
                        .Select(o => new { o.ObjectKind, o.ObjectRef, o.Factors }).ToListAsync(ct))
                    .Where(o => keys.Contains(new IcrObjectKey(o.ObjectKind, o.ObjectRef)))
                    .Select(o => RiskIndexStoredJson.Deserialize<List<StoredFactor>>(o.Factors) ?? [])
                    .ToList();

                var enabled = day.P.EnabledSources.ToHashSet(StringComparer.Ordinal);
                var sources = TabSources(group).Select(source =>
                {
                    if (!enabled.Contains(source))
                        return new RiskIndexSourceVisibilityDto(source, "N", false, 0, 0, 0, exposure.Count, null, "source_not_enabled");
                    int current = 0, stale = 0, expired = 0, none = 0;
                    DateTime? last = null;
                    foreach (var list in factors)
                    {
                        var reading = list.Where(f => f.Source == source).MaxBy(f => f.V);
                        if (reading is null) none++;
                        else if (reading.V >= 1) current++;
                        else if (reading.V > 0) stale++;
                        else expired++;
                        var at = list.Where(f => f.Source == source).Select(f => f.ReadAt).Max();
                        if (at is not null && (last is null || at > last)) last = at;
                    }

                    none += Math.Max(0, exposure.Count - factors.Count);
                    return new RiskIndexSourceVisibilityDto(source, "H", true, current, stale, expired, none, last, null);
                }).ToList();

                var overall = new RiskIndexStateCountsDto(
                    exposure.Count(r => r.State is RiskIndexObjectState.Assessed or RiskIndexObjectState.AssessedClean),
                    exposure.Count(r => r.State == RiskIndexObjectState.Stale),
                    exposure.Count(r => r.State == RiskIndexObjectState.Presumed),
                    exposure.Count(r => r.State == RiskIndexObjectState.NotAssessed));

                // The R of the assessed objects by band of 100·R: sums to Overall.Assessed (S40 §5.5.1).
                var distribution = Bands(day.P).ToDictionary(b => b.Key, _ => 0);
                foreach (var r in exposure.Where(r => r.State is RiskIndexObjectState.Assessed or RiskIndexObjectState.AssessedClean))
                {
                    var band = BandIndex(IcrMath.Displayed(100 * r.R), RiskIndexDisplayState.Point, day.P) ?? 0;
                    distribution[BandKey(band)]++;
                }

                return new RiskIndexInventoryDto(group, exposure.Count, "exp_objects", sources, overall, distribution);
            });
    }
}
