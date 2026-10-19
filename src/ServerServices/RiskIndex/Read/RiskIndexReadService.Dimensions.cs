using System.Globalization;
using DAL.Enums;
using Model.RiskIndex;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    /// <summary>A listed value is present among the node's visible objects; absent values are not listed.</summary>
    public const string DimensionPresent = "present";

    // ------------------------------------------------------------------------------------------------------------------
    // The values present in a node, per filter kind (S40 §8.2 GET /RiskIndex/Dimensions; T254)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexDimensionsDto> GetDimensionsAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var key = ParseKey(node);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(key, day, ct);
        return await cache.GetOrCreateAsync(CacheKey("dimensions", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () => Dimensions((await ComposeAsync(resolved, day, caller, ct)).Rows));
    }

    /// <summary>
    /// The pure part: the deduplicated objects of the node's (already scope-filtered) rows per value, with the same matching
    /// rules a key's filter term uses (<see cref="Matches"/>): a source counts on exposure rows, an environment only on
    /// hosts. Tab groups come first in tab order, then the single classes.
    /// </summary>
    internal static RiskIndexDimensionsDto Dimensions(IReadOnlyList<RiskIndexObjectRow> rows)
    {
        static int Objects(IEnumerable<RiskIndexObjectRow> subset) => subset.Select(r => r.Key).Distinct().Count();
        static RiskIndexDimensionValueDto Value(string term, string labelKey, int count) => new(term, labelKey, count, DimensionPresent);

        var classed = rows.Where(r => r.AssetClass is not null).ToList();
        var groups = RiskIndexTabMap.All
            .Select(tab => (Tab: tab, Count: Objects(classed.Where(r => RiskIndexTabGroups.Of(r.AssetClass!.Value) == tab))))
            .Where(x => x.Count > 0)
            .Select(x => Value("c:" + RiskIndexNodeKey.TabTerm(x.Tab), "RiskTab_" + x.Tab, x.Count));
        var classes = classed.GroupBy(r => r.AssetClass!.Value).OrderBy(g => g.Key)
            .Select(g => Value("c:" + RiskIndexNodeKey.ClassTerm(g.Key), "RiskAssetClass_" + g.Key, Objects(g)));

        var exposure = rows.Where(r => r.Category == RiskIndexCategory.Exp).ToList();
        var sources = RiskIndexSourceBits.Sources
            .Select(s => (Source: s, Count: Objects(exposure.Where(r => RiskIndexSourceBits.Has(r.SourceMask, s)))))
            .Where(x => x.Count > 0)
            .Select(x => Value("s:" + x.Source, "RiskSource_" + x.Source, x.Count));

        var criticalities = Enumerable.Range(1, 5)
            .Select(k => (K: k, Count: Objects(rows.Where(r => r.Criticality == k))))
            .Where(x => x.Count > 0)
            .Select(x => Value("k:" + x.K.ToString(CultureInfo.InvariantCulture), "RiskExploreCriticalityFMT", x.Count));

        var environments = rows.Where(r => r.Environment is not null && RiskIndexNodeKey.TryParse("a:" + r.Environment, out _))
            .GroupBy(r => r.Environment!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Value("a:" + g.Key, g.Key is "prod" or "none" ? "RiskEnvironment_" + g.Key : "RiskEnvironmentFMT", Objects(g)));

        return new RiskIndexDimensionsDto(groups.Concat(classes).ToList(), sources.ToList(), criticalities.ToList(),
            environments.ToList());
    }
}
