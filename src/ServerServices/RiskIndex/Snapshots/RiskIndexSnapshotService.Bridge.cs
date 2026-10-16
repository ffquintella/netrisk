using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Profiles;
using ServerServices.RiskIndex.Read;

namespace ServerServices.RiskIndex;

public sealed partial class RiskIndexSnapshotService
{
    /// <summary>How many stored days before the change the overlay replays (S39 §9.5).</summary>
    public const int OverlayDays = 90;

    /// <summary>One node and category of a bridge: both versions on the bridge day and the overlay.</summary>
    internal sealed record BridgeNode(RiskIndexScopeKind ScopeKind, string ScopeRef, int? EntityId, RiskIndexCategory Category,
        double Old, double New, IReadOnlyList<StoredOverlayPoint> Overlay);

    /// <summary>The bridge of a profile change, and each node's "what moved" with <c>A_cfg</c> = Δ of the version.</summary>
    internal sealed record BridgeDay(int FromProfileId, bool OverlayPartial, IReadOnlyList<BridgeNode> Rows,
        IReadOnlyDictionary<(RiskIndexScopeKind, string), IcrChangeAttributionResult> Changes);

    /// <summary>
    /// M53, T250 (S39 §9.4–§9.5; S40 §6.4, §9.3 step 4). On the first day of a new profile the day is also computed under
    /// the previous one, so each node gets <c>Δ_versão = new − old</c>: that is <c>A_cfg</c>, and the rest of the day's
    /// change is the previous version's attribution from D−1, so "what moved" across the change still reconciles. The new
    /// version is replayed over the previous version's stored days of the last 90 (the overlay), labelled partial when the
    /// change touches what a replay cannot reproduce. The old series is never rewritten.
    /// </summary>
    internal async Task<BridgeDay> BridgeAsync(PreviousSnapshot previous, RiskIndexProfile profile, RiskIndexParameters parameters,
        RiskIndexDay day, DateTime instant, IReadOnlyDictionary<int, DateTime> frozen, DateOnly date, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var old = await db.RiskIndexProfiles.AsNoTracking().FirstAsync(p => p.Id == previous.ProfileId, ct);
        var oldParameters = RiskIndexParametersJson.Deserialize(old.Parameters);

        var oldDay = await calculator.ComputeAsync(new RiskIndexDayRequest
        {
            Parameters = oldParameters,
            Mode = old.Mode,
            SnapshotInstant = instant,
            Options = new RiskIndexCollectionOptions { IncidentClosureDates = frozen, DivergenceSince = previous.DivergenceSince },
            Previous = previous.Day
        }, ct);
        var oldNodes = oldDay.Nodes.Where(n => !n.Node.Unassigned).ToDictionary(n => (n.Node.ScopeKind, n.Node.ScopeRef));

        // The overlay: the new version over the previous version's stored days (S39 §9.5).
        var from = date.AddDays(-OverlayDays);
        var days = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ProfileId == old.Id && s.ScopeKind == RiskIndexScopeKind.Organization &&
                        s.Category == RiskIndexCategory.Icr && s.SnapshotDate >= from && s.SnapshotDate < date)
            .Select(s => s.SnapshotDate).OrderBy(d => d).ToListAsync(ct);
        var map = await RiskIndexObjectCollector.ReadEntityMapAsync(db, ct);
        var overlay = new Dictionary<(RiskIndexScopeKind, string, RiskIndexCategory), List<StoredOverlayPoint>>();
        foreach (var d in days)
        {
            ct.ThrowIfCancellationRequested();
            var set = await RiskIndexStoredDays.LoadSetAsync(db, d, old.Id, oldParameters, map, ct);
            var at = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue) + SnapshotTimeOfDay, DateTimeKind.Utc);
            foreach (var node in RiskIndexReplay.Day(set, parameters, profile.Mode, at).Nodes.Where(n => !n.Node.Unassigned))
                foreach (var (category, value) in Values(node.Result))
                {
                    var key = (node.Node.ScopeKind, node.Node.ScopeRef, category);
                    if (!overlay.TryGetValue(key, out var points)) overlay[key] = points = [];
                    points.Add(new StoredOverlayPoint(d, value));
                }
        }

        var partial = old.Mode != profile.Mode ||
                      !RiskIndexProfileRules.IsReplayable(RiskIndexProfileRules.ChangedKeys(oldParameters, parameters));

        var rows = new List<BridgeNode>();
        var changes = new Dictionary<(RiskIndexScopeKind, string), IcrChangeAttributionResult>();
        foreach (var node in day.Nodes.Where(n => !n.Node.Unassigned))
        {
            if (!oldNodes.TryGetValue((node.Node.ScopeKind, node.Node.ScopeRef), out var before)) continue;

            var oldValues = Values(before.Result).ToDictionary(v => v.Category, v => v.Value);
            foreach (var (category, value) in Values(node.Result))
                rows.Add(new BridgeNode(node.Node.ScopeKind, node.Node.ScopeRef, node.Node.EntityId, category,
                    oldValues.GetValueOrDefault(category), value,
                    overlay.GetValueOrDefault((node.Node.ScopeKind, node.Node.ScopeRef, category)) ?? []));

            // ΔICR* = A_cfg + (the previous version's attribution from D−1 to D) — S39 §9.4.
            if (before.Change is { } change && before.Result.Unfloored is { } oldValue && node.Result.Unfloored is { } newValue)
                changes[(node.Node.ScopeKind, node.Node.ScopeRef)] = change with
                {
                    ConfigurationEffect = newValue - oldValue,
                    After = newValue,
                    Change = change.Before is { } b ? newValue - b : null
                };
        }

        return new BridgeDay(old.Id, partial, rows, changes);
    }

    /// <summary>The headline (ICR*) and each available category's <c>C_k</c> of a node.</summary>
    private static IEnumerable<(RiskIndexCategory Category, double Value)> Values(IcrNodeResult result)
    {
        if (result.Unfloored is { } headline) yield return (RiskIndexCategory.Icr, headline);
        foreach (var c in result.Categories.Where(c => c.Status == IcrCategoryStatus.Available))
            yield return (c.Category, c.Value);
    }
}
