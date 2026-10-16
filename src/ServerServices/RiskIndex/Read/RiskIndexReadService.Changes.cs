using System.Globalization;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using Model.Status;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

public sealed partial class RiskIndexReadService
{
    public const int MaxChangeDays = 30;

    /// <summary>
    /// "What moved" (S39 §9.4; S40 §5.7) between two snapshots of a node: the stored daily attributions of an official node
    /// summed over the period — they are additive, each day's path from the day before — or, for a derived node, the
    /// Aumann–Shapley attribution of the engine between the two days' object sets. A partial node has none: the attribution
    /// needs the whole set (S40 §5.6.5).
    /// </summary>
    public async Task<RiskIndexChangesDto> GetChangesAsync(string? node, DateOnly? from, DateOnly? to, RiskIndexCaller caller,
        CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var day = await ResolveDayAsync(to, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        if (resolved.Partial)
            throw RiskIndexException.Conflict(RiskIndexErrors.ChangesUnavailablePartial,
                "A partial view has no \"what moved\": the attribution needs the node's whole object set.");

        await using var db = dalService.GetContext();
        var start = from ?? await PreviousDateAsync(db, day.Date, ct)
            ?? throw RiskIndexException.NotFound(RiskIndexErrors.NoSnapshot, "There is no earlier snapshot to compare with.");
        if (start >= day.Date || day.Date.DayNumber - start.DayNumber > MaxChangeDays)
            throw RiskIndexException.BadRequest(RiskIndexErrors.InvalidPeriod,
                $"'from' must be before 'to', and at most {MaxChangeDays} days earlier.");

        var extra = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await cache.GetOrCreateAsync(CacheKey("changes", resolved, day, caller, extra), RiskIndexReadCache.SnapshotTimeToLive,
            () => resolved.Official
                ? OfficialChangesAsync(resolved, day, start, caller, ct)
                : DerivedChangesAsync(resolved, day, start, caller, ct));
    }

    private static async Task<DateOnly?> PreviousDateAsync(DAL.Context.NRDbContext db, DateOnly date, CancellationToken ct) =>
        await db.RiskIndexNodeMembers.AsNoTracking().Where(m => m.SnapshotDate < date).Select(m => (DateOnly?)m.SnapshotDate)
            .MaxAsync(ct)
        ?? await db.RiskIndexSnapshots.AsNoTracking().Where(s => s.SnapshotDate < date).Select(s => (DateOnly?)s.SnapshotDate)
            .MaxAsync(ct);

    private async Task<RiskIndexChangesDto> OfficialChangesAsync(Node node, Day day, DateOnly from, RiskIndexCaller caller,
        CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var heads = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ScopeKind == node.OfficialKind && s.ScopeRef == node.OfficialRef && s.Category == RiskIndexCategory.Icr &&
                        s.SnapshotDate >= from && s.SnapshotDate <= day.Date)
            .Select(s => new { s.SnapshotDate, s.ValueUnfloored, s.ValueDisplayed, s.ChangeAttribution, s.Markers })
            .ToListAsync(ct);
        var before = heads.FirstOrDefault(h => h.SnapshotDate == from);
        var after = heads.FirstOrDefault(h => h.SnapshotDate == day.Date)
                    ?? throw RiskIndexException.NotFound(RiskIndexErrors.NodeNotFound, "The node has no snapshot on 'to'.");
        if (before is null) throw RiskIndexException.NotFound(RiskIndexErrors.NoSnapshot, "The node has no snapshot on 'from'.");

        // Every day of the period must be readable officially: an attribution names the objects of the whole node.
        var covered = await CoveredDatesAsync(db, day, node, from, day.Date, ct);
        if (covered is not null && heads.Any(h => !covered.Contains(h.SnapshotDate)))
            throw RiskIndexException.Conflict(RiskIndexErrors.ChangesUnavailablePartial,
                "Part of the period is outside your scope's official view of this node.");

        var days = heads.Where(h => h.SnapshotDate > from).OrderBy(h => h.SnapshotDate).ToList();
        var parts = days.Select(h => RiskIndexStoredJson.Deserialize<StoredChangeAttribution>(h.ChangeAttribution))
            .Where(c => c is not null).Select(c => c!).ToList();
        var markers = days.Where(h => RiskIndexStoredJson.ParseMarkers(h.Markers).Count > 0).Select(h => h.SnapshotDate).ToList();

        var delta = after.ValueUnfloored - before.ValueUnfloored;
        var entries = parts.SelectMany(p => p.Entries)
            .GroupBy(e => (e.Object.Kind, e.Object.Ref, e.Category, e.Reattribution))
            .Select(g => g.First() with { Risk = g.Sum(e => e.Risk), Context = g.Sum(e => e.Context) })
            .ToList();
        var effects = new Effects(parts.Sum(p => p.Configuration), parts.Sum(p => p.Coverage), parts.Sum(p => p.Restructuring),
            parts.Sum(p => p.Unattributed), parts.Sum(p => p.OmittedTotal));

        var labels = await RiskIndexLabels.ResolveAsync(db, entries.Select(e => new IcrObjectKey(e.Object.Kind, e.Object.Ref)),
            caller, ct);
        return Changes(node, from, day.Date, delta, after.ValueDisplayed - before.ValueDisplayed, entries, effects, labels, markers);
    }

    private sealed record Effects(double Configuration, double Coverage, double Restructuring, double Unattributed, double Omitted);

    private Task<RiskIndexChangesDto> DerivedChangesAsync(Node node, Day day, DateOnly from, RiskIndexCaller caller,
        CancellationToken ct) =>
        budget.RunAsync(caller.UserId, async token =>
        {
            await using var db = dalService.GetContext();
            var previousProfile = await ProfileOfAsync(db, from, token)
                                  ?? throw RiskIndexException.NotFound(RiskIndexErrors.NoSnapshot, "There is no snapshot on 'from'.");
            if (previousProfile.Id != day.Profile.Id)
                throw RiskIndexException.Conflict("changes_cross_profile",
                    "The period crosses a profile change; a derived node's change is computed within one profile.");

            var beforeSet = await RiskIndexStoredDays.LoadSetAsync(db, from, day.Profile.Id, day.P, day.Map, token);
            var afterSet = await RiskIndexStoredDays.LoadSetAsync(db, day.Date, day.Profile.Id, day.P, day.Map, token);
            var before = Compose(node, day with { Date = from }, beforeSet);
            var after = Compose(node, day, afterSet);
            var change = IcrChangeAttribution.Attribute(before.Input, after.Input, day.P, new HashSet<IcrObjectKey>());

            var entries = change.Entries.Select(e => new StoredAttributionEntry(StoredObjectRef.Of(e.Key), e.Category, e.Membership,
                e.RiskEffect, e.ContextEffect, e.Reattribution)).ToList();
            var labels = await RiskIndexLabels.ResolveAsync(db, change.Entries.Select(e => e.Key), caller, token);
            return Changes(node, from, day.Date, (change.After ?? 0) - (change.Before ?? 0),
                (after.Result.Displayed ?? 0) - (before.Result.Displayed ?? 0), entries,
                new Effects(change.ConfigurationEffect, change.CoverageEffect, change.RestructuringEffect, change.Unattributed, 0),
                labels, []);
        }, ct);

    private static RiskIndexChangesDto Changes(Node node, DateOnly from, DateOnly to, double delta, int deltaDisplayed,
        IReadOnlyList<StoredAttributionEntry> entries, Effects effects, IReadOnlyDictionary<IcrObjectKey, RiskIndexObjectLabelDto> labels,
        IReadOnlyList<DateOnly> markers)
    {
        double? Percent(double points) => Math.Abs(delta) > 1e-12 ? 100 * points / delta : null;
        RiskIndexAttributionRowDto Effect(RiskIndexAttributionEffect effect, double points) =>
            new(effect, null, null, "RiskEffect_" + effect, points, IcrMath.Displayed(points), Percent(points), null, false);

        var rows = new List<RiskIndexAttributionRowDto>();
        foreach (var e in entries)
        {
            var label = labels[new IcrObjectKey(e.Object.Kind, e.Object.Ref)];
            if (e.Reattribution)
            {
                rows.Add(new RiskIndexAttributionRowDto(RiskIndexAttributionEffect.Restructuring, e.Category, label,
                    "RiskEffect_Restructuring", e.Risk + e.Context, IcrMath.Displayed(e.Risk + e.Context), Percent(e.Risk + e.Context),
                    null, true));
                continue;
            }

            if (Math.Abs(e.Risk) > 1e-9)
                rows.Add(new RiskIndexAttributionRowDto(RiskIndexAttributionEffect.Risk, e.Category, label, "RiskEffect_Risk", e.Risk,
                    IcrMath.Displayed(e.Risk), Percent(e.Risk), null, false));
            if (Math.Abs(e.Context) > 1e-9)
                rows.Add(new RiskIndexAttributionRowDto(RiskIndexAttributionEffect.Context, e.Category, label, "RiskEffect_Context",
                    e.Context, IcrMath.Displayed(e.Context), Percent(e.Context), null, false));
        }

        if (Math.Abs(effects.Configuration) > 1e-9) rows.Add(Effect(RiskIndexAttributionEffect.Configuration, effects.Configuration));
        if (Math.Abs(effects.Coverage) > 1e-9) rows.Add(Effect(RiskIndexAttributionEffect.Coverage, effects.Coverage));
        if (Math.Abs(effects.Unattributed + effects.Omitted) > 1e-9)
            rows.Add(Effect(RiskIndexAttributionEffect.Unattributed, effects.Unattributed + effects.Omitted));

        // Restructuring rows already carry their shares; the stored total adds nothing beyond them.
        var explained = entries.Sum(e => e.Risk + e.Context) + effects.Configuration + effects.Coverage + effects.Unattributed +
                        effects.Omitted;
        var ordered = rows.OrderByDescending(r => Math.Abs(r.Points)).ToList();
        return new RiskIndexChangesDto(node.Ref, from, to, delta, deltaDisplayed, ordered, [], delta - explained, markers);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Governance and review indicators — volume beside the index, never in it (S40 §5.2.5)
    // ------------------------------------------------------------------------------------------------------------------

    public async Task<RiskIndexGovernanceIndicatorsDto> GetGovernanceIndicatorsAsync(string? node, DateOnly? date,
        RiskIndexCaller caller, CancellationToken ct = default)
    {
        await EnsureNotInShadowAsync(caller, ct);
        var day = await ResolveDayAsync(date, ct);
        var resolved = await ResolveNodeAsync(ParseKey(node), day, ct);
        return await cache.GetOrCreateAsync(CacheKey("governance", resolved, day, caller), RiskIndexReadCache.SnapshotTimeToLive,
            async () =>
            {
                var composition = await ComposeAsync(resolved, day, caller, ct);
                var risks = Ids(composition.Rows, RiskIndexObjectKind.Scenario);
                var hosts = Ids(composition.Rows, RiskIndexObjectKind.Host);
                var applications = Ids(composition.Rows, RiskIndexObjectKind.Application);
                var now = DateTime.UtcNow;

                // Every read below goes through the caller's scope filter, so a partial view counts only the visible.
                await using var db = dalService.GetContext();
                var expiringBy = now.AddDays(day.P.ExpiryWarningDays);
                var expiring = await db.RiskAcceptances.AsNoTracking()
                    .CountAsync(a => a.RiskId != null && risks.Contains(a.RiskId.Value) && a.Status == RiskAcceptanceStatus.Active &&
                                     a.ExpiresAt >= now && a.ExpiresAt <= expiringBy, ct);

                var since = now.AddDays(-90);
                var overrides = await db.MgmtReviews.AsNoTracking()
                    .CountAsync(m => risks.Contains(m.RiskId) && m.SegregationOverrideReason != null &&
                                     m.SegregationOverrideReason != "" && m.SubmissionDate >= since, ct);

                var entities = resolved.IsRoot ? null : resolved.Closure?.ToList();
                var campaigns = await db.RiskReviewCampaigns.AsNoTracking()
                    .Where(c => entities == null || entities.Contains(c.EntityId))
                    .Select(c => new { c.Id, c.Status, c.CreatedAt })
                    .ToListAsync(ct);
                double? completion = null, meanDays = null;
                if (campaigns.MaxBy(c => c.CreatedAt) is { } latest)
                {
                    var items = await db.RiskReviewCampaignItems.AsNoTracking().Where(i => i.CampaignId == latest.Id)
                        .Select(i => new { i.Decision, i.DecidedAt }).ToListAsync(ct);
                    if (items.Count > 0)
                    {
                        var decided = items.Where(i => i.Decision != RiskReviewDecision.Pending).ToList();
                        completion = (double)decided.Count / items.Count;
                        var spans = decided.Where(i => i.DecidedAt is not null)
                            .Select(i => (i.DecidedAt!.Value - latest.CreatedAt).TotalDays).ToList();
                        meanDays = spans.Count > 0 ? spans.Average() : null;
                    }
                }

                var findings = await db.Vulnerabilities.AsNoTracking()
                    .Where(v => v.SlaDueDate != null &&
                                ((v.HostId != null && hosts.Contains(v.HostId.Value)) ||
                                 (v.HostId == null && v.EntityId != null && applications.Contains(v.EntityId.Value))))
                    .Select(v => new { v.LifecycleStatus, v.SlaDueDate })
                    .ToListAsync(ct);
                var open = findings.Where(f => f.LifecycleStatus.IsOpen()).ToList();
                double? sla = open.Count > 0 ? (double)open.Count(f => f.SlaDueDate >= now) / open.Count : null;

                return new RiskIndexGovernanceIndicatorsDto(expiring, day.P.ExpiryWarningDays, overrides, completion, meanDays,
                    campaigns.Count(c => c.Status == RiskReviewCampaignStatus.Overdue), sla, "requires_m43", "requires_m45_m46");
            });

        static List<int> Ids(IEnumerable<RiskIndexObjectRow> rows, RiskIndexObjectKind kind) =>
            rows.Where(r => r.Key.Kind == kind).Select(r => int.TryParse(r.Key.Ref, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
    }
}
