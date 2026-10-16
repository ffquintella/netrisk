using System.Globalization;
using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Read;
using ServerServices.Services;
using Tools.Security;
using ILogger = Serilog.ILogger;

namespace ServerServices.RiskIndex.Profiles;

public enum RiskIndexPreviewRunResult
{
    /// <summary>No request was waiting.</summary>
    Idle = 1,
    Completed = 2,
    Failed = 3,

    /// <summary>Another run claimed the request first: nothing was done twice.</summary>
    LostClaim = 4
}

/// <summary>Computes the queued sensitivity previews (S40 §9.6) — what <c>RiskIndexPreviewJob</c> runs, in the job host.</summary>
public interface IRiskIndexPreviewService
{
    /// <summary>Requeues the abandoned, claims the oldest queued request atomically and computes its report.</summary>
    Task<RiskIndexPreviewRunResult> ProcessNextAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// M53, T250 (S39 §13.2; S40 §6.3, §9.6): the draft replayed over the stored days of the last 90 and the latest day,
/// against the profile in force — Δ per node, band changes, Kendall's τ between units, the tornado, the presets, the
/// register's effective weight and the objects that move most. The report goes to the request row, never to the profile.
/// The first profile of all has no stored day: its preview covers today, collected live under the draft.
/// </summary>
public sealed class RiskIndexPreviewService(IDalService dalService, ILogger logger) : IRiskIndexPreviewService
{
    /// <summary>A Running request whose heartbeat is older than this is queued again (S40 §9.6).</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(15);

    public const int ReplayDays = 90;
    public const int TopMovers = 20;

    public static string ParametersHash(RiskIndexMode mode, string parameters) =>
        HashTool.CreateSha256(((int)mode).ToString(CultureInfo.InvariantCulture) + "|" + parameters);

    public static readonly JsonSerializerOptions ReportJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    public async Task<RiskIndexPreviewRunResult> ProcessNextAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await RequeueAbandonedAsync(nowUtc, cancellationToken);

        int requestId;
        await using (var db = dalService.GetContext())
        {
            var next = await db.RiskIndexPreviewRequests.AsNoTracking().Where(r => r.Status == RiskIndexPreviewStatus.Queued)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(cancellationToken);
            if (next is null) return RiskIndexPreviewRunResult.Idle;
            requestId = next.Value;
        }

        if (!await ClaimAsync(requestId, nowUtc, cancellationToken)) return RiskIndexPreviewRunResult.LostClaim;

        try
        {
            var report = await ComputeAsync(requestId, nowUtc, cancellationToken);
            await using var db = dalService.GetContext();
            var request = await db.RiskIndexPreviewRequests.Include(r => r.Profile).FirstAsync(r => r.Id == requestId, cancellationToken);

            // The draft changed while the preview ran: its report describes parameters that no longer exist (S40 §9.6).
            if (ParametersHash(request.Profile.Mode, request.Profile.Parameters) != request.ParametersHash)
                return await FailAsync(requestId, "The parameters changed during the preview.", cancellationToken);

            request.Status = RiskIndexPreviewStatus.Completed;
            request.Progress = 100;
            request.FinishedAt = DateTime.UtcNow;
            request.HeartbeatAt = request.FinishedAt;
            request.Report = JsonSerializer.Serialize(report, ReportJson);
            await db.SaveChangesAsync(cancellationToken);
            logger.Information("Risk index preview {RequestId} of profile {ProfileId} completed", requestId, request.ProfileId);
            return RiskIndexPreviewRunResult.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error(ex, "Risk index preview {RequestId} failed", requestId);
            return await FailAsync(requestId, ex.Message, cancellationToken);
        }
    }

    private async Task RequeueAbandonedAsync(DateTime now, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var cutoff = now - AbandonedAfter;
        var stale = await db.RiskIndexPreviewRequests
            .Where(r => r.Status == RiskIndexPreviewStatus.Running && (r.HeartbeatAt ?? r.StartedAt ?? r.CreatedAt) < cutoff)
            .ToListAsync(ct);
        foreach (var r in stale)
        {
            r.Status = RiskIndexPreviewStatus.Queued;
            r.StartedAt = null;
            r.HeartbeatAt = null;
            r.Progress = 0;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.Warning("Requeued {Count} abandoned risk index preview(s)", stale.Count);
        }
    }

    /// <summary>
    /// The atomic claim (S40 §9.6): <c>UPDATE … SET status = Running WHERE id = ? AND status = Queued</c>, checking the rows
    /// affected — two overlapping runs never process one request. The in-memory provider has no set-based update; there a
    /// tracked update stands in, under the job's single-flight.
    /// </summary>
    internal async Task<bool> ClaimAsync(int requestId, DateTime now, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        if (db.Database.IsRelational())
            return await db.RiskIndexPreviewRequests
                .Where(r => r.Id == requestId && r.Status == RiskIndexPreviewStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RiskIndexPreviewStatus.Running)
                    .SetProperty(r => r.StartedAt, now)
                    .SetProperty(r => r.HeartbeatAt, now), ct) == 1;

        var request = await db.RiskIndexPreviewRequests.FirstOrDefaultAsync(
            r => r.Id == requestId && r.Status == RiskIndexPreviewStatus.Queued, ct);
        if (request is null) return false;
        request.Status = RiskIndexPreviewStatus.Running;
        request.StartedAt = now;
        request.HeartbeatAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<RiskIndexPreviewRunResult> FailAsync(int requestId, string message, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var request = await db.RiskIndexPreviewRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return RiskIndexPreviewRunResult.Failed;
        request.Status = RiskIndexPreviewStatus.Failed;
        request.FinishedAt = DateTime.UtcNow;
        request.ErrorMessage = message.Length <= 2000 ? message : message[..2000];
        await db.SaveChangesAsync(ct);
        return RiskIndexPreviewRunResult.Failed;
    }

    private async Task ProgressAsync(int requestId, int progress, CancellationToken ct)
    {
        await using var db = dalService.GetContext();
        var request = await db.RiskIndexPreviewRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return;
        request.Progress = Math.Clamp(progress, 0, 99);
        request.HeartbeatAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------------------------------

    internal async Task<RiskIndexPreviewDto> ComputeAsync(int requestId, DateTime now, CancellationToken ct)
    {
        await using var db = dalService.GetContext(withIdentity: false);
        var request = await db.RiskIndexPreviewRequests.AsNoTracking().Include(r => r.Profile).FirstAsync(r => r.Id == requestId, ct);
        var draft = RiskIndexParametersJson.Deserialize(request.Profile.Parameters);
        var mode = request.Profile.Mode;
        var map = await RiskIndexObjectCollector.ReadEntityMapAsync(db, ct);

        var instant = RiskIndexSnapshotService.SnapshotInstant(now);
        var active = await RiskIndexSnapshotService.ProfileInForceAsync(db, instant, ct);
        var latest = active is null ? null : await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ProfileId == active.Id && s.ScopeKind == RiskIndexScopeKind.Organization && s.Category == RiskIndexCategory.Icr)
            .Select(s => (DateOnly?)s.SnapshotDate).MaxAsync(ct);

        if (active is null || latest is null)
        {
            // The first profile (S39 §13.2): today with raw data, under the draft — no Δ, no τ, no overlay.
            var live = await RiskIndexObjectCollector.CollectAsync(db, draft, mode, instant, null, ct);
            await ProgressAsync(requestId, 50, ct);
            var liveDay = RiskIndexReplay.Day(live, draft, mode, instant);
            var liveRoot = RiskIndexReplay.Root(live, draft, mode);
            return new RiskIndexPreviewDto(Status(), await NodesAsync(db, liveDay, null, draft, null, map, ct), null, 0,
                RiskIndexReplay.Tornado(live, draft, mode), Presets(live, mode), 0, RiskIndexReplay.RegisterWeight(liveRoot), [],
                PureMean(live, mode), false);
        }

        var activeParameters = RiskIndexParametersJson.Deserialize(active.Parameters);
        var set = await RiskIndexStoredDays.LoadSetAsync(db, latest.Value, active.Id, activeParameters, map, ct);
        var draftDay = RiskIndexReplay.Day(set, draft, mode, instant);
        var activeDay = RiskIndexReplay.Day(set, activeParameters, active.Mode, instant);
        await ProgressAsync(requestId, 20, ct);

        var draftRoot = RiskIndexReplay.Root(set, draft, mode);
        var activeRoot = RiskIndexReplay.Root(set, activeParameters, active.Mode);
        var nodes = await NodesAsync(db, draftDay, activeDay, draft, activeParameters, map, ct);
        var units = nodes.Where(n => n.Active is not null && n.Node.NodeType is RiskIndexEntityTypes.OrganizationUnit
                or RiskIndexEntityTypes.SubOrganizationUnit)
            .Select(n => (n.Active!.Value, n.Draft)).ToList();
        var tornado = RiskIndexReplay.Tornado(set, draft, mode);
        await ProgressAsync(requestId, 40, ct);

        // The objects that move most between the versions: Δ of their points in the root's headline (S39 §13.2).
        var before = RiskIndexReplay.Contributions(activeRoot);
        var after = RiskIndexReplay.Contributions(draftRoot);
        var movers = before.Keys.Union(after.Keys)
            .Select(k => (k.Key, k.Category, Delta: after.GetValueOrDefault(k) - before.GetValueOrDefault(k)))
            .Where(x => Math.Abs(x.Delta) > 1e-9)
            .OrderByDescending(x => Math.Abs(x.Delta)).Take(TopMovers)
            .Select(x => new RiskIndexMovingObjectDto(Unlabelled(x.Key), x.Category, x.Delta)).ToList();

        // The overlay: the root's headline under the draft on each stored day of the last 90 (S39 §9.5).
        var from = latest.Value.AddDays(-(ReplayDays - 1));
        var days = await db.RiskIndexSnapshots.AsNoTracking()
            .Where(s => s.ProfileId == active.Id && s.ScopeKind == RiskIndexScopeKind.Organization &&
                        s.Category == RiskIndexCategory.Icr && s.SnapshotDate >= from && s.SnapshotDate <= latest.Value)
            .Select(s => s.SnapshotDate).OrderBy(d => d).ToListAsync(ct);
        var overlay = new List<RiskIndexOverlayPointDto>();
        for (var i = 0; i < days.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var daySet = days[i] == latest.Value ? set
                : await RiskIndexStoredDays.LoadSetAsync(db, days[i], active.Id, activeParameters, map, ct);
            if (RiskIndexReplay.Root(daySet, draft, mode).Unfloored is { } value) overlay.Add(new RiskIndexOverlayPointDto(days[i], value));
            if (i % 10 == 9) await ProgressAsync(requestId, 40 + 55 * (i + 1) / days.Count, ct);
        }

        var partial = !RiskIndexProfileRules.IsReplayable(RiskIndexProfileRules.ChangedKeys(activeParameters, draft)) ||
                      active.Mode != mode;
        return new RiskIndexPreviewDto(Status(), nodes, RiskIndexReplay.KendallTau(units), units.Count, tornado, Presets(set, mode),
            RiskIndexReplay.RegisterWeight(activeRoot), RiskIndexReplay.RegisterWeight(draftRoot), movers, PureMean(set, mode), partial,
            new RiskIndexOverlayDto(active.Version, request.Profile.Version, partial, overlay));
    }

    private static RiskIndexPreviewStatusDto Status() => new(null, RiskIndexPreviewState.Completed, 100, null, null, null);

    private static RiskIndexObjectLabelDto Unlabelled(IcrObjectKey key) =>
        new(new RiskIndexObjectRef(key.Kind, key.Ref), RiskIndexRedactionPolicy.RedactedLabel(key.Kind, key.Ref),
            RiskIndexRedactionPolicy.LabelPermission(key.Kind));

    private static RiskIndexNodeRef RootRef => new(RiskIndexNodeKey.Organization, "Organization", RiskIndexEntityTypes.Organization);

    private static IReadOnlyList<RiskIndexPresetValueDto> Presets(RiskIndexObjectSet set, RiskIndexMode mode) =>
        RiskIndexPresets.All.Where(RiskIndexPresets.IsActivatable)
            .Select(k => new RiskIndexPresetValueDto(k, RootRef, RiskIndexReplay.Root(set, RiskIndexPresets.Parameters(k), mode).Unfloored ?? 0))
            .ToList();

    private static IReadOnlyList<RiskIndexPresetValueDto> PureMean(RiskIndexObjectSet set, RiskIndexMode mode) =>
        [new(RiskIndexPresets.PureMean, RootRef,
            RiskIndexReplay.Root(set, RiskIndexPresets.Parameters(RiskIndexPresets.PureMean), mode).Unfloored ?? 0)];

    /// <summary>Each pre-computed node (the root and the entity nodes) under the draft, against the profile in force.</summary>
    private static async Task<IReadOnlyList<RiskIndexPreviewNodeDto>> NodesAsync(NRDbContext db, RiskIndexDay draft,
        RiskIndexDay? active, RiskIndexParameters draftParameters, RiskIndexParameters? activeParameters, EntityMap map,
        CancellationToken ct)
    {
        var activeByRef = active?.Nodes.ToDictionary(n => (n.Node.ScopeKind, n.Node.ScopeRef), n => n.Result);
        var entityIds = draft.Nodes.Where(n => n.Node.EntityId is not null).Select(n => n.Node.EntityId!.Value).ToList();
        var names = await RiskIndexLabels.EntityNamesAsync(db, entityIds, ct);

        var result = new List<RiskIndexPreviewNodeDto>();
        foreach (var node in draft.Nodes.Where(n => n.Node.ScopeKind is RiskIndexScopeKind.Organization or RiskIndexScopeKind.Entity
                                                    && !n.Node.Unassigned))
        {
            if (node.Result.Unfloored is not { } value) continue;
            var reference = node.Node.EntityId is { } id
                ? new RiskIndexNodeRef(RiskIndexNodeKey.ForEntity(id).ToString(),
                    names.GetValueOrDefault(id) ?? "#" + id.ToString(CultureInfo.InvariantCulture), map.DefinitionOf(id) ?? "entity")
                : RootRef;
            double? before = activeByRef is not null && activeByRef.TryGetValue((node.Node.ScopeKind, node.Node.ScopeRef), out var a)
                ? a.Unfloored
                : null;
            var bandDraft = Band(node.Result.Displayed, node.Result.DisplayState, draftParameters);
            var bandActive = before is { } b && activeParameters is not null
                ? Band(IcrMath.Displayed(b), activeByRef![(node.Node.ScopeKind, node.Node.ScopeRef)].DisplayState, activeParameters)
                : null;
            result.Add(new RiskIndexPreviewNodeDto(reference, before, value, bandActive, bandDraft,
                bandActive is not null && bandDraft is not null && bandActive != bandDraft));
        }

        return result;
    }

    private static string? Band(int? displayed, IcrDisplayState state, RiskIndexParameters p) =>
        RiskIndexReadService.BandIndex(displayed, (RiskIndexDisplayState)(int)state, p) is { } i ? RiskIndexReadService.BandKey(i) : null;
}
