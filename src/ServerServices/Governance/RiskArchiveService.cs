using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.RiskFlags;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.DecisionCycle;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.9 (S50 §4.1–§4.3) — the archive: the fourth Phase 4 decision ("⚪ archive: justification + reopening trigger,
/// quarterly review").
///
/// Archiving closes the risk through a closure of its own (S50 D1), so nothing that already skips closed risks has to
/// learn a fifth status: the cadence, the appetite counts, the metrics, Gate A and every list keep treating it as closed.
/// What the archive adds is recorded beside the closure — the justification as a Phase 4 decision, the reopening
/// conditions, and a quarterly review with its due date and its notice.
///
/// The reopening trigger is not new machinery (S50 D3): the conditions are the six Phase 7 triggers of Stage 9.8, and a
/// reassessment event of a watched type that reaches the archived risk — declared naming it, or a breach of a KRI linked
/// to it — reopens the archive in the same write as its <see cref="RiskReassessmentTrigger"/>
/// (<see cref="ReopenWatchingAsync"/>, called by <see cref="MonitoringService"/>). It fires once (S50 D4): the archive
/// becomes <see cref="RiskArchiveStatus.Reopened"/>, the trigger is unique per event and risk, and a further event
/// reaches an open risk, which Stage 9.8 already flags for review.
/// </summary>
public class RiskArchiveService(
    ILogger logger,
    IDalService dalService,
    IRiskWorkflowService workflow,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IRiskArchiveService
{
    public const string RiskClosedRule = "risk_archive_risk_closed";
    public const string NotLiveRule = "risk_archive_not_live";
    public const string ConditionMetRule = "risk_archive_condition_met";

    private const string Closed = RiskWorkflowService.StatusClosed;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The register statuses an archive may restore; anything else restores to "New".</summary>
    private static readonly Dictionary<string, RiskStatus> Restorable = new(StringComparer.OrdinalIgnoreCase)
    {
        [RiskWorkflowService.StatusNew] = RiskStatus.New,
        [RiskWorkflowService.StatusMitigationPlanned] = RiskStatus.MitigationPlanned,
        [RiskWorkflowService.StatusManagementReview] = RiskStatus.ManagementReview
    };

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- reading -----------------------------------------------------------------------------------------------

    public async Task<List<RiskArchiveDto>> GetArchivesAsync(bool dueOnly, bool includeEnded)
    {
        await using var db = DalService.GetContext();
        var now = Clock();

        var archives = await db.RiskArchives.AsNoTracking()
            .Where(a => includeEnded || a.Status == RiskArchiveStatus.Archived)
            .Include(a => a.Conditions).Include(a => a.Reviews)
            .OrderBy(a => a.NextReviewDueAt).ThenBy(a => a.Id)
            .ToListAsync();

        var dtos = await ToDtosAsync(db, archives, now);

        return dtos
            .Where(d => includeEnded || d.State == RiskArchiveState.Live)
            .Where(d => !dueOnly || d.ReviewOverdue)
            .ToList();
    }

    public async Task<List<RiskArchiveDto>> GetRiskArchivesAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        if (!await db.Risks.AnyAsync(r => r.Id == riskId))
            throw new DataNotFoundException("risks", riskId.ToString(Invariant));

        var archives = await db.RiskArchives.AsNoTracking().Where(a => a.RiskId == riskId)
            .Include(a => a.Conditions).Include(a => a.Reviews)
            .OrderByDescending(a => a.ArchivedAt).ThenByDescending(a => a.Id)
            .ToListAsync();

        return await ToDtosAsync(db, archives, Clock());
    }

    // --- archiving ---------------------------------------------------------------------------------------------

    public async Task<RiskArchiveDto> ArchiveAsync(int riskId, RiskArchiveRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var justification = Required(request.Justification, DecisionCycleLimits.MaxJustificationLength,
            nameof(RiskArchiveRequest.Justification),
            "Say why the risk is not worth treating — the justification is what the archive decision rests on.");

        if (request.CloseReason is not { } closeReason || closeReason < 0)
            throw new InvalidParameterException(nameof(RiskArchiveRequest.CloseReason),
                "The closure reason is required: archiving closes the risk.");

        var conditions = ValidateConditions(request.Conditions);
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("risks", riskId.ToString(Invariant));

        if (string.Equals(risk.Status, Closed, StringComparison.OrdinalIgnoreCase))
            throw new RuleBrokenException(
                "The risk is already closed. Archiving is a decision taken on an open risk: reopen it first if it should " +
                "be archived with reopening conditions.", RiskClosedRule);

        if (!await db.CloseReasons.AnyAsync(c => c.Value == closeReason))
            throw new InvalidParameterException(nameof(RiskArchiveRequest.CloseReason),
                $"Closure reason {closeReason} does not exist.");

        // Who first, as for an acceptance — but Gate A outranks everything, and archiving is closing (S46 §4.7).
        await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "archive a risk");
        await workflow.EnsureTransitionAllowedAsync(riskId, risk.Status, Closed);
        await workflow.EnsureSegregationOfDutiesAsync(riskId, actingUserId, "archive");

        if (conditions.Any(c => c.Type == ReassessmentTriggerType.NewDataOrKriBreach))
            await RefuseIfAKriAlreadyBreachedAsync(riskId);

        var closure = new Closure
        {
            RiskId = riskId, UserId = actingUserId, ClosureDate = now, CloseReason = closeReason, Note = justification
        };

        var archive = new RiskArchive
        {
            RiskId = riskId,
            Closure = closure,
            Status = RiskArchiveStatus.Archived,
            Justification = justification,
            PreviousStatus = Truncate(risk.Status, 50),
            ArchivedAt = now,
            ArchivedById = actingUserId,
            NextReviewDueAt = ArchiveRules.NextReviewDue(now),
            CreatedAt = now,
            Conditions = conditions.Select(c => new RiskArchiveCondition
                { TriggerType = c.Type, Description = c.Description, CreatedAt = now }).ToList()
        };

        db.Closures.Add(closure);
        db.RiskArchives.Add(archive);

        // The Phase 4 decision the archive is (S46 §4.3): the decision in force becomes "archive", so the metrics and the
        // Top Risks list read the same decision the register acted on.
        db.RiskDecisions.Add(new RiskDecision
        {
            RiskId = riskId, Decision = RiskDecisionKind.Archive, Source = RiskDecisionSource.Declared,
            Reason = justification, DecidedAt = now, DecidedById = actingUserId, CreatedAt = now
        });

        risk.Status = Closed;
        risk.StatusId = RiskStatus.Closed;
        risk.LastUpdate = now;

        await db.SaveChangesAsync();

        // The closure's id exists only now. The status is already Closed above, in the same write as the archive.
        risk.CloseId = closure.Id;
        await db.SaveChangesAsync();

        Logger.Information("Risk {Risk} ARCHIVED by user {User}: {Count} reopening condition(s), review due {Due:yyyy-MM-dd}",
            riskId, actingUserId, conditions.Count, archive.NextReviewDueAt);

        return (await ToDtosAsync(db, [archive], now)).Single();
    }

    public async Task<RiskArchiveDto> ReopenAsync(int riskId, RiskArchiveReopenRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = Required(request.Reason, DecisionCycleLimits.MaxReopenReasonLength,
            nameof(RiskArchiveReopenRequest.Reason), "Say why the archive is reopened.");
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var (risk, archive, closure) = await RequireLiveAsync(db, riskId);

        await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "reopen an archive");

        Reopen(db, archive, risk, closure, now, RiskArchiveReopenOrigin.Manual, reason, actingUserId, null);
        FlagForReview(risk, now, $"The archive was reopened: {reason}");

        await db.SaveChangesAsync();

        Logger.Information("Archive {Archive} of risk {Risk} reopened by user {User}", archive.Id, riskId, actingUserId);

        return (await ToDtosAsync(db, [archive], now)).Single();
    }

    public async Task<RiskArchiveDto> ReviewAsync(int riskId, RiskArchiveReviewRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Outcome is not { } outcome || !Enum.IsDefined(outcome))
            throw new InvalidParameterException(nameof(RiskArchiveReviewRequest.Outcome),
                "The outcome is keep archived (1) or reopen (2).");

        var note = Required(request.Note, DecisionCycleLimits.MaxReviewNoteLength, nameof(RiskArchiveReviewRequest.Note),
            "Record what the review looked at and concluded.");
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var (risk, archive, closure) = await RequireLiveAsync(db, riskId);

        await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "review an archive");

        if (outcome == RiskArchiveReviewOutcome.KeepArchived)
        {
            // Keeping it archived is discarding it for another quarter: a Gate A condition that appeared since forbids
            // that (S46 §4.7), and the risk's own people may not decide it (S50 §4.2).
            await workflow.EnsureGateAAllowsAsync(riskId, GateAAction.Close);
            await workflow.EnsureSegregationOfDutiesAsync(riskId, actingUserId, "review the archive of");

            archive.LastReviewedAt = now;
            archive.NextReviewDueAt = ArchiveRules.NextReviewDue(now);
            archive.ReviewNotifiedAt = null;
            archive.UpdatedAt = now;
        }
        else
        {
            Reopen(db, archive, risk, closure, now, RiskArchiveReopenOrigin.QuarterlyReview, note, actingUserId, null);
            FlagForReview(risk, now, $"The quarterly review reopened the archive: {note}");
        }

        db.RiskArchiveReviews.Add(new RiskArchiveReview
        {
            Archive = archive, Outcome = outcome, Note = note, ReviewedAt = now, ReviewedById = actingUserId,
            NextReviewDueAt = outcome == RiskArchiveReviewOutcome.KeepArchived ? archive.NextReviewDueAt : null,
            CreatedAt = now
        });

        await db.SaveChangesAsync();

        Logger.Information("Archive {Archive} of risk {Risk} reviewed by user {User}: {Outcome}", archive.Id, riskId,
            actingUserId, outcome);

        var reloaded = await db.RiskArchives.AsNoTracking().Include(a => a.Conditions).Include(a => a.Reviews)
            .SingleAsync(a => a.Id == archive.Id);
        return (await ToDtosAsync(db, [reloaded], now)).Single();
    }

    // --- the quarterly notice ----------------------------------------------------------------------------------

    public async Task<RiskArchiveReviewSweepSummary> NotifyDueReviewsAsync()
    {
        var summary = new RiskArchiveReviewSweepSummary();
        var now = Clock();

        await using var db = SystemContext();

        var archives = await db.RiskArchives.Where(a => a.Status == RiskArchiveStatus.Archived).ToListAsync();
        var states = await LiveFactsAsync(db, archives);

        foreach (var archive in archives)
        {
            if (!states.TryGetValue(archive.Id, out var risk)) continue;

            summary.Live++;
            if (!ArchiveRules.IsReviewDue(archive.NextReviewDueAt, now)) continue;

            summary.Due++;
            if (archive.ReviewNotifiedAt is not null) continue;

            try
            {
                archive.ReviewNotifiedAt = now;
                await db.SaveChangesAsync();
                await notifications.RiskArchiveReviewDueAsync(risk, archive);
                summary.Notified++;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Announcing the quarterly review of archive {Archive} failed", archive.Id);
            }
        }

        return summary;
    }

    // --- the reopening trigger (called by MonitoringService) ---------------------------------------------------

    /// <summary>
    /// For the closed risks a reassessment event reaches, the live archives that watch its type, reopened in
    /// <paramref name="db"/> — not saved: the caller saves them with the trigger it raises on each, so the two are one
    /// write, and a concurrent evaluation that loses the unique index on (event, risk) loses the reopening with it.
    /// </summary>
    internal static async Task<List<(Risk Risk, RiskArchive Archive)>> ReopenWatchingAsync(AuditableContext db,
        IReadOnlyCollection<Risk> closedRisks, ReassessmentEvent reassessment, DateTime now)
    {
        var reopened = new List<(Risk, RiskArchive)>();
        if (closedRisks.Count == 0) return reopened;

        var ids = closedRisks.Select(r => r.Id).ToList();
        var archives = await db.RiskArchives
            .Where(a => ids.Contains(a.RiskId) && a.Status == RiskArchiveStatus.Archived)
            .Include(a => a.Conditions)
            .OrderBy(a => a.Id)
            .ToListAsync();

        var closureIds = archives.Where(a => a.ClosureId != null).Select(a => a.ClosureId!.Value).ToList();
        var closures = await db.Closures.Where(c => closureIds.Contains(c.Id)).ToListAsync();

        foreach (var archive in archives)
        {
            var risk = closedRisks.First(r => r.Id == archive.RiskId);
            var closure = closures.FirstOrDefault(c => c.Id == archive.ClosureId && c.RiskId == risk.Id);

            if (ArchiveRules.StateOf(archive.Status, archive.ClosureId, risk.Status, closure is not null)
                != RiskArchiveState.Live) continue;

            if (!ArchiveRules.Reopens(archive.Conditions.Select(c => c.TriggerType), reassessment.TriggerType)) continue;

            Reopen(db, archive, risk, closure, now, RiskArchiveReopenOrigin.Condition,
                $"Reopening condition met ({MonitoringService.Label(reassessment.TriggerType)}): {reassessment.Title}",
                null, reassessment);
            reopened.Add((risk, archive));
        }

        return reopened;
    }

    /// <summary>Announces each reopening once it is saved (<c>risk.archive_reopened</c>).</summary>
    internal static async Task NotifyReopenedAsync(AuditableContext db, INotificationEventPublisher notifications,
        IReadOnlyCollection<(Risk Risk, RiskArchive Archive)> reopened, ReassessmentEvent reassessment)
    {
        if (reopened.Count == 0) return;

        var ids = reopened.Select(r => r.Risk.Id).ToList();
        var scores = await db.RiskScorings.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, Score = s.ResidualRisk ?? s.CalculatedRisk })
            .ToDictionaryAsync(s => s.Id, s => (double?)s.Score);

        foreach (var (risk, archive) in reopened)
        {
            Log.Warning("Archive {Archive} of risk {Risk} REOPENED by reassessment event {Event} ({Type})", archive.Id,
                risk.Id, reassessment.Id, reassessment.TriggerType);
            await notifications.RiskArchiveReopenedAsync(risk, scores.GetValueOrDefault(risk.Id), archive, reassessment);
        }
    }

    // --- internals ---------------------------------------------------------------------------------------------

    private static void Reopen(AuditableContext db, RiskArchive archive, Risk risk, Closure? closure, DateTime now,
        RiskArchiveReopenOrigin origin, string reason, int? userId, ReassessmentEvent? reassessment)
    {
        archive.Status = RiskArchiveStatus.Reopened;
        archive.ReopenedAt = now;
        archive.ReopenOrigin = origin;
        archive.ReopenReason = Truncate(reason, DecisionCycleLimits.MaxReopenReasonLength);
        archive.ReopenedById = userId;
        archive.UpdatedAt = now;

        if (reassessment is not null)
        {
            if (reassessment.Id > 0) archive.ReopenEventId = reassessment.Id;
            else archive.ReopenEvent = reassessment;
        }

        // The closure the archive made goes, as on the legacy reopen route: a closure row on an open risk would make it
        // read as closed to whatever reads closures rather than the status.
        if (closure is not null) db.Closures.Remove(closure);
        archive.ClosureId = null;

        var restored = Restorable.TryGetValue(archive.PreviousStatus, out _)
            ? archive.PreviousStatus
            : RiskWorkflowService.StatusNew;

        risk.Status = restored;
        risk.StatusId = Restorable[restored];
        risk.CloseId = null;
        risk.LastUpdate = now;
    }

    /// <summary>Flags the risk for review, keeping the first reason, as <c>RisksService.RequestReviewAsync</c> does.</summary>
    private static void FlagForReview(Risk risk, DateTime now, string reason)
    {
        if (risk.ReviewRequested) return;

        risk.ReviewRequested = true;
        risk.ReviewRequestedAt = now;
        risk.ReviewRequestedReason = Truncate(reason, 1000);
    }

    private async Task<(Risk Risk, RiskArchive Archive, Closure? Closure)> RequireLiveAsync(AuditableContext db,
        int riskId)
    {
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("risks", riskId.ToString(Invariant));

        var archive = await db.RiskArchives.Include(a => a.Conditions)
            .Where(a => a.RiskId == riskId && a.Status == RiskArchiveStatus.Archived)
            .OrderByDescending(a => a.Id).FirstOrDefaultAsync();

        var closure = archive?.ClosureId is { } closureId
            ? await db.Closures.FirstOrDefaultAsync(c => c.Id == closureId && c.RiskId == riskId)
            : null;

        if (archive is null ||
            ArchiveRules.StateOf(archive.Status, archive.ClosureId, risk.Status, closure is not null) != RiskArchiveState.Live)
            throw new RuleBrokenException(
                "The risk has no live archive: it was never archived, the archive was reopened, or the risk was reopened " +
                "outside it.", NotLiveRule);

        return (risk, archive, closure);
    }

    /// <summary>
    /// A KRI linked to the risk is in a breach episode now: the condition that would reopen the archive already holds,
    /// so archiving with it would be discarding a risk its indicator says is beyond tolerance (S50 §4.1). Read unscoped:
    /// the KRI may be one the caller cannot see, and it still governs the risk.
    /// </summary>
    private async Task RefuseIfAKriAlreadyBreachedAsync(int riskId)
    {
        await using var system = SystemContext();

        var linked = await system.KriRisks.AsNoTracking().Where(l => l.RiskId == riskId).Select(l => l.KriId).ToListAsync();
        if (linked.Count == 0) return;

        var episodes = await system.ReassessmentEvents.AsNoTracking()
            .Where(e => e.KriId != null && linked.Contains(e.KriId.Value) && e.Origin == ReassessmentEventOrigin.KriBreach
                        && e.KriBreachEndedAt == null)
            .Select(e => e.KriId!.Value)
            .ToListAsync();
        if (episodes.Count == 0) return;

        var breached = await system.Kris.AsNoTracking()
            .Where(k => episodes.Contains(k.Id) && k.RetiredAt == null)
            .OrderBy(k => k.Id).Select(k => k.Name)
            .FirstOrDefaultAsync();

        if (breached is not null)
            throw new RuleBrokenException(
                $"The KRI '{breached}' linked to this risk is beyond its tolerance now, so the condition that would reopen " +
                "the archive already holds. Treat or reassess the risk instead.", ConditionMetRule);
    }

    /// <summary>The archives that are live, with their risk — for the sweep, in a system context.</summary>
    private static async Task<Dictionary<int, Risk>> LiveFactsAsync(AuditableContext db, List<RiskArchive> archives)
    {
        var riskIds = archives.Select(a => a.RiskId).Distinct().ToList();
        var risks = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var closureIds = archives.Where(a => a.ClosureId != null).Select(a => a.ClosureId!.Value).ToList();
        var closures = await db.Closures.AsNoTracking().Where(c => closureIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RiskId }).ToListAsync();

        var live = new Dictionary<int, Risk>();
        foreach (var archive in archives)
        {
            if (!risks.TryGetValue(archive.RiskId, out var risk)) continue;
            var closureExists = closures.Any(c => c.Id == archive.ClosureId && c.RiskId == archive.RiskId);
            if (ArchiveRules.StateOf(archive.Status, archive.ClosureId, risk.Status, closureExists) == RiskArchiveState.Live)
                live[archive.Id] = risk;
        }

        return live;
    }

    private static async Task<List<RiskArchiveDto>> ToDtosAsync(AuditableContext db, List<RiskArchive> archives,
        DateTime now)
    {
        var riskIds = archives.Select(a => a.RiskId).Distinct().ToList();
        var risks = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Subject, r.Status }).ToDictionaryAsync(r => r.Id);
        var closureIds = archives.Where(a => a.ClosureId != null).Select(a => a.ClosureId!.Value).ToList();
        var closures = await db.Closures.AsNoTracking().Where(c => closureIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RiskId }).ToListAsync();

        return archives.Select(a =>
        {
            var risk = risks.GetValueOrDefault(a.RiskId);
            var closureExists = closures.Any(c => c.Id == a.ClosureId && c.RiskId == a.RiskId);
            var state = ArchiveRules.StateOf(a.Status, a.ClosureId, risk?.Status, closureExists);

            return new RiskArchiveDto
            {
                Id = a.Id,
                RiskId = a.RiskId,
                RiskSubject = risk?.Subject ?? string.Empty,
                Status = a.Status,
                State = state,
                Justification = a.Justification,
                PreviousStatus = a.PreviousStatus,
                ArchivedAt = a.ArchivedAt,
                ArchivedById = a.ArchivedById,
                NextReviewDueAt = a.NextReviewDueAt,
                ReviewOverdue = state == RiskArchiveState.Live && ArchiveRules.IsReviewDue(a.NextReviewDueAt, now),
                LastReviewedAt = a.LastReviewedAt,
                ReopenedAt = a.ReopenedAt,
                ReopenOrigin = a.ReopenOrigin,
                ReopenedById = a.ReopenedById,
                ReopenReason = a.ReopenReason,
                ReopenEventId = a.ReopenEventId ?? a.ReopenEvent?.Id,
                Conditions = a.Conditions.OrderBy(c => c.TriggerType)
                    .Select(c => new RiskArchiveConditionDto { TriggerType = c.TriggerType, Description = c.Description })
                    .ToList(),
                Reviews = a.Reviews.OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id)
                    .Select(r => new RiskArchiveReviewDto
                    {
                        Id = r.Id, Outcome = r.Outcome, Note = r.Note, ReviewedAt = r.ReviewedAt,
                        ReviewedById = r.ReviewedById, NextReviewDueAt = r.NextReviewDueAt
                    }).ToList()
            };
        }).ToList();
    }

    private sealed record ValidCondition(ReassessmentTriggerType Type, string? Description);

    private static List<ValidCondition> ValidateConditions(List<RiskArchiveConditionRequest>? conditions)
    {
        const string parameter = nameof(RiskArchiveRequest.Conditions);

        if (conditions is null || conditions.Count == 0)
            throw new InvalidParameterException(parameter,
                "An archive needs at least one reopening condition: the Phase 7 triggers whose event brings the risk back.");

        var valid = new List<ValidCondition>();
        foreach (var condition in conditions)
        {
            if (condition?.TriggerType is not { } type || !Enum.IsDefined(type))
                throw new InvalidParameterException(parameter,
                    "A condition is one of the six reassessment triggers, 1 to 6.");

            if (valid.Any(v => v.Type == type))
                throw new InvalidParameterException(parameter, $"The trigger {(int)type} is listed twice.");

            var description = string.IsNullOrWhiteSpace(condition.Description) ? null : condition.Description.Trim();
            if (description is { Length: > DecisionCycleLimits.MaxConditionDescriptionLength })
                throw new InvalidParameterException(parameter,
                    $"A condition's description is at most {DecisionCycleLimits.MaxConditionDescriptionLength} characters.");

            valid.Add(new ValidCondition(type, description));
        }

        return valid;
    }

    private static string Required(string? text, int max, string parameter, string missing)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidParameterException(parameter, missing);

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private AuditableContext SystemContext() => DalService.GetContext(withIdentity: false, bypassEntityScope: true);
}
