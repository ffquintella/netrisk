using System.Globalization;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    public static readonly IReadOnlyList<int> TrendWindows = [30, 90, 365];

    /// <summary>
    /// The trend (S39 §9.3; S40 §5.3): the node's daily ICR* (or <c>C_k</c>) over 30, 90 or 365 days, with gaps, the EWMA
    /// restarting at every marker, the bands of each profile's stretch, the markers with the version delta of a bridge and
    /// the bridge's overlay. Official nodes read their rows; derived nodes recompose each day (30 or 90 days, under the
    /// budget); a partial node has no trend.
    /// </summary>
    public async Task<RiskIndexTrendDto> GetTrendAsync(string? node, int window, RiskIndexCategory series, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        if (!TrendWindows.Contains(window))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidWindow, "The window is 30, 90 or 365 days.");
        if (!Enum.IsDefined(series))
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidSeries, "The series is ICR (0) or a category (1–6).");

        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        if (resolved.Partial)
            throw RiskIndexException.Conflict(RiskIndexErrors.TrendUnavailablePartial,
                "A partial view has no trend: the series needs the node's whole object set.");
        if (resolved.Derived && window == 365)
            throw RiskIndexException.BadRequest(RiskIndexErrors.WindowRequiresPrecomputedNode,
                "365 days are available for pre-computed nodes only.");

        var extra = string.Create(CultureInfo.InvariantCulture, $"{window}|{(int)series}");
        return await cache.GetOrCreateAsync(CacheKey("trend", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            () => TrendAsync(resolved, day, window, series, caller, ct));
    }

    private sealed record TrendDay(DateOnly Date, int ProfileId, double? Value, int? Displayed, RiskIndexDisplayState State,
        double? Low, double? High, IReadOnlyList<RiskIndexCategory> Unavailable, IReadOnlyList<StoredMarker> Markers,
        StoredChangeAttribution? Change);

    private async Task<RiskIndexTrendDto> TrendAsync(Node node, Day day, int window, RiskIndexCategory series,
        RiskIndexCaller caller, CancellationToken ct)
    {
        var from = day.Date.AddDays(1 - window);
        var days = node.Official
            ? await OfficialTrendAsync(node, day, from, series, ct)
            : await DerivedTrendAsync(node, day, from, series, caller, ct);

        await using var db = dalService.GetContext();
        var profileIds = days.Select(d => d.ProfileId).Distinct().ToList();
        var profiles = await db.RiskIndexProfiles.AsNoTracking().Where(p => profileIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // The three largest shares of "what moved" on a day the value moved by at least delta_detalhe (S40 §5.3).
        var movers = days.Where(d => d.Change is not null && Math.Abs(d.Change.Change ?? 0) >= day.P.DetailDelta)
            .ToDictionary(d => d.Date, d => d.Change!.Entries.OrderByDescending(e => Math.Abs(e.Risk + e.Context)).Take(3).ToList());
        var labels = await RiskIndexLabels.ResolveAsync(db,
            movers.Values.SelectMany(e => e).Select(e => new IcrObjectKey(e.Object.Kind, e.Object.Ref)), caller, ct);

        var byDate = days.ToDictionary(d => d.Date);
        var points = new List<RiskIndexTrendPointDto>();
        double? ewma = null, last = null;
        int? lastDisplayed = null;
        var alpha = 2.0 / (day.P.EwmaN + 1);
        for (var d = from; d <= day.Date; d = d.AddDays(1))
        {
            if (!byDate.TryGetValue(d, out var t))
            {
                // A day without a snapshot repeats the last value as a gap (S39 §9.3).
                if (last is not null)
                {
                    ewma = ewma is null ? last : alpha * last.Value + (1 - alpha) * ewma.Value;
                    points.Add(new RiskIndexTrendPointDto(d, lastDisplayed, last, lastDisplayed, ewma, RiskIndexDisplayState.NoData,
                        null, null, [], true, false, null, []));
                }

                continue;
            }

            if (t.Markers.Count > 0) ewma = null; // the EWMA restarts at every discontinuity
            int? delta = t.Displayed is { } v && lastDisplayed is { } p && t.Markers.Count == 0 ? v - p : null;
            if (t.Value is { } value) ewma = ewma is null ? value : alpha * value + (1 - alpha) * ewma.Value;

            var top = movers.TryGetValue(d, out var entries)
                ? entries.Select(e => AttributionRow(e, labels, t.Change!.Change)).ToList()
                : [];
            points.Add(new RiskIndexTrendPointDto(d, t.Displayed, t.Value, t.Displayed, t.Value is null ? null : ewma, t.State,
                t.Low, t.High, t.Unavailable, false, false, delta, top));

            if (t.Value is not null)
            {
                last = t.Value;
                lastDisplayed = t.Displayed;
            }
        }

        var segments = new List<RiskIndexBandSegmentDto>();
        foreach (var run in Runs(days))
            if (profiles.TryGetValue(run.ProfileId, out var profile))
                segments.Add(new RiskIndexBandSegmentDto(run.From, run.To, profile.Version,
                    Bands(RiskIndexParametersJson.Deserialize(profile.Parameters))));

        var bridges = node.Official
            ? await db.RiskIndexBridges.AsNoTracking()
                .Where(b => profileIds.Contains(b.ToProfileId) && b.ScopeKind == node.OfficialKind && b.ScopeRef == node.OfficialRef &&
                            b.Category == series && b.BridgeDate >= from && b.BridgeDate <= day.Date)
                .ToListAsync(ct)
            : [];
        var markers = days.SelectMany(d => d.Markers.Select(m => new RiskIndexMarkerDto(d.Date, m.Kind, m.Args,
            m.Kind == RiskIndexMarkerKind.Profile && bridges.FirstOrDefault(b => b.BridgeDate == d.Date) is { } bridge
                ? bridge.ValueNew - bridge.ValueOld
                : null))).ToList();
        var overlays = bridges.Select(b => new RiskIndexOverlayDto(
            profiles.TryGetValue(b.FromProfileId, out var a) ? a.Version : 0,
            profiles.TryGetValue(b.ToProfileId, out var z) ? z.Version : 0, b.OverlayPartial,
            (RiskIndexStoredJson.Deserialize<List<StoredOverlayPoint>>(b.Overlay) ?? [])
            .Where(o => o.Date >= from).Select(o => new RiskIndexOverlayPointDto(o.Date, o.Value)).ToList())).ToList();

        return new RiskIndexTrendDto(node.Ref, window, series, points, segments, markers, [], [], overlays, null, null);
    }

    private static IEnumerable<(int ProfileId, DateOnly From, DateOnly To)> Runs(IReadOnlyList<TrendDay> days)
    {
        (int ProfileId, DateOnly From, DateOnly To)? run = null;
        foreach (var d in days.OrderBy(d => d.Date))
        {
            if (run is { } r && r.ProfileId == d.ProfileId)
            {
                run = (r.ProfileId, r.From, d.Date);
                continue;
            }

            if (run is not null) yield return run.Value;
            run = (d.ProfileId, d.Date, d.Date);
        }

        if (run is not null) yield return run.Value;
    }

    private async Task<List<TrendDay>> OfficialTrendAsync(Node node, Day day, DateOnly from, RiskIndexCategory series,
        CancellationToken ct)
    {
        var to = day.Date;
        await using var db = dalService.GetContext();
        // A day whose members the claims did not cover is a gap, never a value (S40 §11.3).
        var covered = await CoveredDatesAsync(db, day, node, from, to, ct);
        // The idx_risk_index_snapshots_scope_category_date read (S40 §7.1): the node's rows of the window, every category,
        // so a day can say which categories were unavailable.
        var rows = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ScopeKind == node.OfficialKind && s.ScopeRef == node.OfficialRef && s.SnapshotDate >= from &&
                        s.SnapshotDate <= to)
            .Select(s => new
            {
                s.SnapshotDate, s.ProfileId, s.Category, s.ValueUnfloored, s.ValueDisplayed, s.DisplayState, s.CategoryState,
                s.IntervalLow, s.IntervalHigh, s.Markers, s.ChangeAttribution
            })
            .ToListAsync(ct);

        return rows.Where(r => covered is null || covered.Contains(r.SnapshotDate)).GroupBy(r => r.SnapshotDate).Select(g =>
        {
            var head = g.FirstOrDefault(r => r.Category == RiskIndexCategory.Icr);
            var row = g.FirstOrDefault(r => r.Category == series);
            var unavailable = g.Where(r => r.CategoryState == RiskIndexCategoryState.Unavailable).Select(r => r.Category).ToList();
            var markers = RiskIndexStoredJson.ParseMarkers(head?.Markers);
            var change = series == RiskIndexCategory.Icr ? RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(head?.ChangeAttribution) : null;
            if (row is null) return new TrendDay(g.Key, g.First().ProfileId, null, null, RiskIndexDisplayState.NoData, null, null, unavailable, markers, change);

            if (series == RiskIndexCategory.Icr)
            {
                var noData = row.DisplayState == RiskIndexDisplayState.NoData;
                return new TrendDay(g.Key, row.ProfileId, noData ? null : row.ValueUnfloored, noData ? null : row.ValueDisplayed,
                    row.DisplayState ?? RiskIndexDisplayState.NoData, row.IntervalLow, row.IntervalHigh, unavailable, markers, change);
            }

            // A category that was not available that day is a gap in its series (S40 §5.3).
            var available = row.CategoryState == RiskIndexCategoryState.Available;
            return new TrendDay(g.Key, row.ProfileId, available ? row.ValueUnfloored : null, available ? row.ValueDisplayed : null,
                available ? RiskIndexDisplayState.Point : RiskIndexDisplayState.NoData, row.IntervalLow, row.IntervalHigh,
                unavailable, markers, null);
        }).OrderBy(d => d.Date).ToList();
    }

    private Task<List<TrendDay>> DerivedTrendAsync(Node node, Day day, DateOnly from, RiskIndexCategory series,
        RiskIndexCaller caller, CancellationToken ct) =>
        budget.RunAsync(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            var dates = await db.RiskIndexNodeMembers.AsNoTracking()
                .Where(m => m.SnapshotDate >= from && m.SnapshotDate <= day.Date)
                .Select(m => new { m.SnapshotDate, m.ProfileId }).Distinct().ToListAsync(token);
            if (dates.Count == 0)
                dates = await db.RiskIndexSnapshots.AsNoTracking()
                    .Where(s => s.SnapshotDate >= from && s.SnapshotDate <= day.Date)
                    .Select(s => new { s.SnapshotDate, s.ProfileId }).Distinct().ToListAsync(token);

            var profiles = new Dictionary<int, RiskIndexProfile>();
            var result = new List<TrendDay>();
            foreach (var d in dates.OrderBy(d => d.SnapshotDate))
            {
                token.ThrowIfCancellationRequested();
                if (!profiles.TryGetValue(d.ProfileId, out var profile))
                    profiles[d.ProfileId] = profile = await db.RiskIndexProfiles.AsNoTracking().FirstAsync(p => p.Id == d.ProfileId, token);
                var p = RiskIndexParametersJson.Deserialize(profile.Parameters);
                var set = await RiskIndexStoredDays.LoadSetAsync(db, d.SnapshotDate, d.ProfileId, p, day.Map, token);
                var composition = Compose(node, day with { Date = d.SnapshotDate, Profile = profile, P = p }, set);
                var r = composition.Result;
                var unavailable = r.Categories.Where(c => c.Status == IcrCategoryStatus.Unavailable).Select(c => c.Category).ToList();
                if (series == RiskIndexCategory.Icr)
                {
                    result.Add(new TrendDay(d.SnapshotDate, d.ProfileId, r.Unfloored, r.Displayed,
                        (RiskIndexDisplayState)(int)r.DisplayState, r.IntervalLow, r.IntervalHigh, unavailable, [], null));
                    continue;
                }

                var c = r.Categories.FirstOrDefault(x => x.Category == series);
                var ok = c is { Status: IcrCategoryStatus.Available };
                result.Add(new TrendDay(d.SnapshotDate, d.ProfileId, ok ? c!.Value : null, ok ? IcrMath.Displayed(c!.Value) : null,
                    ok ? RiskIndexDisplayState.Point : RiskIndexDisplayState.NoData, ok ? c!.IntervalLow : null,
                    ok ? c!.IntervalHigh : null, unavailable, [], null));
            }

            // A derived node marks a profile change where its days' profile changes (S39 §9.5).
            for (var i = 1; i < result.Count; i++)
                if (result[i].ProfileId != result[i - 1].ProfileId)
                    result[i] = result[i] with
                    {
                        Markers = [new StoredMarker(RiskIndexMarkerKind.Profile, new Dictionary<string, string>
                        {
                            ["from_profile_id"] = result[i - 1].ProfileId.ToString(CultureInfo.InvariantCulture),
                            ["to_profile_id"] = result[i].ProfileId.ToString(CultureInfo.InvariantCulture)
                        })]
                    };
            return result;
        }, ct);

    private static RiskIndexAttributionRowDto AttributionRow(StoredAttributionEntry e,
        IReadOnlyDictionary<IcrObjectKey, RiskIndexObjectLabelDto> labels, double? change)
    {
        var points = e.Risk + e.Context;
        var effect = e.Reattribution ? RiskIndexAttributionEffect.Restructuring
            : Math.Abs(e.Context) > Math.Abs(e.Risk) ? RiskIndexAttributionEffect.Context
            : RiskIndexAttributionEffect.Risk;
        return new RiskIndexAttributionRowDto(effect, e.Category, labels[new IcrObjectKey(e.Object.Kind, e.Object.Ref)],
            "RiskEffect_" + effect, points, IcrMath.Displayed(points),
            change is { } c && Math.Abs(c) > 1e-12 ? 100 * points / c : null, null, e.Reattribution);
    }
}
