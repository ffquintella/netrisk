using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.AiGovernance;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for AI governance (Stage 9.12, S53 §7): the model inventory with its findings and evaluation, the data a
/// model uses, the metric readings and human overrides, and the models a risk involves. No view consumes it yet — the
/// desktop surface is T310.
/// </summary>
public interface IAiGovernanceService
{
    Task<List<AiModelSummaryDto>> GetModelsAsync(AiModelStatus? status = null, bool includeRetired = false,
        bool withFindingsOnly = false);

    Task<AiModelDto> GetModelAsync(int modelId);

    Task<List<DAL.Entities.AuditLog>> GetHistoryAsync(int modelId, int limit = 500);

    Task<AiModelDto> CreateModelAsync(AiModelRequest request);

    Task<AiModelDto> UpdateModelAsync(int modelId, AiModelRequest request);

    Task<AiModelDto> RetireModelAsync(int modelId, AiGovernanceReasonRequest request);

    Task<AiModelDto> SetDataAsync(int modelId, AiModelDataRequest request);

    Task<List<AiModelReadingDto>> GetReadingsAsync(int modelId, AiModelMetric? metric = null, bool includeVoided = false);

    Task<AiModelReadingDto> RecordReadingAsync(int modelId, AiModelReadingRequest request);

    Task<AiModelReadingDto> VoidReadingAsync(int modelId, int readingId, AiGovernanceReasonRequest request);

    Task<List<AiModelOverrideDto>> GetOverridesAsync(int modelId, bool includeVoided = false);

    Task<AiModelOverrideDto> RecordOverrideAsync(int modelId, AiModelOverrideRequest request);

    Task<AiModelOverrideDto> VoidOverrideAsync(int modelId, int overrideId, AiGovernanceReasonRequest request);

    Task<RiskAiModelsDto> GetRiskModelsAsync(int riskId);

    Task<RiskAiModelsDto> LinkRiskAsync(int modelId, int riskId, AiModelRiskLinkRequest? request = null);

    Task UnlinkRiskAsync(int modelId, int riskId);
}
