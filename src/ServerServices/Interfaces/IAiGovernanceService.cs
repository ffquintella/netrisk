using DAL.Entities;
using DAL.Enums;
using Model.AiGovernance;

namespace ServerServices.Interfaces;

/// <summary>
/// AI governance (Stage 9.12, S53 §6): the model inventory — purpose, data, vendor and version, with the risk tier, the
/// human oversight, the owner and the evaluation's validity — with its findings; the data each model uses, read against the
/// Stage 9.11 catalogue; the metric readings (accuracy, precision, recall, calibration, drift) and the human overrides the
/// override rate is computed from; and the register's risks that involve a model, which derive flag 11.
///
/// <b>Governance, never use</b> (S53 D1): nothing here runs a model, takes a model's output as a decision, or lets a model
/// act. A model is not a principal — it has no credential and no user — and no method here accepts, approves, reviews,
/// closes or votes on anything; the decision services are not dependencies of this one (T215).
///
/// A model is entity-scoped like a third party: read by the caller's scope (the organization's models by everyone), and
/// written only by a caller whose scope includes the model's own entity. The risk links follow the risk and the model.
///
/// Errors: <see cref="Model.Exceptions.InvalidParameterException"/> (400, naming the field),
/// <see cref="DAL.Exceptions.EntityScopeViolationException"/> (403 — a write outside the caller's scope),
/// <see cref="Model.Exceptions.DataNotFoundException"/> (404 — missing or outside the scope),
/// <see cref="Model.Exceptions.DataAlreadyExistsException"/> (409 — a model name already used),
/// <see cref="Model.Exceptions.RuleBrokenException"/> (422, naming the rule).
/// </summary>
public interface IAiGovernanceService
{
    // --- the inventory (T212) ----------------------------------------------------------------------------------------

    /// <summary>The models the caller sees, retired ones only when asked; only those with findings when asked.</summary>
    Task<List<AiModelSummaryDto>> GetModelsAsync(AiModelStatus? status, bool includeRetired, bool withFindingsOnly);

    Task<AiModelDto> GetModelAsync(int modelId);

    Task<List<AuditLog>> GetHistoryAsync(int modelId, int limit);

    Task<AiModelDto> CreateAsync(AiModelRequest request, int actingUserId);

    Task<AiModelDto> UpdateAsync(int modelId, AiModelRequest request, int actingUserId);

    /// <summary>Retires the model with a written reason. Terminal: a retired model takes no write and derives nothing.</summary>
    Task<AiModelDto> RetireAsync(int modelId, AiGovernanceReasonRequest request, int actingUserId);

    /// <summary>Declares, whole, the data records the model uses — an empty list is a declaration.</summary>
    Task<AiModelDto> SetDataAsync(int modelId, AiModelDataRequest request, int actingUserId);

    // --- metrics and overrides (T214, T215) --------------------------------------------------------------------------

    Task<List<AiModelReadingDto>> GetReadingsAsync(int modelId, AiModelMetric? metric, bool includeVoided);

    /// <summary>Records a reading of the model's current version; the override rate is computed, never taken.</summary>
    Task<AiModelReadingDto> RecordReadingAsync(int modelId, AiModelReadingRequest request, int actingUserId);

    Task<AiModelReadingDto> VoidReadingAsync(int modelId, int readingId, AiGovernanceReasonRequest request, int actingUserId);

    Task<List<AiModelOverrideDto>> GetOverridesAsync(int modelId, bool includeVoided);

    /// <summary>Records a person's decision contrary to the model, with the caller as its author and a written reason.</summary>
    Task<AiModelOverrideDto> RecordOverrideAsync(int modelId, AiModelOverrideRequest request, int actingUserId);

    Task<AiModelOverrideDto> VoidOverrideAsync(int modelId, int overrideId, AiGovernanceReasonRequest request,
        int actingUserId);

    // --- the register's risks (T213) ---------------------------------------------------------------------------------

    /// <summary>The models a visible risk involves; links to models the caller cannot see are counted, never named.</summary>
    Task<RiskAiModelsDto> GetRiskModelsAsync(int riskId);

    /// <summary>Links a visible risk to a visible model that is not retired; linking again only restates the note.</summary>
    Task<RiskAiModelsDto> LinkRiskAsync(int modelId, int riskId, AiModelRiskLinkRequest request, int actingUserId);

    Task UnlinkRiskAsync(int modelId, int riskId, int actingUserId);

    // --- the methodology panel (M10) ---------------------------------------------------------------------------------

    /// <summary>The models in use the caller sees, by evaluation state and by metric (S53 §4.9).</summary>
    Task<AiModelMetricsSummaryDto> GetMetricsSummaryAsync();
}
