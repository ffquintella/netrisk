using DAL.Enums;
using Model.Monitoring;

namespace ServerServices.Interfaces;

/// <summary>
/// Stage 9.8 (S49) — key risk indicators and the mandatory reassessment triggers of MIGR-TI/IA Phase 7: the KRI
/// register with its tolerance and history, the risks each governs, the evaluation that detects a breach and raises a
/// reassessment once per episode, and the declared trigger events.
///
/// Errors: <see cref="Model.Exceptions.InvalidParameterException"/> (400) naming the field;
/// <see cref="Model.Exceptions.DataNotFoundException"/> (404) for a KRI, reading, risk, incident or event that does not
/// exist or is outside the caller's scope; <see cref="Model.Exceptions.DataAlreadyExistsException"/> (409) for a second
/// event of the same incident; <see cref="Model.Exceptions.RuleBrokenException"/> (422) with <c>kri_retired</c>,
/// <c>kri_entity_mismatch</c>, <c>kri_reading_already_voided</c>, <c>reassessment_event_from_kri</c> or
/// <c>reassessment_no_open_risk</c>; <see cref="DAL.Exceptions.EntityScopeViolationException"/> (403, by the
/// middleware) for a KRI — or a reading of one — written outside the caller's entities.
/// </summary>
public interface IMonitoringService
{
    /// <summary>The KRIs the caller can see, with their state; retired ones only when asked.</summary>
    Task<List<KriDto>> GetKrisAsync(bool includeRetired);

    /// <summary>A KRI with its state, its readings (newest first, voided included) and the risks it governs.</summary>
    Task<KriDetailDto> GetKriAsync(int kriId);

    Task<KriDto> CreateKriAsync(KriRequest request, int actingUserId);

    /// <summary>
    /// Updates a KRI's definition and re-evaluates it — a new tolerance can open or close a breach episode. Giving it an
    /// entity while it governs a risk of another one is refused (<c>kri_entity_mismatch</c>).
    /// </summary>
    Task<KriDto> UpdateKriAsync(int kriId, KriRequest request, int actingUserId);

    /// <summary>Retires a KRI (idempotent): it stops being evaluated and gating, and its open breach episode ends.</summary>
    Task<KriDto> RetireKriAsync(int kriId, int actingUserId);

    /// <summary>Records a reading and evaluates the KRI at once.</summary>
    Task<KriDetailDto> RecordReadingAsync(int kriId, KriReadingRequest request, int actingUserId);

    /// <summary>Voids a reading with a reason — it stays in the history, ignored — and re-evaluates the KRI.</summary>
    Task<KriDetailDto> VoidReadingAsync(int kriId, int readingId, KriReadingVoidRequest request, int actingUserId);

    /// <summary>Links a risk to a KRI (idempotent) and evaluates the KRI, so a breach in progress triggers the risk.</summary>
    Task<KriDetailDto> LinkRiskAsync(int kriId, int riskId, int actingUserId);

    /// <summary>
    /// Unlinks a risk from a KRI — the KRI stops gating it. The link is found through the risk's visibility alone, so the
    /// unit of a risk can remove a KRI it cannot see.
    /// </summary>
    Task UnlinkRiskAsync(int kriId, int riskId, int actingUserId);

    /// <summary>The reassessment events the caller can see, newest first, with their visible triggers.</summary>
    Task<List<ReassessmentEventDto>> GetEventsAsync(ReassessmentTriggerType? type, int? limit);

    /// <summary>Declares an event and raises its trigger on each open risk named.</summary>
    Task<ReassessmentEventDto> DeclareEventAsync(ReassessmentEventRequest request, int actingUserId);

    /// <summary>Applies a declared event to more risks; a risk it already triggered is not triggered again.</summary>
    Task<ReassessmentEventDto> AddEventRisksAsync(int eventId, ReassessmentRisksRequest request, int actingUserId);

    /// <summary>The triggers the caller can see, optionally of one risk, optionally only the pending ones.</summary>
    Task<List<ReassessmentTriggerDto>> GetTriggersAsync(int? riskId, bool pendingOnly);

    /// <summary>
    /// Evaluates every active KRI — the nightly pass (S49 §4.7). Opens and closes breach episodes and raises the
    /// triggers still missing; running it twice changes nothing the second time.
    /// </summary>
    Task<KriEvaluationSummary> EvaluateAllAsync();
}

/// <summary>
/// Stage 9.8 (S49 §4.9) — the methodology's metrics panel: the ten Phase 7 metrics, each with whether it is available
/// and which stage delivers it, and the health of the KRIs and the reassessment triggers. Computed on read, never stored.
/// </summary>
public interface IMethodologyMetricsService
{
    Task<MethodologyMetricsDto> GetAsync();
}
