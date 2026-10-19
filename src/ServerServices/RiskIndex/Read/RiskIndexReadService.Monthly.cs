using System.Globalization;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    public static readonly IReadOnlyList<int> MonthlyWindows = [6, 12];

    // ------------------------------------------------------------------------------------------------------------------
    // The monthly category summary (S40 §5.5.1, §8.2; T253)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexMonthlySummaryDto> GetMonthlySummaryAsync(string? node, RiskIndexTabGroup? tab, int months,
        DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (!MonthlyWindows.Contains(months))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidMonths, "The summary covers 6 or 12 months.");
        if (tab is { } given && !Enum.IsDefined(given))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidTab,
                "The tab is Devices, InternetFacing, Identities, Applications or Clouds.");

        var key = ParseKey(node);
        if (tab is { } group) EnsureTabFits(key, group);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(key, day, ct);

        var extra = string.Create(CultureInfo.InvariantCulture, $"{(tab is { } t ? (int)t : 0)}|{months}");
        return await cache.GetOrCreateAsync(CacheKey("monthly", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            () => MonthlySummaryAsync(resolved, day, tab, months, caller, ct));
    }

    /// <summary>
    /// A tab cuts the node by asset class (S40 §5.5): a node already cut by another class has no such intersection, and the
    /// GUI disables those tabs ("O nó atual já é recortado por ‹classe›") — the API refuses them the same way.
    /// </summary>
    internal static void EnsureTabFits(RiskIndexNodeKey key, RiskIndexTabGroup tab)
    {
        if ((key.AssetClass is { } c && RiskIndexTabGroups.Of(c) != tab) || (key.TabGroup is { } t && t != tab))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidTab, "The node is already cut by another asset class.");
    }

    /// <summary>The node ∩ tab; a node already cut by a class of the tab (<see cref="EnsureTabFits"/>) is its own intersection.</summary>
    internal static Node IntersectTab(Node node, RiskIndexTabGroup tab) =>
        node.Key.AssetClass is null && node.Key.TabGroup is null ? node with { Key = node.Key.WithTabGroup(tab) } : node;

    /// <summary>One stored or recomposed day: its profile, the available <c>C_k</c> values and the applicable categories.</summary>
    internal sealed record MonthlyDay(DateOnly Date, int ProfileId, IReadOnlyDictionary<RiskIndexCategory, double> Values,
        IReadOnlyCollection<RiskIndexCategory> Applicable);

    private async Task<RiskIndexMonthlySummaryDto> MonthlySummaryAsync(Node node, Day day, RiskIndexTabGroup? tab, int months,
        RiskIndexCaller caller, CancellationToken ct)
    {
        var firstMonth = new DateOnly(day.Date.Year, day.Date.Month, 1).AddMonths(1 - months);

        // Pre-computed nodes read their stored days and never the object rows (S40 §8.1, §10.9): the node's category rows,
        // or the job's node ∩ tab values. Class days exist for the root and entity nodes only.
        var storedClassDays = node.Official && node.OfficialKind is RiskIndexScopeKind.Organization or RiskIndexScopeKind.Entity;
        IReadOnlyList<MonthlyDay> days;
        RiskIndexProvenance provenance;
        if (tab is { } group && storedClassDays)
        {
            days = await ClassDaysAsync(node, day, group, firstMonth, ct);
            provenance = RiskIndexProvenance.OfficialDerived;
        }
        else if (tab is null && node.Official)
        {
            days = await SnapshotCategoryDaysAsync(node, day, firstMonth, ct);
            provenance = RiskIndexProvenance.Official;
        }
        else
        {
            days = await DerivedMonthlyDaysAsync(node, day, tab, firstMonth, caller, ct);
            provenance = node.Provenance;
        }

        var series = tab is { } g
            ? RiskIndexTabGroups.Categories(g).Where(c => IcrCategoryCalculator.Omega(day.P, c) > 0).ToHashSet()
            : days.Count > 0
                ? days.SelectMany(d => d.Applicable).ToHashSet()
                : ChartOrder.Where(c => IcrCategoryCalculator.Omega(day.P, c) > 0).ToHashSet();
        return BuildMonthlySummary(tab, ChartOrder.Where(series.Contains).ToList(), days, firstMonth, months, day.Date, provenance);
    }

    /// <summary>The categories in the fixed order of every chart and table of the module (S40 §5.5.1, §10.4).</summary>
    internal static readonly IReadOnlyList<RiskIndexCategory> ChartOrder =
    [
        RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl, RiskIndexCategory.Gov,
        RiskIndexCategory.Ter
    ];

    /// <summary>The pure half: month buckets, means, the partial month and the months that mix profiles.</summary>
    internal static RiskIndexMonthlySummaryDto BuildMonthlySummary(RiskIndexTabGroup? tab, IReadOnlyList<RiskIndexCategory> series,
        IReadOnlyList<MonthlyDay> days, DateOnly firstMonth, int months, DateOnly resolved, RiskIndexProvenance provenance)
    {
        var list = new List<RiskIndexMonthDto>(months);
        for (var i = 0; i < months; i++)
        {
            var start = firstMonth.AddMonths(i);
            var end = start.AddMonths(1).AddDays(-1);
            var inMonth = days.Where(d => d.Date >= start && d.Date <= end && d.Date <= resolved).ToList();
            var means = series.ToDictionary(c => c, c =>
            {
                var values = inMonth.Where(d => d.Values.ContainsKey(c)).Select(d => d.Values[c]).ToList();
                return values.Count == 0 ? (double?)null : values.Average();
            });
            list.Add(new RiskIndexMonthDto(start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                resolved >= start && resolved < end, inMonth.Select(d => d.ProfileId).Distinct().Count() > 1, means));
        }

        return new RiskIndexMonthlySummaryDto(tab, series, list, provenance);
    }

    /// <summary>The days with a snapshot in the window and each one's profile (the job writes a day with one profile).</summary>
    private static async Task<Dictionary<DateOnly, int>> SnapshotDaysAsync(NRDbContext db, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        var dates = await db.RiskIndexNodeMembers.AsNoTracking()
            .Where(m => m.SnapshotDate >= from && m.SnapshotDate <= to)
            .Select(m => new { m.SnapshotDate, m.ProfileId }).Distinct().ToListAsync(ct);
        if (dates.Count == 0)
            dates = await db.RiskIndexSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate >= from && s.SnapshotDate <= to)
                .Select(s => new { s.SnapshotDate, s.ProfileId }).Distinct().ToListAsync(ct);
        return dates.GroupBy(d => d.SnapshotDate).ToDictionary(g => g.Key, g => g.Max(d => d.ProfileId));
    }

    /// <summary>The node ∩ tab days the job stored (<c>risk_index_class_days</c>), on the days the claims covered the node.</summary>
    private async Task<IReadOnlyList<MonthlyDay>> ClassDaysAsync(Node node, Day day, RiskIndexTabGroup tab, DateOnly from,
        CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var covered = await CoveredDatesAsync(db, day, node, from, day.Date, ct);
        var profiles = await SnapshotDaysAsync(db, from, day.Date, ct);
        var query = db.RiskIndexClassDays.AsNoTracking()
            .Where(c => c.TabGroup == tab && c.SnapshotDate >= from && c.SnapshotDate <= day.Date);
        query = node.EntityId is { } id ? query.Where(c => c.NodeEntityId == id) : query.Where(c => c.NodeEntityId == null);
        var rows = await query.Select(c => new { c.SnapshotDate, c.ProfileId, c.Category, c.Value }).ToListAsync(ct);

        return rows.Where(r => covered is null || covered.Contains(r.SnapshotDate))
            .Where(r => !profiles.TryGetValue(r.SnapshotDate, out var p) || p == r.ProfileId)
            .GroupBy(r => r.SnapshotDate)
            .Select(g => new MonthlyDay(g.Key, g.First().ProfileId, g.ToDictionary(r => r.Category, r => r.Value),
                g.Select(r => r.Category).ToList()))
            .OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// The official node's own category rows, on the days the claims covered it (S40 §11.3). A profile activated mid-day
    /// leaves two row sets on one date (the job is idempotent per date and profile): only the day's profile is kept.
    /// </summary>
    private async Task<IReadOnlyList<MonthlyDay>> SnapshotCategoryDaysAsync(Node node, Day day, DateOnly from, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var covered = await CoveredDatesAsync(db, day, node, from, day.Date, ct);
        var profiles = await SnapshotDaysAsync(db, from, day.Date, ct);
        var rows = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ScopeKind == node.OfficialKind && s.ScopeRef == node.OfficialRef && s.SnapshotDate >= from &&
                        s.SnapshotDate <= day.Date && s.Category != RiskIndexCategory.Icr)
            .Select(s => new { s.SnapshotDate, s.ProfileId, s.Category, s.CategoryState, s.ValueUnfloored })
            .ToListAsync(ct);

        return rows.Where(r => covered is null || covered.Contains(r.SnapshotDate))
            .Where(r => !profiles.TryGetValue(r.SnapshotDate, out var p) || p == r.ProfileId)
            .GroupBy(r => r.SnapshotDate)
            .Select(g => new MonthlyDay(g.Key, g.First().ProfileId,
                g.Where(r => r.CategoryState == RiskIndexCategoryState.Available).ToDictionary(r => r.Category, r => r.ValueUnfloored),
                g.Where(r => r.CategoryState is RiskIndexCategoryState.Available or RiskIndexCategoryState.Unavailable)
                    .Select(r => r.Category).ToList()))
            .OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// A derived or partial node, recomposed day by day from the visible object rows under the budget (S40 §8.1). A month
    /// that ended before the requested day never changes, so its days are cached by (node, tab, month, scope, permissions):
    /// a retry after <c>derived_computation_timeout</c> resumes from the months already done.
    /// </summary>
    private Task<IReadOnlyList<MonthlyDay>> DerivedMonthlyDaysAsync(Node node, Day day, RiskIndexTabGroup? tab, DateOnly firstMonth,
        RiskIndexCaller caller, CancellationToken ct)
    {
        var target = tab is { } g ? IntersectTab(node, g) : node;
        var categories = tab is { } t ? RiskIndexTabGroups.Categories(t) : ChartOrder;
        var tabKey = tab is { } k ? ((int)k).ToString(CultureInfo.InvariantCulture) : "all";
        return budget.RunAsync<IReadOnlyList<MonthlyDay>>(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            var profiles = await SnapshotDaysAsync(db, firstMonth, day.Date, token);
            var parameters = new Dictionary<int, (DAL.Entities.RiskIndexProfile Profile, RiskIndexParameters P)>();
            var result = new List<MonthlyDay>();
            for (var month = firstMonth; month <= day.Date; month = month.AddMonths(1))
            {
                var end = month.AddMonths(1).AddDays(-1);
                var complete = end < day.Date;
                var monthDays = profiles.Where(p => p.Key >= month && p.Key <= end && p.Key <= day.Date).OrderBy(p => p.Key).ToList();

                async Task<Box<IReadOnlyList<MonthlyDay>>> Compute()
                {
                    var list = new List<MonthlyDay>();
                    foreach (var (date, profileId) in monthDays)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!parameters.TryGetValue(profileId, out var entry))
                        {
                            var profile = await db.RiskIndexProfiles.AsNoTracking().FirstAsync(x => x.Id == profileId, token);
                            parameters[profileId] = entry = (profile, RiskIndexParametersJson.Deserialize(profile.Parameters));
                        }

                        var set = await RiskIndexStoredDays.LoadSetAsync(db, date, profileId, entry.P, day.Map, token);
                        var r = Compose(target, day with { Date = date, Profile = entry.Profile, P = entry.P }, set).Result;
                        var inTab = r.Categories.Where(c => categories.Contains(c.Category)).ToList();
                        list.Add(new MonthlyDay(date, profileId,
                            inTab.Where(c => c.Status == IcrCategoryStatus.Available).ToDictionary(c => c.Category, c => c.Value),
                            inTab.Where(c => c.Status is IcrCategoryStatus.Available or IcrCategoryStatus.Unavailable)
                                .Select(c => c.Category).ToList()));
                    }

                    return new Box<IReadOnlyList<MonthlyDay>>(list);
                }

                // The tab is in the key: on a node already cut by it, the target is the same with and without it.
                var days = complete
                    ? await cache.GetOrCreateAsync(string.Join('|', "monthly-days", target.Key.ToString(), tabKey,
                        month.ToString("yyyy-MM", CultureInfo.InvariantCulture), day.ScopeKey, caller.PermissionKey),
                        RiskIndexReadCache.SnapshotTimeToLive, Compute)
                    : await Compute();
                result.AddRange(days.Value);
            }

            return result;
        }, ct);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Risk factors of an asset-class tab (S40 §5.5.1, §8.2; T253)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RiskIndexRiskFactorDto>> GetRiskFactorsAsync(string? node, RiskIndexTabGroup? tab,
        DateOnly? date, RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (tab is not { } group || !Enum.IsDefined(group))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidTab,
                "The tab is Devices, InternetFacing, Identities, Applications or Clouds.");

        var key = ParseKey(node);
        EnsureTabFits(key, group);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(key, day, ct);
        var extra = ((int)group).ToString(CultureInfo.InvariantCulture);
        var lines = await cache.GetOrCreateAsync(CacheKey("factors", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            async () => new Box<IReadOnlyList<RiskIndexRiskFactorDto>>(await RiskFactorsAsync(resolved, day, group, caller, ct)));
        return lines.Value;
    }

    /// <summary>The composition and the factor JSON it splits by are one derived computation: one slot of the budget.</summary>
    private Task<IReadOnlyList<RiskIndexRiskFactorDto>> RiskFactorsAsync(Node node, Day day, RiskIndexTabGroup tab,
        RiskIndexCaller caller, CancellationToken ct) =>
        budget.RunAsync(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            var composition = await ComposeWithinAsync(db, IntersectTab(node, tab), day, token);
            var points = ObjectPoints(composition.Result);
            var categories = RiskIndexTabGroups.Categories(tab);
            var rows = composition.Rows.Where(r => categories.Contains(r.Category)).ToList();

            var stored = await StoredFactorsAsync(db, day, rows.Select(r => (r.Key, r.Category)).ToList(), token);
            return RiskIndexFactorAttribution.Aggregate(rows.Select(r =>
            {
                stored.TryGetValue((r.Key, r.Category), out var json);
                var split = RiskIndexFactorAttribution.Split(r.Category, r.State, r.R, r.Criticality, json.Factors ?? [],
                    json.Signals ?? [], day.P);
                return new RiskIndexFactorAttribution.Attributed(r.Key, points.GetValueOrDefault((r.Category, r.Key)), split);
            }));
        }, ct);

    /// <summary>
    /// Each object's Euler points in each category (S39 §4.8): the category's gain <c>g_k = contribution / C_k</c> times
    /// the object's part of <c>C_k</c> — what the object list shows as contribution and the factor lines add up.
    /// </summary>
    internal static Dictionary<(RiskIndexCategory, IcrObjectKey), double> ObjectPoints(IcrNodeResult result)
    {
        var gains = result.Categories.Where(c => c.Status == IcrCategoryStatus.Available && c.Value > 0)
            .ToDictionary(c => c.Category, c => result.CategoryContributions.GetValueOrDefault(c.Category) / c.Value);
        return result.Categories.SelectMany(c => c.Parts.Select(p => (c.Category, p.Key, p.Part)))
            .GroupBy(x => (x.Category, x.Key))
            .ToDictionary(g => g.Key, g => gains.GetValueOrDefault(g.Key.Category) * g.Sum(x => x.Part));
    }

    /// <summary>The factor and signal JSON of the day's <paramref name="wanted"/> (object, category) rows, through the caller's scope.</summary>
    private static async Task<Dictionary<(IcrObjectKey, RiskIndexCategory), (List<StoredFactor>? Factors, List<StoredSignal>? Signals)>>
        StoredFactorsAsync(NRDbContext db, Day day, IReadOnlyCollection<(IcrObjectKey Key, RiskIndexCategory Category)> wanted,
            CancellationToken ct)
    {
        var pairs = wanted.ToHashSet();
        var rows = await ObjectDaysOf(db, day, pairs)
            .Select(o => new { o.ObjectKind, o.ObjectRef, o.Category, o.Factors, o.Signals })
            .ToListAsync(ct);
        var result = new Dictionary<(IcrObjectKey, RiskIndexCategory), (List<StoredFactor>?, List<StoredSignal>?)>();
        foreach (var row in rows)
        {
            var key = new IcrObjectKey(row.ObjectKind, row.ObjectRef);
            if (!pairs.Contains((key, row.Category))) continue;
            result[(key, row.Category)] = (RiskIndexStoredJson.Deserialize<List<StoredFactor>>(row.Factors),
                RiskIndexStoredJson.Deserialize<List<StoredSignal>>(row.Signals));
        }

        return result;
    }

    /// <summary>A node with at most this many objects has its stored rows picked by reference in SQL, not only by kind.</summary>
    internal const int SqlRefLimit = 1000;

    /// <summary>
    /// The day's object rows narrowed in SQL to the categories and kinds of <paramref name="wanted"/> — and to its references,
    /// when there are at most <see cref="SqlRefLimit"/> — so a node's factor JSON is read without the whole day's.
    /// The caller still matches the exact (object, category) pairs.
    /// </summary>
    internal static IQueryable<DAL.Entities.RiskIndexObjectDay> ObjectDaysOf(NRDbContext db, Day day,
        IReadOnlyCollection<(IcrObjectKey Key, RiskIndexCategory Category)> wanted)
    {
        var categories = wanted.Select(w => w.Category).Distinct().ToList();
        var kinds = wanted.Select(w => w.Key.Kind).Distinct().ToList();
        var query = db.RiskIndexObjectDays.AsNoTracking().Where(o => o.SnapshotDate == day.Date && o.ProfileId == day.Profile.Id &&
                                                                     categories.Contains(o.Category) && kinds.Contains(o.ObjectKind));
        var refs = wanted.Select(w => w.Key.Ref).Distinct().ToList();
        return refs.Count <= SqlRefLimit ? query.Where(o => refs.Contains(o.ObjectRef)) : query;
    }
}
