using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    /// <summary>A node's values on a day, whether read from its snapshot rows or recomposed from its object rows.</summary>
    internal sealed record NodeValues(
        double? Unfloored, double? Floored, int? Displayed, RiskIndexDisplayState State, double Coverage, double Freshness,
        double Quality, double? IntervalLow, double? IntervalHigh, int ObjectCount, double PresumptionPoints,
        bool HeadlineFloorActive, DateTime ComputedAt, IReadOnlyList<CategoryValues> Categories,
        IReadOnlyList<StoredChip> Chips, IReadOnlyList<StoredMarker> Markers, IReadOnlyList<StoredContribution> Contributions,
        StoredChangeAttribution? Change);

    internal sealed record CategoryValues(RiskIndexCategory Category, RiskIndexCategoryState State, double Value,
        double Coverage, double Freshness, double PresumptionPoints, double? IntervalLow, double? IntervalHigh, int ObjectCount,
        IReadOnlyList<StoredChip> Chips, double? Points, IReadOnlyList<IcrObjectKey> HeldBy);

    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexOverviewDto> GetOverviewAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var key = ParseKey(node);
        var day = await ResolveDayAsync(date, ct);
        if (key.IsScopeRoot) return await ScopeRootAsync(day, ct);

        var resolved = await ResolveNodeAsync(key, day, ct);
        return await cache.GetOrCreateAsync(CacheKey("overview", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () =>
            {
                var values = await ValuesAsync(resolved, day, caller, ct);
                await using var db = dalService.GetContext();
                var labels = await RiskIndexLabels.ResolveAsync(db, values.Categories.SelectMany(c => c.HeldBy), caller, ct);
                var seals = await SealsAsync(db, resolved, day, RiskIndexRedactionPolicy.Holds(RiskIndexModulePermissions.RiskManagement,
                    caller.IsAdmin, caller.Permissions), ct);
                var delta = resolved.Official ? await Delta7dAsync(db, resolved, day, values, ct) : null;

                var headline = Headline(values, day.P, delta);
                var categories = values.Categories.Select(c => CategoryDto(c, values, day.P, labels,
                    c.Category == RiskIndexCategory.Reg ? seals : [])).ToList();
                var chips = values.Chips.Select(c => new RiskIndexChipDto(c.Key, c.Args ?? new Dictionary<string, string>(), null, null))
                    .ToList();
                if (resolved.Partial) chips.Add(new RiskIndexChipDto("partial_visibility", new Dictionary<string, string>(), null, null));

                return new RiskIndexOverviewDto(resolved.Ref, resolved.Breadcrumb, day.Date, values.ComputedAt, resolved.Provenance,
                    Summary(day.Profile), day.Profile.Mode, headline, categories, seals, chips, Quality(values, day.P), values.ObjectCount,
                    Bands(day.P), []);
            });
    }

    public async Task<IReadOnlyList<RiskIndexCategoryDto>> GetCategoriesAsync(string? node, DateOnly? date, RiskIndexCaller caller,
        CancellationToken ct = default) =>
        (await GetOverviewAsync(node, date, caller, ct)).Categories;

    /// <summary>"Meu escopo" (S40 §5.1): the entities the claims name, with no headline (S39 §10.4).</summary>
    private async Task<RiskIndexOverviewDto> ScopeRootAsync(Day day, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var claimed = day.Scope.IsUnrestricted ? [] : day.Scope.ClaimedEntityIds.Where(day.Map.Contains).ToList();
        var names = await RiskIndexLabels.EntityNamesAsync(db, claimed, ct);
        var entities = claimed.Select(id => new RiskIndexNodeRef(RiskIndexNodeKey.ForEntity(id).ToString(),
            names.GetValueOrDefault(id) ?? "#" + id.ToString(CultureInfo.InvariantCulture), day.Map.DefinitionOf(id) ?? "entity")).ToList();
        return new RiskIndexOverviewDto(new RiskIndexNodeRef(RiskIndexNodeKey.Scope, "My scope", "scope"), [], day.Date,
            DateTime.UtcNow, RiskIndexProvenance.Official, Summary(day.Profile), day.Profile.Mode, null, [], [], [],
            new RiskIndexQualitySummaryDto(0, 0, 0, "none", []), 0, Bands(day.P), entities);
    }

    /// <summary>The node's values: the snapshot rows when official, the engine over the visible object rows otherwise.</summary>
    internal async Task<NodeValues> ValuesAsync(Node node, Day day, RiskIndexCaller caller, CancellationToken ct)
    {
        if (node.Official)
        {
            await using var db = dalService.GetContext();
            return await OfficialValuesAsync(db, day.Date, day.Profile.Id, node.OfficialKind, node.OfficialRef, day.P, ct)
                   ?? throw RiskIndexException.NotFound(RiskIndexErrors.NodeNotFound, "The node has no snapshot on this day.");
        }

        return DerivedValues(await ComposeAsync(node, day, caller, ct), day.P, DateTime.UtcNow);
    }

    internal static async Task<NodeValues?> OfficialValuesAsync(NRDbContext db, DateOnly date, int profileId,
        RiskIndexScopeKind kind, string scopeRef, RiskIndexParameters p, CancellationToken ct)
    {
        var rows = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate == date && s.ProfileId == profileId && s.ScopeKind == kind && s.ScopeRef == scopeRef)
            .ToListAsync(ct);
        var head = rows.FirstOrDefault(r => r.Category == RiskIndexCategory.Icr);
        return head is null ? null : FromRows(head, rows.Where(r => r.Category != RiskIndexCategory.Icr).ToList(), p);
    }

    internal static NodeValues FromRows(RiskIndexSnapshot head, IReadOnlyList<RiskIndexSnapshot> categoryRows,
        RiskIndexParameters p)
    {
        var states = categoryRows.Select(r => (r.Category, Status: (IcrCategoryStatus)(int)(r.CategoryState ?? RiskIndexCategoryState.Disabled),
            Value: r.ValueUnfloored)).ToList();
        var gains = IcrNodeCalculator.Headline(states, p).Gains;

        var categories = categoryRows.OrderBy(r => r.Category).Select(r =>
        {
            var chips = RiskIndexStoredJson.ParseChips(r.Chips);
            var heldBy = chips.Where(c => c.Key == RiskIndexChipKeys.TailFloor && c.Args is not null &&
                                          c.Args.TryGetValue("held_by", out _))
                .SelectMany(c => c.Args!["held_by"].Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(ParseObjectKey).Where(k => k is not null).Select(k => k!.Value).ToList();
            var state = r.CategoryState ?? RiskIndexCategoryState.Disabled;
            return new CategoryValues(r.Category, state, r.ValueUnfloored, (double)r.Coverage, (double)r.Freshness,
                r.PresumptionPoints, r.IntervalLow, r.IntervalHigh, r.ObjectCount, chips,
                state == RiskIndexCategoryState.Available ? gains.GetValueOrDefault(r.Category) * r.ValueUnfloored : null, heldBy);
        }).ToList();

        var chipsHead = RiskIndexStoredJson.ParseChips(head.Chips);
        return new NodeValues(head.DisplayState == RiskIndexDisplayState.NoData ? null : head.ValueUnfloored,
            head.DisplayState == RiskIndexDisplayState.NoData ? null : head.ValueFloored,
            head.DisplayState == RiskIndexDisplayState.NoData ? null : head.ValueDisplayed,
            head.DisplayState ?? RiskIndexDisplayState.NoData, (double)head.Coverage, (double)head.Freshness, (double)head.Quality,
            head.IntervalLow, head.IntervalHigh, head.ObjectCount, head.PresumptionPoints,
            chipsHead.Any(c => c.Key == RiskIndexChipKeys.HeadlineFloor), head.CreatedAt, categories, chipsHead,
            RiskIndexStoredJson.ParseMarkers(head.Markers),
            RiskIndexStoredJson.Deserialize<List<StoredContribution>>(head.TopContributions) ?? [],
            RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(head.ChangeAttribution));
    }

    internal static NodeValues DerivedValues(Composition composition, RiskIndexParameters p, DateTime computedAt)
    {
        var r = composition.Result;
        var categories = r.Categories.Select(c =>
        {
            var chips = new List<StoredChip>();
            if (c.Partial) chips.Add(new StoredChip(RiskIndexChipKeys.Partial));
            else if (c.Note == "requires M39") chips.Add(new StoredChip(RiskIndexChipKeys.RequiresM39));
            if (c.Note == "no incident intake") chips.Add(new StoredChip(RiskIndexChipKeys.NoIncidentIntake));
            if (c.FloorActive && c.HeldBy.Count > 0)
                chips.Add(new StoredChip(RiskIndexChipKeys.TailFloor,
                    new Dictionary<string, string> { ["held_by"] = string.Join(",", c.HeldBy.Select(k => k.ToString())) }));
            if (c.PresumedIncluded > 0)
                chips.Add(new StoredChip(RiskIndexChipKeys.PresumedIncluded, new Dictionary<string, string>
                {
                    ["count"] = c.PresumedIncluded.ToString(CultureInfo.InvariantCulture),
                    ["inventory"] = c.InventoryCount.ToString(CultureInfo.InvariantCulture)
                }));
            var available = c.Status == IcrCategoryStatus.Available;
            return new CategoryValues(c.Category, (RiskIndexCategoryState)(int)c.Status, available ? c.Value : 0, c.Coverage,
                c.Freshness, available ? c.PresumptionPoints : 0, available ? c.IntervalLow : null, available ? c.IntervalHigh : null,
                c.ObjectCount, chips, available ? r.CategoryContributions.GetValueOrDefault(c.Category) : null,
                c.FloorActive ? c.HeldBy : []);
        }).ToList();

        var head = new List<StoredChip>();
        if (r.HeadlineFloorActive &&
            r.Categories.Where(c => c.Status == IcrCategoryStatus.Available).MaxBy(c => c.Value) is { } top)
            head.Add(new StoredChip(RiskIndexChipKeys.HeadlineFloor,
                new Dictionary<string, string> { ["category"] = ((int)top.Category).ToString(CultureInfo.InvariantCulture) }));

        var sensitivity = r.Sensitivity.GroupBy(s => (s.Key, s.Category)).ToDictionary(g => g.Key, g => g.First());
        var contributions = r.TopContributions.Select(c =>
        {
            sensitivity.TryGetValue((c.Key, c.Category), out var s);
            return new StoredContribution(StoredObjectRef.Of(c.Key), c.Category, c.Points, c.Presumed, c.HoldsFloor,
                s?.ChangeIfCleared, s is null || s.NextHolders.Count == 0 ? null : s.NextHolders.Select(StoredObjectRef.Of).ToList());
        }).ToList();

        return new NodeValues(r.Unfloored, r.Floored, r.Displayed, (RiskIndexDisplayState)(int)r.DisplayState, r.Coverage,
            r.Freshness, r.Quality, r.IntervalLow, r.IntervalHigh, r.ObjectCount, r.PresumptionPoints, r.HeadlineFloorActive,
            computedAt, categories, head, [], contributions, null);
    }

    // ------------------------------------------------------------------------------------------------------------------

    private static RiskIndexHeadlineDto Headline(NodeValues v, RiskIndexParameters p, RiskIndexDeltaDto? delta)
    {
        var band = BandIndex(v.Displayed, v.State, p);
        var heldBy = v.Chips.FirstOrDefault(c => c.Key == RiskIndexChipKeys.HeadlineFloor)?.Args is { } args &&
                     args.TryGetValue("category", out var text) &&
                     int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var category)
            ? (RiskIndexCategory?)category
            : null;
        return new RiskIndexHeadlineDto(v.Displayed, v.Unfloored, v.Floored, band is { } b ? BandKey(b) : null, band, v.State,
            v.IntervalLow, v.IntervalHigh, v.State is RiskIndexDisplayState.PointWithInterval or RiskIndexDisplayState.Indicative,
            delta, [], !v.HeadlineFloorActive, heldBy);
    }

    private static RiskIndexCategoryDto CategoryDto(CategoryValues c, NodeValues node, RiskIndexParameters p,
        IReadOnlyDictionary<IcrObjectKey, RiskIndexObjectLabelDto> labels, IReadOnlyList<RiskIndexSealDto> seals)
    {
        var omega = IcrCategoryCalculator.Omega(p, c.Category);
        var availableOmega = node.Categories.Where(x => x.State == RiskIndexCategoryState.Available)
            .Sum(x => IcrCategoryCalculator.Omega(p, x.Category));
        var available = c.State == RiskIndexCategoryState.Available;
        var displayed = available ? IcrMath.Displayed(c.Value) : (int?)null;
        var band = available ? BandIndex(displayed, RiskIndexDisplayState.Point, p) : null;

        RiskIndexHeldByDto? heldBy = null;
        if (c.HeldBy.Count > 0)
        {
            var first = c.HeldBy[0];
            heldBy = new RiskIndexHeldByDto(labels.GetValueOrDefault(first) ?? Redacted(first), c.Value / 100.0 / Math.Max(p.Tau, 1e-9),
                c.HeldBy.Count > 1 ? labels.GetValueOrDefault(c.HeldBy[1]) ?? Redacted(c.HeldBy[1]) : null, c.HeldBy.Count > 1,
                c.HeldBy.Count);
        }

        var chipKeys = c.Chips.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        string? reason = null, requires = null;
        if (chipKeys.Contains(RiskIndexChipKeys.SourceDisabled)) reason = RiskIndexChipKeys.SourceDisabled;
        else if (chipKeys.Contains(RiskIndexChipKeys.NoIncidentIntake)) reason = RiskIndexChipKeys.NoIncidentIntake;
        else if (chipKeys.Contains(RiskIndexChipKeys.RequiresM39))
        {
            reason = RiskIndexChipKeys.RequiresM39;
            requires = "M39";
        }
        else if (chipKeys.Contains(RiskIndexChipKeys.Partial)) requires = "M39";

        return new RiskIndexCategoryDto(c.Category, c.State, available ? c.Value : null, displayed, band is { } b ? BandKey(b) : null,
            omega, available && availableOmega > 0 ? omega / availableOmega : 0, c.Points, heldBy, c.Coverage,
            available ? c.Freshness : null, available ? c.PresumptionPoints : null,
            c.State == RiskIndexCategoryState.Available ? null : reason ?? c.State.ToString().ToLowerInvariant(), requires, seals);
    }

    private static RiskIndexObjectLabelDto Redacted(IcrObjectKey key) =>
        new(new RiskIndexObjectRef(key.Kind, key.Ref), RiskIndexRedactionPolicy.RedactedLabel(key.Kind, key.Ref),
            RiskIndexRedactionPolicy.LabelPermission(key.Kind));

    private static RiskIndexQualitySummaryDto Quality(NodeValues v, RiskIndexParameters p)
    {
        var band = v.Quality >= p.QualityHigh ? "high" : v.Quality >= p.QualityMedium ? "medium" : "low";
        var strip = new List<RiskIndexQualityStripItemDto>
        {
            new("coverage", v.Coverage, null, null, v.Coverage < p.CoveragePoint, null),
            new("freshness", v.Freshness, null, null, false, null)
        };

        // The organization's data-quality counts (S39 §8.5, §10.1), stored on its headline row.
        if (v.Chips.FirstOrDefault(c => c.Key == RiskIndexChipKeys.DataQuality)?.Args is { } args)
        {
            foreach (var (name, value) in args.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) continue;
                strip.Add(name == "unassigned_fraction"
                    ? new RiskIndexQualityStripItemDto(name, number, null, null, number > p.UnassignedThreshold, "node=u")
                    : new RiskIndexQualityStripItemDto(name, null, (int)number, null, number > 0, null));
            }
        }

        return new RiskIndexQualitySummaryDto(v.Coverage, v.Freshness, v.Quality, band, strip);
    }

    /// <summary>
    /// The 7-day arrow (S39 §9.3; S40 §5.2.2): against D−7, or the nearest snapshot back to D−10; never across a marker of
    /// the node in between; an arrow only when the displayed values differ by at least <c>delta_seta</c>.
    /// </summary>
    private static async Task<RiskIndexDeltaDto> Delta7dAsync(NRDbContext db, Node node, Day day, NodeValues values,
        CancellationToken ct)
    {
        var from = day.Date.AddDays(-10);
        var heads = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ScopeKind == node.OfficialKind && s.ScopeRef == node.OfficialRef && s.Category == RiskIndexCategory.Icr &&
                        s.SnapshotDate >= from && s.SnapshotDate <= day.Date)
            .Select(s => new { s.SnapshotDate, s.ValueDisplayed, s.Markers, s.DisplayState })
            .ToListAsync(ct);

        var covered = await CoveredDatesAsync(db, day, node, from, day.Date, ct);
        var baseline = heads.Where(h => h.SnapshotDate <= day.Date.AddDays(-7) && (covered is null || covered.Contains(h.SnapshotDate)))
            .MaxBy(h => h.SnapshotDate);
        if (baseline is null || values.Displayed is null || baseline.DisplayState == RiskIndexDisplayState.NoData)
            return new RiskIndexDeltaDto(null, null, null, RiskIndexDeltaSuppressedReason.MissingBaseline, null);

        var marker = heads.Where(h => h.SnapshotDate > baseline.SnapshotDate && h.SnapshotDate <= day.Date &&
                                      RiskIndexStoredJson.ParseMarkers(h.Markers).Count > 0)
            .Select(h => (DateOnly?)h.SnapshotDate).Max();
        if (marker is not null)
            return new RiskIndexDeltaDto(null, null, baseline.SnapshotDate, RiskIndexDeltaSuppressedReason.Discontinuity, marker);

        var points = values.Displayed.Value - baseline.ValueDisplayed;
        if (Math.Abs(points) < day.P.ArrowDelta)
            return new RiskIndexDeltaDto(points, RiskIndexDeltaDirection.Stable, baseline.SnapshotDate,
                RiskIndexDeltaSuppressedReason.BelowThreshold, null);
        return new RiskIndexDeltaDto(points, points > 0 ? RiskIndexDeltaDirection.Worse : RiskIndexDeltaDirection.Better,
            baseline.SnapshotDate, null, null);
    }

    /// <summary>
    /// The appetite and review seals of S39 §6.6 over the node's visible scenarios, from their stored register inputs:
    /// scenarios above the appetite ceiling without a valid acceptance, accepted above it, and acceptances expiring soon.
    /// Every reader sees the counts; only <paramref name="mayOpenLists"/> (riskmanagement, §11.3) gets the list behind them.
    /// </summary>
    private static async Task<IReadOnlyList<RiskIndexSealDto>> SealsAsync(NRDbContext db, Node node, Day day, bool mayOpenLists,
        CancellationToken ct)
    {
        var scenarios = await db.RiskIndexObjectDays.AsNoTracking()
            .Where(o => o.SnapshotDate == day.Date && o.ProfileId == day.Profile.Id && o.Category == RiskIndexCategory.Reg &&
                        o.ObjectKind == RiskIndexObjectKind.Scenario)
            .Select(o => new { o.ObjectRef, o.Attribution, o.ScenarioInputs })
            .ToListAsync(ct);

        var inNode = scenarios.Where(s =>
        {
            if (node.Key.HasFilters) return false; // scenarios are not in an asset intersection
            var attribution = RiskIndexStoredJson.ParseAttribution(s.Attribution);
            return node.IsRoot || (node.Key.Unassigned ? !attribution.Any(day.Map.Contains) : attribution.Any(node.Closure!.Contains));
        }).Select(s => RiskIndexStoredJson.ParseScenarioInputs(s.ScenarioInputs)).Where(i => i is not null).Select(i => i!).ToList();
        if (inNode.Count == 0) return [];

        var seals = new List<RiskIndexSealDto>();
        var above = inNode.Count(i => i.AboveCeiling && !i.AcceptedAboveCeiling);
        if (above > 0) seals.Add(Seal("scenarios_above_appetite", above, "seal=above_appetite"));
        var accepted = inNode.Count(i => i.AcceptedAboveCeiling);
        if (accepted > 0) seals.Add(Seal("scenarios_accepted_above_appetite", accepted, "seal=accepted_above_appetite"));
        var expiring = inNode.Count(i => i.AcceptanceExpiringSoon);
        if (expiring > 0) seals.Add(Seal("acceptances_expiring", expiring, "seal=acceptance_expiring"));
        if (inNode.All(i => !i.AppetiteConfigured)) seals.Add(Seal("appetite_not_configured", null, null));
        return seals;

        RiskIndexSealDto Seal(string key, int? count, string? filter) =>
            new(key, count, new Dictionary<string, string>(), mayOpenLists ? filter : null);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Contributions (S40 §5.8)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexContributionsDto> GetContributionsAsync(string? node, DateOnly? date, RiskIndexCategory? category,
        RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        var extra = category is null ? "" : ((int)category).ToString(CultureInfo.InvariantCulture);
        return await cache.GetOrCreateAsync(CacheKey("contributions", resolved, day, caller, extra),
            RiskIndexReadCache.SnapshotTimeToLive, async () =>
            {
                var values = await ValuesAsync(resolved, day, caller, ct);
                var categoryPoints = values.Categories.ToDictionary(c => c.Category, c => c.Points ?? 0);
                var rows = values.Contributions.Where(c => category is null || c.Category == category).ToList();

                await using var db = dalService.GetContext();
                var keys = rows.Select(r => new IcrObjectKey(r.Object.Kind, r.Object.Ref))
                    .Concat(rows.SelectMany(r => r.NextHolders ?? []).Select(n => new IcrObjectKey(n.Kind, n.Ref)));
                var labels = await RiskIndexLabels.ResolveAsync(db, keys, caller, ct);

                var dto = rows.Select(r =>
                {
                    var key = new IcrObjectKey(r.Object.Kind, r.Object.Ref);
                    var next = r.NextHolders is { Count: > 0 } n ? labels[new IcrObjectKey(n[0].Kind, n[0].Ref)] : null;
                    var share = categoryPoints.GetValueOrDefault(r.Category) is var points and > 0 ? r.Points / points : 0;
                    var branch = r.HoldsFloor ? RiskIndexContributionBranch.HeldBy
                        : r.Presumed ? RiskIndexContributionBranch.Presumption
                        : RiskIndexContributionBranch.Mean;
                    return new RiskIndexContributionRowDto(labels[key], r.Category, r.Points, share, branch, r.ChangeIfCleared, next,
                        null);
                }).ToList();

                return new RiskIndexContributionsDto(resolved.Ref, RegisterWeight(values), dto);
            });
    }

    /// <summary>
    /// The register's effective weight (S39 §13.2): the share of the headline the scenarios hold through REG, GOV (their
    /// population) and CTL.P — REG's whole Euler part plus the scenario contributions of GOV and CTL among those stored.
    /// </summary>
    internal static double RegisterWeight(NodeValues values)
    {
        if (values.Unfloored is not { } headline || headline <= 0) return 0;
        var reg = values.Categories.FirstOrDefault(c => c.Category == RiskIndexCategory.Reg)?.Points ?? 0;
        var others = values.Contributions.Where(c => c.Object.Kind == RiskIndexObjectKind.Scenario &&
                                                     c.Category is RiskIndexCategory.Gov or RiskIndexCategory.Ctl)
            .Sum(c => c.Points);
        return Math.Clamp((reg + others) / headline, 0, 1);
    }
}
