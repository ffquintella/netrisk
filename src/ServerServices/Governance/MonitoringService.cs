using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Monitoring;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Monitoring;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.8 (S49) — key risk indicators and the mandatory reassessment triggers of MIGR-TI/IA Phase 7.
///
/// The state of a KRI and Gate B by indicator are pure, in <c>Tools.Monitoring</c> (<see cref="KriEvaluator"/>,
/// <see cref="KriAppetite"/>), and tested there; this service validates and writes the KRI register, its readings, its
/// links and the declared events, and runs the evaluation that turns a breach into reassessment triggers.
///
/// Idempotence (S49 §4.7, D8): one event per breach episode — the open episode is reused, and a new one is unique on the
/// reading that opened it — and one trigger per event and risk, unique in the database. A KRI that stays breached for
/// thirty days therefore raises one reassessment per risk, however many times it is evaluated.
///
/// The evaluation runs in a system context (no identity, no entity scope): a trigger is a consequence of the indicator,
/// not a write of the person who recorded the reading, and a risk outside that person's scope is still triggered. The
/// reading itself, the definition, the links and the declared events are written in the caller's context and audited
/// with the caller.
/// </summary>
public class MonitoringService(
    ILogger logger,
    IDalService dalService,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IMonitoringService
{
    public const string KriRetiredRule = "kri_retired";
    public const string KriEntityMismatchRule = "kri_entity_mismatch";
    public const string ReadingAlreadyVoidedRule = "kri_reading_already_voided";
    public const string EventFromKriRule = "reassessment_event_from_kri";
    public const string NoOpenRiskRule = "reassessment_no_open_risk";

    /// <summary>The unique indexes whose MariaDB 1062 means "a concurrent request wrote this first" (S49 §4.7, D8).</summary>
    public const string EpisodeIndex = "uq_reassessment_events_kri_reading_id";
    public const string IncidentIndex = "uq_reassessment_events_incident_id";
    public const string TriggerIndex = "uq_risk_reassessment_triggers_event_id_risk_id";

    /// <summary>How many times a write that lost a race on <see cref="TriggerIndex"/> re-reads and tries again.</summary>
    internal const int MaxRaceAttempts = 3;

    private const string Closed = RiskWorkflowService.StatusClosed;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // --- the KRI register ----------------------------------------------------------------------------

    public async Task<List<KriDto>> GetKrisAsync(bool includeRetired)
    {
        await using var db = DalService.GetContext();
        var now = Clock();

        var kris = await db.Kris.AsNoTracking()
            .Where(k => includeRetired || k.RetiredAt == null)
            .OrderBy(k => k.Name).ThenBy(k => k.Id)
            .ToListAsync();

        var ids = kris.Select(k => k.Id).ToList();
        var readings = await ValidReadingsAsync(db, ids);
        var links = await db.KriRisks.AsNoTracking().Where(l => ids.Contains(l.KriId))
            .GroupBy(l => l.KriId).Select(g => new { KriId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.KriId, g => g.Count);
        var open = await OpenEpisodesAsync(db, ids);

        return kris.Select(k => ToDto(new KriDto(), k, readings.GetValueOrDefault(k.Id) ?? [], now,
            links.GetValueOrDefault(k.Id), open.GetValueOrDefault(k.Id))).ToList();
    }

    public async Task<KriDetailDto> GetKriAsync(int kriId)
    {
        await using var db = DalService.GetContext();
        return await BuildDetailAsync(db, await RequireKriAsync(db, kriId, tracked: false));
    }

    public async Task<KriDto> CreateKriAsync(KriRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = ValidateDefinition(request);
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireReferencesAsync(db, valid);

        var kri = new Kri { CreatedAt = now };
        Apply(kri, valid, actingUserId);
        db.Kris.Add(kri);

        // A scoped caller writing an organization-wide KRI, or another entity's, is refused here by the write guard
        // (EntityScopeViolationException → 403), because Kri is IEntityScoped.
        await db.SaveChangesAsync();

        Logger.Information("KRI {Kri} '{Name}' created by user {User} with tolerance {Tolerance} ({Direction})",
            kri.Id, kri.Name, actingUserId, kri.ToleranceThreshold, kri.Direction);

        return ToDto(new KriDto(), kri, [], now, 0, null);
    }

    public async Task<KriDto> UpdateKriAsync(int kriId, KriRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = ValidateDefinition(request);

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var kri = await RequireKriAsync(db, kriId, tracked: true);
            RefuseRetired(kri);
            await RequireReferencesAsync(db, valid);
            await RequireLinkedRisksInEntityAsync(kriId, valid.EntityId);

            var relaxed = Relaxes(kri, valid);

            Apply(kri, valid, actingUserId);
            kri.UpdatedAt = Clock();
            await db.SaveChangesAsync();

            // Loosening the tolerance, turning the direction or letting readings age longer can lift a refusal of
            // Gate B — the act the trail exists for; the interceptor records the fields, this line the operational log.
            if (relaxed)
                Logger.Warning("KRI {Kri} tolerance RELAXED by user {User}: now {Tolerance} ({Direction}), max age {Age} days",
                    kriId, actingUserId, kri.ToleranceThreshold, kri.Direction, kri.MaxReadingAgeDays);
            else
                Logger.Information("KRI {Kri} updated by user {User}", kriId, actingUserId);
        }

        // A new tolerance can open or close a breach episode.
        await EvaluateInSystemContextAsync(kriId);

        await using var read = DalService.GetContext();
        return await BuildDetailAsync(read, await RequireKriAsync(read, kriId, tracked: false));
    }

    public async Task<KriDto> RetireKriAsync(int kriId, int actingUserId)
    {
        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var kri = await RequireKriAsync(db, kriId, tracked: true);
            if (kri.RetiredAt is null)
            {
                kri.RetiredAt = now;
                kri.UpdatedAt = now;
                kri.UpdatedById = actingUserId;
                await db.SaveChangesAsync();

                Logger.Warning("KRI {Kri} RETIRED by user {User}; it no longer gates any risk", kriId, actingUserId);
            }
        }

        // A retired KRI is not evaluated, so its breach in progress would otherwise stay open forever.
        await using (var system = SystemContext())
        {
            var open = await system.ReassessmentEvents
                .Where(e => e.KriId == kriId && e.Origin == ReassessmentEventOrigin.KriBreach && e.KriBreachEndedAt == null)
                .ToListAsync();
            foreach (var episode in open) episode.KriBreachEndedAt = now;
            if (open.Count > 0) await system.SaveChangesAsync();
        }

        await using var read = DalService.GetContext();
        return await BuildDetailAsync(read, await RequireKriAsync(read, kriId, tracked: false));
    }

    // --- readings ------------------------------------------------------------------------------------

    public async Task<KriDetailDto> RecordReadingAsync(int kriId, KriReadingRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();

        var value = RequireValue(request.Value, nameof(KriReadingRequest.Value));
        var observedAt = RequirePast(request.ObservedAt, now, nameof(KriReadingRequest.ObservedAt),
            "When the value was observed is required — the reading's date, not today's by default.");
        var note = Optional(request.Note, MonitoringLimits.MaxNoteLength, nameof(KriReadingRequest.Note));

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var kri = await RequireKriAsync(db, kriId, tracked: false);
            RefuseRetired(kri);
            RequireWritableHistory(db, kri);

            db.KriReadings.Add(new KriReading
            {
                KriId = kriId, Value = value, ObservedAt = observedAt, Note = note, RecordedById = actingUserId,
                CreatedAt = now
            });
            await db.SaveChangesAsync();

            Logger.Information("KRI {Kri} reading {Value} observed {Observed:o} recorded by user {User}",
                kriId, value, observedAt, actingUserId);
        }

        await EvaluateInSystemContextAsync(kriId);

        await using var read = DalService.GetContext();
        return await BuildDetailAsync(read, await RequireKriAsync(read, kriId, tracked: false));
    }

    public async Task<KriDetailDto> VoidReadingAsync(int kriId, int readingId, KriReadingVoidRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = Required(request.Reason, MonitoringLimits.MaxVoidReasonLength, nameof(KriReadingVoidRequest.Reason),
            "Say why the reading is voided — it stays in the history with this reason.");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            RequireWritableHistory(db, await RequireKriAsync(db, kriId, tracked: false));

            var reading = await db.KriReadings.FirstOrDefaultAsync(r => r.Id == readingId && r.KriId == kriId)
                          ?? throw new DataNotFoundException("kri_readings", readingId.ToString(Invariant));

            if (reading.VoidedAt is not null)
                throw new RuleBrokenException(
                    $"Reading {readingId} was already voided on {reading.VoidedAt:yyyy-MM-dd}; a voiding is not undone or repeated.",
                    ReadingAlreadyVoidedRule);

            reading.VoidedAt = Clock();
            reading.VoidedById = actingUserId;
            reading.VoidReason = reason;
            await db.SaveChangesAsync();

            // Voiding a reading beyond the tolerance can lift a refusal of Gate B (S49 R1).
            Logger.Warning("KRI {Kri} reading {Reading} ({Value}) VOIDED by user {User}", kriId, readingId,
                reading.Value, actingUserId);
        }

        await EvaluateInSystemContextAsync(kriId);

        await using var read = DalService.GetContext();
        return await BuildDetailAsync(read, await RequireKriAsync(read, kriId, tracked: false));
    }

    // --- links ---------------------------------------------------------------------------------------

    public async Task<KriDetailDto> LinkRiskAsync(int kriId, int riskId, int actingUserId)
    {
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var kri = await RequireKriAsync(db, kriId, tracked: false);
            RefuseRetired(kri);

            var risk = await db.Risks.AsNoTracking().Where(r => r.Id == riskId)
                           .Select(r => new { r.Id, r.EntityId }).FirstOrDefaultAsync()
                       ?? throw new DataNotFoundException("risks", riskId.ToString(Invariant));

            // Whoever sees the risk must see the indicator that gates it (S49 D6).
            if (kri.EntityId is { } kriEntity && kriEntity != risk.EntityId)
                throw new RuleBrokenException(
                    "This indicator belongs to another business entity than the risk. Link an organization-wide indicator " +
                    "or one of the risk's own entity.", KriEntityMismatchRule);

            if (!await db.KriRisks.AnyAsync(l => l.KriId == kriId && l.RiskId == riskId))
            {
                db.KriRisks.Add(new KriRisk
                    { KriId = kriId, RiskId = riskId, CreatedAt = Clock(), CreatedById = actingUserId });
                await db.SaveChangesAsync();

                Logger.Information("KRI {Kri} linked to risk {Risk} by user {User}", kriId, riskId, actingUserId);
            }
        }

        // A breach in progress reaches the newly governed risk now, not at the next nightly pass.
        await EvaluateInSystemContextAsync(kriId);

        await using var read = DalService.GetContext();
        return await BuildDetailAsync(read, await RequireKriAsync(read, kriId, tracked: false));
    }

    public async Task UnlinkRiskAsync(int kriId, int riskId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        // Found through the risk's visibility alone (the link's own filter), not the KRI's: a KRI that has come to gate a
        // risk of another entity — the risk moved after it was linked — is invisible to that risk's unit, which must
        // still be able to remove a gate it cannot see (S49 §4.3). Its 404 says nothing the appetite block did not.
        var link = await db.KriRisks.FirstOrDefaultAsync(l => l.KriId == kriId && l.RiskId == riskId)
                   ?? throw new DataNotFoundException("kri_risks", $"{kriId}/{riskId}");

        db.KriRisks.Remove(link);
        await db.SaveChangesAsync();

        // Unlinking removes a gate (S49 R2).
        Logger.Warning("KRI {Kri} UNLINKED from risk {Risk} by user {User}; it no longer gates the risk",
            kriId, riskId, actingUserId);
    }

    // --- reassessment events -------------------------------------------------------------------------

    public async Task<List<ReassessmentEventDto>> GetEventsAsync(ReassessmentTriggerType? type, int? limit)
    {
        if (type is { } t && !Enum.IsDefined(t))
            throw new InvalidParameterException("type", "The trigger type is 1 to 6.");

        var take = limit ?? MonitoringLimits.DefaultListLimit;
        if (take is < 1 or > MonitoringLimits.MaxListLimit)
            throw new InvalidParameterException("limit", $"The limit is 1 to {MonitoringLimits.MaxListLimit}.");

        await using var db = DalService.GetContext();

        var events = await db.ReassessmentEvents.AsNoTracking()
            .Where(e => type == null || e.TriggerType == type)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync();

        var ids = events.Select(e => e.Id).ToList();
        var triggers = await ReadTriggersAsync(db, db.RiskReassessmentTriggers.Where(tr => ids.Contains(tr.EventId)));

        return events.Select(e => ToDto(e, triggers.Where(tr => tr.EventId == e.Id).ToList())).ToList();
    }

    public async Task<ReassessmentEventDto> DeclareEventAsync(ReassessmentEventRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = Clock();

        if (request.Type is not { } type || !Enum.IsDefined(type))
            throw new InvalidParameterException(nameof(ReassessmentEventRequest.Type),
                "The trigger type is 1 to 6: architecture or technology change, supplier/acquisition/migration, " +
                "significant incident or near miss, new regulation, new AI model, new data.");

        var title = Required(request.Title, MonitoringLimits.MaxTitleLength, nameof(ReassessmentEventRequest.Title),
            "Name the event — what changed.");
        var description = Optional(request.Description, MonitoringLimits.MaxDescriptionLength,
            nameof(ReassessmentEventRequest.Description));
        var occurredAt = RequirePast(request.OccurredAt, now, nameof(ReassessmentEventRequest.OccurredAt),
            "When the event happened is required.");
        var riskIds = ValidateRiskIds(request.RiskIds, nameof(ReassessmentEventRequest.RiskIds));

        if (request.IncidentId is not null && type != ReassessmentTriggerType.SignificantIncidentOrNearMiss)
            throw new InvalidParameterException(nameof(ReassessmentEventRequest.IncidentId),
                "Only a significant incident or near miss event references an incident.");

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var risks = await RequireVisibleRisksAsync(db, riskIds);

        if (request.IncidentId is { } incidentId)
        {
            if (!await db.Incidents.AnyAsync(i => i.Id == incidentId))
                throw new DataNotFoundException("incidents", incidentId.ToString(Invariant));

            // Checked unscoped: the event of this incident may be one the caller cannot see, and the unique index would
            // otherwise answer with a 500. Only its id is disclosed.
            await using var system = SystemContext();
            var existing = await system.ReassessmentEvents.AsNoTracking().Where(e => e.IncidentId == incidentId)
                .Select(e => (int?)e.Id).FirstOrDefaultAsync();
            if (existing is { } existingId)
                throw new DataAlreadyExistsException("local", "reassessment_events", existingId.ToString(Invariant),
                    $"Incident {incidentId} already has reassessment event {existingId}. Apply that event to more risks " +
                    "rather than declaring the incident twice.");
        }

        var reassessment = new ReassessmentEvent
        {
            TriggerType = type, Origin = ReassessmentEventOrigin.Declared, Title = title, Description = description,
            OccurredAt = occurredAt, IncidentId = request.IncidentId, DeclaredById = actingUserId, CreatedAt = now
        };

        // Stage 9.9 (S50 §4.3): a closed risk is skipped — unless it is archived and its archive watches this trigger,
        // in which case the event reopens it, in the same write as its trigger, once.
        var open = risks.Where(r => r.Status != Closed).ToList();
        var reopened = await RiskArchiveService.ReopenWatchingAsync(db,
            risks.Where(r => r.Status == Closed).ToList(), reassessment, now);

        if (open.Count == 0 && reopened.Count == 0)
            throw new RuleBrokenException(
                "Every risk named is closed, and no archive among them watches this trigger, so the event would reassess " +
                "nothing.", NoOpenRiskRule);

        db.ReassessmentEvents.Add(reassessment);

        var raised = Raise(db, reassessment, open.Concat(reopened.Select(r => r.Risk)), now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (request.IncidentId is { } racedIncident && IsDuplicate(ex, IncidentIndex))
        {
            // A concurrent declaration of the same incident won the unique index: the same 409 as when it is found first.
            await using var system = SystemContext();
            var winner = await system.ReassessmentEvents.AsNoTracking().Where(e => e.IncidentId == racedIncident)
                .Select(e => e.Id).FirstAsync();
            throw new DataAlreadyExistsException("local", "reassessment_events", winner.ToString(Invariant),
                $"Incident {racedIncident} already has reassessment event {winner}. Apply that event to more risks " +
                "rather than declaring the incident twice.");
        }

        Logger.Information("Reassessment event {Event} ({Type}) declared by user {User} on {Count} risk(s), {Reopened} archive(s) reopened",
            reassessment.Id, type, actingUserId, raised.Count, reopened.Count);

        await RiskArchiveService.NotifyReopenedAsync(db, notifications, reopened, reassessment);
        await NotifyTriggersAsync(db, reassessment, raised);

        var reopenedIds = reopened.Select(r => r.Risk.Id).ToHashSet();
        var dto = ToDto(reassessment, await ReadTriggersAsync(db,
            db.RiskReassessmentTriggers.Where(t => t.EventId == reassessment.Id)));
        dto.SkippedClosedRiskIds = risks.Where(r => r.Status == Closed && !reopenedIds.Contains(r.Id)).Select(r => r.Id)
            .OrderBy(id => id).ToList();
        dto.ReopenedArchivedRiskIds = reopenedIds.OrderBy(id => id).ToList();
        return dto;
    }

    public async Task<ReassessmentEventDto> AddEventRisksAsync(int eventId, ReassessmentRisksRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var riskIds = ValidateRiskIds(request.RiskIds, nameof(ReassessmentRisksRequest.RiskIds));
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        ReassessmentEvent reassessment;
        List<Risk> risks;
        List<int> already;
        List<(Risk Risk, RiskArchive Archive)> reopened;
        List<(RiskReassessmentTrigger Trigger, Risk Risk)> raised;

        for (var attempt = 1; ; attempt++)
        {
            reassessment = await db.ReassessmentEvents.FirstOrDefaultAsync(e => e.Id == eventId)
                           ?? throw new DataNotFoundException("reassessment_events", eventId.ToString(Invariant));

            if (reassessment.Origin == ReassessmentEventOrigin.KriBreach)
                throw new RuleBrokenException(
                    "The risks of a KRI breach are the risks the KRI governs: link the risk to the indicator instead.",
                    EventFromKriRule);

            risks = await RequireVisibleRisksAsync(db, riskIds);

            already = await db.RiskReassessmentTriggers.AsNoTracking()
                .Where(t => t.EventId == eventId && riskIds.Contains(t.RiskId)).Select(t => t.RiskId).ToListAsync();

            var fresh = risks.Where(r => r.Status != Closed && !already.Contains(r.Id)).ToList();

            // Stage 9.9 (S50 §4.3): an archived risk whose archive watches this trigger is reopened by it.
            reopened = await RiskArchiveService.ReopenWatchingAsync(db,
                risks.Where(r => r.Status == Closed && !already.Contains(r.Id)).ToList(), reassessment, now);

            raised = Raise(db, reassessment, fresh.Concat(reopened.Select(r => r.Risk)), now);
            if (raised.Count == 0) break;

            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (IsDuplicate(ex, TriggerIndex) && attempt < MaxRaceAttempts)
            {
                // A concurrent request applied the event to some of these risks first: re-read, and answer as the
                // idempotent call this is — those risks are listed as already triggered, not a 500.
                db.ChangeTracker.Clear();
            }
        }

        Logger.Information("Reassessment event {Event} applied by user {User} to {Count} more risk(s), {Reopened} archive(s) reopened",
            eventId, actingUserId, raised.Count, reopened.Count);

        await RiskArchiveService.NotifyReopenedAsync(db, notifications, reopened, reassessment);
        await NotifyTriggersAsync(db, reassessment, raised);

        var reopenedIds = reopened.Select(r => r.Risk.Id).ToHashSet();
        var dto = ToDto(reassessment, await ReadTriggersAsync(db,
            db.RiskReassessmentTriggers.Where(t => t.EventId == eventId)));
        dto.SkippedClosedRiskIds = risks.Where(r => r.Status == Closed && !reopenedIds.Contains(r.Id)).Select(r => r.Id)
            .OrderBy(id => id).ToList();
        dto.AlreadyTriggeredRiskIds = already.OrderBy(id => id).ToList();
        dto.ReopenedArchivedRiskIds = reopenedIds.OrderBy(id => id).ToList();
        return dto;
    }

    public async Task<List<ReassessmentTriggerDto>> GetTriggersAsync(int? riskId, bool pendingOnly)
    {
        await using var db = DalService.GetContext();

        var query = db.RiskReassessmentTriggers.AsQueryable();
        if (riskId is { } id)
        {
            if (!await db.Risks.AnyAsync(r => r.Id == id))
                throw new DataNotFoundException("risks", id.ToString(Invariant));
            query = query.Where(t => t.RiskId == id);
        }

        var triggers = await ReadTriggersAsync(db, query);

        if (pendingOnly) return triggers.Where(t => t.State == ReassessmentTriggerState.Pending).ToList();

        // The full history of every risk is bounded; a pending queue never is, so only the unfiltered list is capped.
        return riskId is null ? triggers.Take(MonitoringLimits.MaxListLimit).ToList() : triggers;
    }

    // --- evaluation ----------------------------------------------------------------------------------

    public async Task<KriEvaluationSummary> EvaluateAllAsync()
    {
        var summary = new KriEvaluationSummary();

        List<int> ids;
        await using (var db = SystemContext())
            ids = await db.Kris.AsNoTracking().Where(k => k.RetiredAt == null).OrderBy(k => k.Id)
                .Select(k => k.Id).ToListAsync();

        foreach (var id in ids)
        {
            // One failing indicator must not stop the others; it is logged and the pass moves on.
            try
            {
                await using var db = SystemContext();
                await EvaluateKriAsync(db, id, Clock(), summary);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Evaluating KRI {Kri} failed", id);
            }
        }

        return summary;
    }

    private async Task EvaluateInSystemContextAsync(int kriId)
    {
        await using var system = SystemContext();
        await EvaluateKriAsync(system, kriId, Clock(), new KriEvaluationSummary());
    }

    /// <summary>
    /// The breach-episode state machine of S49 §4.7, for one KRI, in <paramref name="db"/> (a system context): open an
    /// episode when the latest valid reading breaches and none is open, reuse the open one otherwise, raise the
    /// triggers still missing on the open risks it governs, and close the episode when the KRI is back within tolerance.
    /// Stale does not close an episode and no reading does not open one.
    /// </summary>
    private async Task EvaluateKriAsync(AuditableContext db, int kriId, DateTime now, KriEvaluationSummary summary)
    {
        var kri = await db.Kris.AsNoTracking().FirstOrDefaultAsync(k => k.Id == kriId);
        if (kri is null || kri.RetiredAt is not null) return;

        summary.KrisEvaluated++;

        var readings = (await ValidReadingsAsync(db, [kriId])).GetValueOrDefault(kriId) ?? [];
        var thresholds = Thresholds(kri);
        var status = KriEvaluator.Evaluate(thresholds, readings, now);

        if (status.State == KriState.Stale) summary.Stale++;
        if (status.State == KriState.Breached) summary.Breached++;

        var open = await db.ReassessmentEvents
            .Where(e => e.KriId == kriId && e.Origin == ReassessmentEventOrigin.KriBreach && e.KriBreachEndedAt == null)
            .OrderBy(e => e.Id).FirstOrDefaultAsync();

        if (status.LatestReadingId is null) return;

        if (!status.LastReadingBreached)
        {
            // Back within tolerance (or in warning): the episode ends. A stale KRI never reaches here — its last
            // reading is current-or-not, and only a reading within tolerance shows recovery.
            if (open is null || status.State is not (KriState.WithinTolerance or KriState.Warning)) return;

            open.KriBreachEndedAt = now;
            await db.SaveChangesAsync();
            summary.EpisodesClosed++;

            Logger.Information("KRI {Kri} back within tolerance; breach episode {Event} ended", kriId, open.Id);
            return;
        }

        var opened = false;
        if (open is null)
        {
            var opening = KriEvaluator.EpisodeOpening(thresholds, readings, now)!;

            // The last episode ended, and the breach run seen now was already recorded when it ended: the episode was
            // ended by a recovery that has since been voided, so this is the same breach — reopen it rather than raise
            // a second reassessment of every risk for one cause. A run recorded after the episode ended is a new
            // breach, however the voidings line up, and opens a new episode: when in doubt, reassess (S49 §4.7).
            var last = await db.ReassessmentEvents
                .Where(e => e.KriId == kriId && e.Origin == ReassessmentEventOrigin.KriBreach)
                .OrderByDescending(e => e.Id).FirstOrDefaultAsync();
            var sameBreach = last?.KriBreachEndedAt is { } endedAt && opening.RecordedAt < endedAt;

            // Otherwise the same opening reading means the same episode — one a concurrent evaluation just opened.
            open = sameBreach
                ? last
                : await db.ReassessmentEvents.FirstOrDefaultAsync(e => e.KriReadingId == opening.Id);

            if (open is not null)
            {
                if (open.KriBreachEndedAt is not null)
                {
                    open.KriBreachEndedAt = null;
                    await db.SaveChangesAsync();
                    Logger.Information("KRI {Kri}: the recovery that ended breach episode {Event} is gone; episode reopened",
                        kriId, open.Id);
                }
            }
            else
            {
                open = new ReassessmentEvent
                {
                    TriggerType = ReassessmentTriggerType.NewDataOrKriBreach,
                    Origin = ReassessmentEventOrigin.KriBreach,
                    Title = Truncate($"KRI '{kri.Name}' beyond its tolerance", MonitoringLimits.MaxTitleLength),
                    Description = Truncate(status.Explanation, MonitoringLimits.MaxDescriptionLength),
                    OccurredAt = opening.ObservedAt,
                    KriId = kriId,
                    KriReadingId = opening.Id,
                    CreatedAt = now
                };
                db.ReassessmentEvents.Add(open);

                try
                {
                    await db.SaveChangesAsync();
                    opened = true;
                    summary.EpisodesOpened++;
                }
                catch (DbUpdateException ex) when (IsDuplicate(ex, EpisodeIndex))
                {
                    // A concurrent evaluation won the unique index on the opening reading: re-read its episode and
                    // continue with it. Any other failure is not a race and propagates.
                    db.ChangeTracker.Clear();
                    open = await db.ReassessmentEvents.FirstAsync(e => e.KriReadingId == opening.Id);
                    Logger.Information("KRI {Kri}: a concurrent evaluation opened breach episode {Event} first", kriId, open.Id);
                }
            }
        }

        var episodeId = open.Id;
        List<int> governed = [];
        List<(RiskReassessmentTrigger Trigger, Risk Risk)> raised = [];
        List<(Risk Risk, RiskArchive Archive)> reopened = [];

        for (var attempt = 1; ; attempt++)
        {
            governed = await db.KriRisks.AsNoTracking().Where(l => l.KriId == kriId).Select(l => l.RiskId).ToListAsync();
            var triggered = await db.RiskReassessmentTriggers.AsNoTracking().Where(t => t.EventId == episodeId)
                .Select(t => t.RiskId).ToListAsync();

            var candidates = await db.Risks
                .Where(r => governed.Contains(r.Id) && !triggered.Contains(r.Id))
                .ToListAsync();
            var pending = candidates.Where(r => !string.Equals(r.Status, Closed, StringComparison.OrdinalIgnoreCase)).ToList();

            // Stage 9.9 (S50 §4.3): a closed risk the KRI governs is skipped — unless it is archived and its archive
            // watches KRI breaches; then this episode reopens it, in the same write as its trigger. A second evaluation
            // finds the trigger and the risk open, so the archive is reopened once however long the breach lasts.
            reopened = await RiskArchiveService.ReopenWatchingAsync(db,
                candidates.Where(r => string.Equals(r.Status, Closed, StringComparison.OrdinalIgnoreCase)).ToList(), open, now);

            raised = Raise(db, open, pending.Concat(reopened.Select(r => r.Risk)), now);
            if (raised.Count == 0) break;

            try
            {
                await db.SaveChangesAsync();
                summary.TriggersRaised += raised.Count;
                summary.ArchivesReopened += reopened.Count;
                break;
            }
            catch (DbUpdateException ex) when (IsDuplicate(ex, TriggerIndex) && attempt < MaxRaceAttempts)
            {
                // A concurrent evaluation raised some of these triggers first (unique on event and risk). The batch was
                // not written: re-read what exists and raise only what is still missing (S49 §4.7).
                db.ChangeTracker.Clear();
                open = await db.ReassessmentEvents.FirstAsync(e => e.Id == episodeId);
                Logger.Information("KRI {Kri}: a concurrent evaluation raised triggers of episode {Event} first; re-reading",
                    kriId, episodeId);
            }
        }

        if (opened)
        {
            Logger.Warning("KRI {Kri} '{Name}' beyond its tolerance: breach episode {Event} opened, {Count} risk(s) triggered",
                kriId, kri.Name, open.Id, raised.Count);
            await notifications.KriToleranceBreachedAsync(kri, status.LatestValue!.Value, status.LatestObservedAt!.Value,
                governed.Count);
        }

        await RiskArchiveService.NotifyReopenedAsync(db, notifications, reopened, open);
        await NotifyTriggersAsync(db, open, raised);
    }

    /// <summary>
    /// Adds a trigger of <paramref name="reassessment"/> on each risk and flags it for review — keeping the first reason
    /// and time when it is already flagged, as <c>RisksService.RequestReviewAsync</c> does. Not saved.
    /// </summary>
    private static List<(RiskReassessmentTrigger Trigger, Risk Risk)> Raise(AuditableContext db,
        ReassessmentEvent reassessment, IEnumerable<Risk> risks, DateTime now)
    {
        var raised = new List<(RiskReassessmentTrigger, Risk)>();

        foreach (var risk in risks)
        {
            var trigger = new RiskReassessmentTrigger { Event = reassessment, RiskId = risk.Id, RaisedAt = now, CreatedAt = now };
            db.RiskReassessmentTriggers.Add(trigger);

            if (!risk.ReviewRequested)
            {
                risk.ReviewRequested = true;
                risk.ReviewRequestedAt = now;
                risk.ReviewRequestedReason = $"Mandatory reassessment ({Label(reassessment.TriggerType)}): {reassessment.Title}";
                risk.LastUpdate = now;
            }

            raised.Add((trigger, risk));
        }

        return raised;
    }

    private async Task NotifyTriggersAsync(AuditableContext db, ReassessmentEvent reassessment,
        List<(RiskReassessmentTrigger Trigger, Risk Risk)> raised)
    {
        if (raised.Count == 0) return;

        var riskIds = raised.Select(r => r.Risk.Id).ToList();
        var scores = await db.RiskScorings.AsNoTracking().Where(s => riskIds.Contains(s.Id))
            .Select(s => new { s.Id, Score = s.ResidualRisk ?? s.CalculatedRisk })
            .ToDictionaryAsync(s => s.Id, s => (double?)s.Score);

        foreach (var (_, risk) in raised)
            await notifications.RiskReassessmentTriggeredAsync(risk, scores.GetValueOrDefault(risk.Id), reassessment);
    }

    // --- Gate B by indicator ------------------------------------------------------------------------

    /// <summary>
    /// Gate B by indicator for one risk (S49 §4.8): its linked, not retired KRIs, each evaluated at <paramref name="now"/>.
    /// <paramref name="unscoped"/> must be a context without entity scope — the caller has already checked that the risk
    /// is visible, and a gate must not depend on who asks (S49 D6).
    /// </summary>
    public static async Task<IndicatorAppetiteEvaluation> EvaluateIndicatorsAsync(AuditableContext unscoped, int riskId,
        DateTime now)
    {
        var kris = await unscoped.KriRisks.AsNoTracking().Where(l => l.RiskId == riskId)
            .Join(unscoped.Kris, l => l.KriId, k => k.Id, (l, k) => k)
            .Where(k => k.RetiredAt == null)
            .ToListAsync();

        var readings = await ValidReadingsAsync(unscoped, kris.Select(k => k.Id).ToList());

        return KriAppetite.Evaluate(kris.Select(k => KriAppetite.Gate(k.Id, k.Name, k.Category, k.Unit, k.Direction,
            k.ToleranceThreshold, KriEvaluator.Evaluate(Thresholds(k), readings.GetValueOrDefault(k.Id) ?? [], now))));
    }

    // --- reading -------------------------------------------------------------------------------------

    private async Task<KriDetailDto> BuildDetailAsync(AuditableContext db, Kri kri)
    {
        var now = Clock();

        var all = await db.KriReadings.AsNoTracking().Where(r => r.KriId == kri.Id)
            .OrderByDescending(r => r.ObservedAt).ThenByDescending(r => r.Id)
            .Take(MonitoringLimits.MaxReadingsReturned + 1)
            .ToListAsync();
        var valid = (await ValidReadingsAsync(db, [kri.Id])).GetValueOrDefault(kri.Id) ?? [];

        var risks = await db.KriRisks.AsNoTracking().Where(l => l.KriId == kri.Id)
            .Join(db.Risks, l => l.RiskId, r => r.Id, (l, r) => new KriLinkedRiskDto
            {
                RiskId = r.Id, Subject = r.Subject, Status = r.Status, LinkedAt = l.CreatedAt, LinkedById = l.CreatedById
            })
            .OrderBy(r => r.RiskId)
            .ToListAsync();

        var open = await OpenEpisodesAsync(db, [kri.Id]);

        var detail = ToDto(new KriDetailDto(), kri, valid, now, risks.Count, open.GetValueOrDefault(kri.Id));
        detail.ReadingsTruncated = all.Count > MonitoringLimits.MaxReadingsReturned;
        detail.Readings = all.Take(MonitoringLimits.MaxReadingsReturned).Select(r => new KriReadingDto
        {
            Id = r.Id, KriId = r.KriId, Value = r.Value, ObservedAt = r.ObservedAt, Note = r.Note,
            RecordedById = r.RecordedById, CreatedAt = r.CreatedAt, VoidedAt = r.VoidedAt, VoidedById = r.VoidedById,
            VoidReason = r.VoidReason,
            BeyondTolerance = KriEvaluator.Breaches(kri.Direction, kri.ToleranceThreshold, r.Value)
        }).ToList();
        detail.Risks = risks;
        return detail;
    }

    /// <summary>The triggers of <paramref name="query"/> with their event, their risk and their computed state.</summary>
    internal static async Task<List<ReassessmentTriggerDto>> ReadTriggersAsync(AuditableContext db,
        IQueryable<RiskReassessmentTrigger> query)
    {
        var rows = await query.AsNoTracking()
            // No IgnoreQueryFilters here: it is query-wide in EF Core, and would lift the trigger's and the risk's scope
            // filters along with the event's. A visible trigger makes its event visible anyway (S49 §4.11).
            .Join(db.ReassessmentEvents, t => t.EventId, e => e.Id, (t, e) => new { t, e })
            .Join(db.Risks, x => x.t.RiskId, r => r.Id, (x, r) => new
            {
                x.t.Id, x.t.EventId, x.t.RiskId, x.t.RaisedAt, x.e.TriggerType, x.e.Origin, x.e.Title,
                r.Subject, r.Status
            })
            .ToListAsync();

        var riskIds = rows.Select(r => r.RiskId).Distinct().ToList();
        var since = rows.Count == 0 ? DateTime.MaxValue : rows.Min(r => r.RaisedAt);
        var reviews = await db.MgmtReviews.AsNoTracking()
            .Where(m => riskIds.Contains(m.RiskId) && m.SubmissionDate >= since)
            .Select(m => new { m.Id, m.RiskId, m.SubmissionDate })
            .ToListAsync();

        return rows.Select(r =>
            {
                // The first review of the risk at or after the trigger answers it (S49 D9).
                var answer = reviews.Where(m => m.RiskId == r.RiskId && m.SubmissionDate >= r.RaisedAt)
                    .OrderBy(m => m.SubmissionDate).ThenBy(m => m.Id).FirstOrDefault();

                return new ReassessmentTriggerDto
                {
                    Id = r.Id, EventId = r.EventId, TriggerType = r.TriggerType, Origin = r.Origin, EventTitle = r.Title,
                    RiskId = r.RiskId, RiskSubject = r.Subject, RaisedAt = r.RaisedAt,
                    State = answer is not null ? ReassessmentTriggerState.Answered
                        : r.Status == Closed ? ReassessmentTriggerState.RiskClosed
                        : ReassessmentTriggerState.Pending,
                    AnsweredByReviewId = answer?.Id,
                    AnsweredAt = answer?.SubmissionDate
                };
            })
            .OrderByDescending(t => t.RaisedAt).ThenByDescending(t => t.Id)
            .ToList();
    }

    /// <summary>The valid (not voided) readings of each KRI, as the evaluation reads them.</summary>
    internal static async Task<Dictionary<int, List<KriObservation>>> ValidReadingsAsync(AuditableContext db,
        IReadOnlyCollection<int> kriIds)
    {
        var rows = await db.KriReadings.AsNoTracking()
            .Where(r => kriIds.Contains(r.KriId) && r.VoidedAt == null)
            .Select(r => new { r.KriId, r.Id, r.Value, r.ObservedAt, r.CreatedAt })
            .ToListAsync();

        return rows.GroupBy(r => r.KriId).ToDictionary(g => g.Key,
            g => g.Select(r => new KriObservation(r.Id, r.Value, r.ObservedAt, RecordedAt: r.CreatedAt)).ToList());
    }

    private static async Task<Dictionary<int, int>> OpenEpisodesAsync(AuditableContext db, IReadOnlyCollection<int> kriIds) =>
        (await db.ReassessmentEvents.AsNoTracking()
            .Where(e => e.KriId != null && kriIds.Contains(e.KriId.Value) && e.Origin == ReassessmentEventOrigin.KriBreach
                        && e.KriBreachEndedAt == null)
            .Select(e => new { KriId = e.KriId!.Value, e.Id })
            .ToListAsync())
        .GroupBy(e => e.KriId).ToDictionary(g => g.Key, g => g.Min(e => e.Id));

    internal static KriThresholds Thresholds(Kri kri) =>
        new(kri.Direction, kri.ToleranceThreshold, kri.WarningThreshold, kri.MaxReadingAgeDays, kri.RetiredAt is not null);

    private static T ToDto<T>(T dto, Kri kri, List<KriObservation> valid, DateTime now, int linkedRisks, int? openEpisode)
        where T : KriDto
    {
        dto.Id = kri.Id;
        dto.Name = kri.Name;
        dto.Description = kri.Description;
        dto.Category = kri.Category;
        dto.Source = kri.Source;
        dto.Unit = kri.Unit;
        dto.Direction = kri.Direction;
        dto.ToleranceThreshold = kri.ToleranceThreshold;
        dto.WarningThreshold = kri.WarningThreshold;
        dto.ToleranceRationale = kri.ToleranceRationale;
        dto.MaxReadingAgeDays = kri.MaxReadingAgeDays;
        dto.OwnerId = kri.OwnerId;
        dto.EntityId = kri.EntityId;
        dto.RetiredAt = kri.RetiredAt;
        dto.CreatedAt = kri.CreatedAt;
        dto.UpdatedAt = kri.UpdatedAt;
        dto.UpdatedById = kri.UpdatedById;
        dto.Status = KriEvaluator.Evaluate(Thresholds(kri), valid, now);
        dto.LinkedRisks = linkedRisks;
        dto.OpenBreachEventId = openEpisode;
        return dto;
    }

    private static ReassessmentEventDto ToDto(ReassessmentEvent e, List<ReassessmentTriggerDto> triggers) => new()
    {
        Id = e.Id,
        TriggerType = e.TriggerType,
        Origin = e.Origin,
        Title = e.Title,
        Description = e.Description,
        OccurredAt = e.OccurredAt,
        IncidentId = e.IncidentId,
        KriId = e.KriId,
        KriReadingId = e.KriReadingId,
        KriBreachEndedAt = e.KriBreachEndedAt,
        DeclaredById = e.DeclaredById,
        CreatedAt = e.CreatedAt,
        Triggers = triggers
    };

    // --- validation ----------------------------------------------------------------------------------

    private sealed record ValidDefinition(
        string Name, string? Description, KriCategory Category, string Source, string Unit, KriDirection Direction,
        decimal Tolerance, decimal? Warning, string Rationale, int MaxReadingAgeDays, int? OwnerId, int? EntityId);

    private static ValidDefinition ValidateDefinition(KriRequest request)
    {
        var name = Required(request.Name, MonitoringLimits.MaxNameLength, nameof(KriRequest.Name), "The KRI needs a name.");
        var description = Optional(request.Description, MonitoringLimits.MaxDescriptionLength, nameof(KriRequest.Description));

        if (request.Category is not { } category || !Enum.IsDefined(category))
            throw new InvalidParameterException(nameof(KriRequest.Category),
                "The category is unavailability (1), data loss (2), number of data subjects (3) or other (4).");

        var source = Required(request.Source, MonitoringLimits.MaxSourceLength, nameof(KriRequest.Source),
            "Say where the readings come from — the system, the report or the person.");
        var unit = Required(request.Unit, MonitoringLimits.MaxUnitLength, nameof(KriRequest.Unit),
            "The unit is required (%, hours, records…).");

        if (request.Direction is not { } direction || !Enum.IsDefined(direction))
            throw new InvalidParameterException(nameof(KriRequest.Direction),
                "The direction is 'worse when higher' (1) or 'worse when lower' (2).");

        var tolerance = RequireValue(request.ToleranceThreshold, nameof(KriRequest.ToleranceThreshold));

        decimal? warning = null;
        if (request.WarningThreshold is { } w)
        {
            warning = RequireValue(w, nameof(KriRequest.WarningThreshold));
            var goodSide = direction == KriDirection.HigherIsWorse ? w < tolerance : w > tolerance;
            if (!goodSide)
                throw new InvalidParameterException(nameof(KriRequest.WarningThreshold),
                    direction == KriDirection.HigherIsWorse
                        ? "The warning comes before the tolerance: below it, for an indicator that is worse when higher."
                        : "The warning comes before the tolerance: above it, for an indicator that is worse when lower.");
        }

        var rationale = Required(request.ToleranceRationale, MonitoringLimits.MaxRationaleLength,
            nameof(KriRequest.ToleranceRationale),
            "Record the Phase 0 decision that set the tolerance — the minutes or the approval reference.");

        var age = request.MaxReadingAgeDays ?? MonitoringLimits.DefaultMaxReadingAgeDays;
        if (age is < MonitoringLimits.MinReadingAgeDays or > MonitoringLimits.MaxReadingAgeDays)
            throw new InvalidParameterException(nameof(KriRequest.MaxReadingAgeDays),
                $"A reading stays current for {MonitoringLimits.MinReadingAgeDays} to {MonitoringLimits.MaxReadingAgeDays} days.");

        if (request.OwnerId is <= 0)
            throw new InvalidParameterException(nameof(KriRequest.OwnerId), "The owner is a user id.");
        if (request.EntityId is <= 0)
            throw new InvalidParameterException(nameof(KriRequest.EntityId), "The entity is an entity id.");

        return new ValidDefinition(name, description, category, source, unit, direction, tolerance, warning, rationale,
            age, request.OwnerId, request.EntityId);
    }

    private static async Task RequireReferencesAsync(AuditableContext db, ValidDefinition valid)
    {
        if (valid.OwnerId is { } owner && !await db.Users.AnyAsync(u => u.Value == owner))
            throw new DataNotFoundException("user", owner.ToString(Invariant));

        if (valid.EntityId is { } entity && !await db.Entities.AnyAsync(e => e.Id == entity))
            throw new DataNotFoundException("entities", entity.ToString(Invariant));
    }

    private static void Apply(Kri kri, ValidDefinition valid, int actingUserId)
    {
        kri.Name = valid.Name;
        kri.Description = valid.Description;
        kri.Category = valid.Category;
        kri.Source = valid.Source;
        kri.Unit = valid.Unit;
        kri.Direction = valid.Direction;
        kri.ToleranceThreshold = valid.Tolerance;
        kri.WarningThreshold = valid.Warning;
        kri.ToleranceRationale = valid.Rationale;
        kri.MaxReadingAgeDays = valid.MaxReadingAgeDays;
        kri.OwnerId = valid.OwnerId;
        kri.EntityId = valid.EntityId;
        kri.UpdatedById = actingUserId;
    }

    /// <summary>A change that can only make a breach less likely, or a stale KRI current again.</summary>
    private static bool Relaxes(Kri current, ValidDefinition next) =>
        current.Direction != next.Direction
        || (next.Direction == KriDirection.HigherIsWorse && next.Tolerance > current.ToleranceThreshold)
        || (next.Direction == KriDirection.LowerIsWorse && next.Tolerance < current.ToleranceThreshold)
        || next.MaxReadingAgeDays > current.MaxReadingAgeDays;

    private static List<int> ValidateRiskIds(List<int>? riskIds, string parameter)
    {
        if (riskIds is null || riskIds.Count == 0)
            throw new InvalidParameterException(parameter, "Name at least one risk the event obliges reassessing.");

        if (riskIds.Any(id => id <= 0))
            throw new InvalidParameterException(parameter, "A risk is a positive id.");

        var distinct = riskIds.Distinct().ToList();
        if (distinct.Count > MonitoringLimits.MaxEventRisks)
            throw new InvalidParameterException(parameter,
                $"One request applies an event to at most {MonitoringLimits.MaxEventRisks} risks.");

        return distinct;
    }

    /// <summary>The risks, every one visible to the caller — one missing or out of scope is a 404 and nothing is written.</summary>
    private static async Task<List<Risk>> RequireVisibleRisksAsync(AuditableContext db, List<int> riskIds)
    {
        var risks = await db.Risks.Where(r => riskIds.Contains(r.Id)).ToListAsync();

        var missing = riskIds.Except(risks.Select(r => r.Id)).OrderBy(id => id).FirstOrDefault();
        if (missing != 0) throw new DataNotFoundException("risks", missing.ToString(Invariant));

        return risks;
    }

    private static async Task<Kri> RequireKriAsync(AuditableContext db, int kriId, bool tracked)
    {
        var query = tracked ? db.Kris : db.Kris.AsNoTracking();
        return await query.FirstOrDefaultAsync(k => k.Id == kriId)
               ?? throw new DataNotFoundException("kris", kriId.ToString(Invariant));
    }

    /// <summary>
    /// A KRI's history is written by a caller whose scope holds the KRI's entity. An organization-wide KRI gates risks
    /// of every unit, so a caller scoped to some units may read it and link their own risks to it, but not record or
    /// void its readings — the same rule the write guard applies to the KRI itself (S49 §4.11). A 403 through the
    /// middleware, like any cross-entity write.
    /// </summary>
    private static void RequireWritableHistory(AuditableContext db, Kri kri)
    {
        if (!db.EntityScope.Allows(kri.EntityId))
            throw new DAL.Exceptions.EntityScopeViolationException(nameof(KriReading), kri.EntityId,
                db.EntityScope.ToString());
    }

    /// <summary>
    /// Moving a KRI to an entity is refused while it governs a risk of another one (S49 §4.3, D6): whoever sees the risk
    /// must see the indicator that gates it. Read unscoped — a link to a risk the caller cannot see counts too — and the
    /// message names no risk.
    /// </summary>
    private async Task RequireLinkedRisksInEntityAsync(int kriId, int? entityId)
    {
        if (entityId is null) return;

        await using var system = SystemContext();
        var foreign = await system.KriRisks.AsNoTracking().Where(l => l.KriId == kriId)
            .Join(system.Risks, l => l.RiskId, r => r.Id, (l, r) => r.EntityId)
            .CountAsync(e => e == null || e != entityId);

        if (foreign > 0)
            throw new RuleBrokenException(
                $"This indicator governs {foreign} risk(s) of another business entity. Unlink them, or keep the indicator " +
                "organization-wide, before giving it an entity.", KriEntityMismatchRule);
    }

    private static void RefuseRetired(Kri kri)
    {
        if (kri.RetiredAt is not null)
            throw new RuleBrokenException(
                $"The indicator was retired on {kri.RetiredAt:yyyy-MM-dd}: it takes no reading, no link and no change.",
                KriRetiredRule);
    }

    private static decimal RequireValue(decimal? value, string parameter)
    {
        if (value is not { } v)
            throw new InvalidParameterException(parameter, "A value is required.");

        if (Math.Abs(v) > MonitoringLimits.MaxValue)
            throw new InvalidParameterException(parameter,
                $"The value is at most {MonitoringLimits.MaxValue.ToString("N0", Invariant)} in absolute terms.");

        return v;
    }

    /// <summary>
    /// A UTC instant that is not in the future — a few minutes ahead is clock skew and is recorded as now, so the
    /// immediate evaluation sees it — and not before 2000. An unspecified kind is read as UTC.
    /// </summary>
    private static DateTime RequirePast(DateTime? value, DateTime now, string parameter, string missing)
    {
        if (value is not { } raw) throw new InvalidParameterException(parameter, missing);

        var utc = raw.Kind switch
        {
            DateTimeKind.Local => raw.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(raw, DateTimeKind.Utc),
            _ => raw
        };

        if (utc > now + MonitoringLimits.ClockSkew)
            throw new InvalidParameterException(parameter, "The date is in the future.");

        if (utc < MonitoringLimits.Earliest)
            throw new InvalidParameterException(parameter, "The date is before 2000.");

        return utc > now ? now : utc;
    }

    private static string Required(string? text, int max, string parameter, string missing)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidParameterException(parameter, missing);

        var trimmed = text.Trim();
        if (trimmed.Length > max)
            throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    private static string? Optional(string? text, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        if (trimmed.Length > max)
            throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    /// <summary>
    /// True when <paramref name="exception"/> is MariaDB's duplicate-key error (1062) on <paramref name="index"/> —
    /// recognised by the index name, which MariaDB puts in the message, as <c>RiskChainPersistence.IsDuplicateLink</c>
    /// does, so no other failure is ever mistaken for a lost race.
    /// </summary>
    internal static bool IsDuplicate(DbUpdateException exception, string index) =>
        ContinuityService.Mentions(exception, index);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    internal static string Label(ReassessmentTriggerType type) => type switch
    {
        ReassessmentTriggerType.ArchitectureOrTechnologyChange => "architecture or technology change",
        ReassessmentTriggerType.SupplierAcquisitionOrMigration => "new supplier, acquisition or migration",
        ReassessmentTriggerType.SignificantIncidentOrNearMiss => "significant incident or near miss",
        ReassessmentTriggerType.NewRegulation => "new regulation",
        ReassessmentTriggerType.NewAiModel => "new AI model",
        ReassessmentTriggerType.NewDataOrKriBreach => "new data or KRI beyond tolerance",
        _ => type.ToString()
    };

    private AuditableContext SystemContext() => DalService.GetContext(withIdentity: false, bypassEntityScope: true);
}
