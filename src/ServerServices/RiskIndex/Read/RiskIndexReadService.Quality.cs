using System.Globalization;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    // ------------------------------------------------------------------------------------------------------------------
    // Data quality (S40 §5.12; S39 §8.5–§8.6) — always visible beside the index, never in it
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexQualityDto> GetQualityAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        return await cache.GetOrCreateAsync(CacheKey("quality", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () =>
            {
                // The counts need the node's visible object rows and their stored readings — one derived computation, under
                // one slot of the budget; coverage and freshness are the overview's own.
                var (composition, readings) = await budget.RunAsync(caller.UserId, async token =>
                {
                    await using var db = dalService.GetContext();
                    var composed = await ComposeWithinAsync(db, resolved, day, token);
                    return (composed, await ReadingsAsync(db, day, composed.Rows, token));
                }, ct);
                var values = resolved.Official
                    ? await ValuesAsync(resolved, day, caller, ct)
                    : DerivedValues(composition, day.P, DateTime.UtcNow);
                var organization = resolved is { Official: true, IsRoot: true, Key.HasFilters: false };
                return QualityOf(values, composition.Rows, day.Map, day.P, StaleSources(readings, day.P), organization);
            });
    }

    /// <summary>
    /// The pure half of <see cref="GetQualityAsync"/>. Unassigned is the share of hosts and applications attributed to no
    /// entity of the map (the "u" node, S39 §8.5); default criticality the share of every object of the node; both by the
    /// object's weight. The tree counts come from the organization's stored data-quality chip, at the root only.
    /// </summary>
    internal static RiskIndexQualityDto QualityOf(NodeValues values, IReadOnlyList<RiskIndexObjectRow> rows, EntityMap map,
        RiskIndexParameters p, IReadOnlyList<RiskIndexStaleSourceDto> staleSources, bool organization)
    {
        var summary = Quality(values, p);

        var assets = rows.Where(r => r.Category is RiskIndexCategory.Exp or RiskIndexCategory.Gov &&
                                     r.Key.Kind is RiskIndexObjectKind.Host or RiskIndexObjectKind.Application or
                                         RiskIndexObjectKind.UnassignedGroup)
            .DistinctBy(r => r.Key).ToList();
        var unassigned = Share(assets, r => !r.Attribution.Any(map.Contains));

        var objects = rows.DistinctBy(r => r.Key).ToList();
        var defaults = Share(objects, r => string.Equals(r.CriticalitySource, RiskIndexQualityFilters.DefaultCriticalitySource,
            StringComparison.Ordinal));

        var presumedCritical = rows.Where(r => r.State == RiskIndexObjectState.Presumed &&
                                                r.Criticality >= RiskIndexQualityFilters.CriticalFrom)
            .Select(r => r.Key).Distinct().Count();
        var residualAboveInherent = rows.Count(r => r.Category == RiskIndexCategory.Reg &&
                                                    r.ScenarioInputs is { ResidualAboveInherent: true });

        var stored = organization
            ? values.Chips.FirstOrDefault(c => c.Key == RiskIndexChipKeys.DataQuality)?.Args
            : null;

        return new RiskIndexQualityDto(summary.Coverage, summary.Freshness, summary.Quality, summary.QualityBandKey, unassigned,
            unassigned > p.UnassignedThreshold, defaults, staleSources, presumedCritical, Count(stored, "divergent_links"),
            Count(stored, "orphan_applications"), null, null, Count(stored, "legacy_lifecycle_disagreements"),
            residualAboveInherent, RiskIndexQualityFilters.EvidenceConfidenceRequiresM40);

        static double Share(IReadOnlyList<RiskIndexObjectRow> population, Func<RiskIndexObjectRow, bool> predicate)
        {
            var total = population.Sum(r => r.Weight);
            return total > 0 ? population.Where(predicate).Sum(r => r.Weight) / total : 0;
        }

        static int? Count(IReadOnlyDictionary<string, string>? args, string name) =>
            args is not null && args.TryGetValue(name, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : null;
    }

    /// <summary>
    /// "Fontes obsoletas" (S39 §8.6): an enabled source with readings in the node of which none is current (every
    /// <c>v &lt; 1</c>), with the date of its freshest reading. A source with no reading in the node is not listed.
    /// </summary>
    internal static IReadOnlyList<RiskIndexStaleSourceDto> StaleSources(IEnumerable<StoredFactor> readings, RiskIndexParameters p)
    {
        var enabled = p.EnabledSources.ToHashSet(StringComparer.Ordinal);
        return readings.Where(f => f.Source is { Length: > 0 } source && enabled.Contains(source))
            .GroupBy(f => f.Source!, StringComparer.Ordinal)
            .Where(g => g.All(f => f.V < 1))
            .Select(g => new RiskIndexStaleSourceDto(g.Key, g.Max(f => f.ReadAt)))
            .OrderBy(s => s.SourceKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The stored factor readings of the node's visible (object, category) rows; the caller's hidden rows never enter.</summary>
    private static async Task<IReadOnlyList<StoredFactor>> ReadingsAsync(NRDbContext db, Day day,
        IReadOnlyList<RiskIndexObjectRow> rows, CancellationToken ct)
    {
        var keys = rows.Select(r => (r.Key, r.Category)).ToHashSet();
        var stored = await ObjectDaysOf(db, day, keys)
            .Select(o => new { o.ObjectKind, o.ObjectRef, o.Category, o.Factors })
            .ToListAsync(ct);
        return stored.Where(o => keys.Contains((new IcrObjectKey(o.ObjectKind, o.ObjectRef), o.Category)))
            .SelectMany(o => RiskIndexStoredJson.Deserialize<List<StoredFactor>>(o.Factors) ?? [])
            .ToList();
    }
}
