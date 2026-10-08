using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Monitoring;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for MIGR-TI/IA Phase 7 monitoring — key risk indicators, their readings and the risks they govern, the
/// mandatory reassessment triggers and the methodology's metrics panel (Stage 9.8, S49 §7). Not the server's
/// <c>ServerServices.Interfaces.IMonitoringService</c>.
/// </summary>
public interface IMonitoringService
{
    Task<List<KriDto>> GetKrisAsync(bool includeRetired = false);

    Task<KriDetailDto> GetKriAsync(int kriId);

    Task<KriDto> CreateKriAsync(KriRequest request);

    Task<KriDto> UpdateKriAsync(int kriId, KriRequest request);

    Task<KriDto> RetireKriAsync(int kriId);

    Task<KriDetailDto> RecordReadingAsync(int kriId, KriReadingRequest request);

    Task<KriDetailDto> VoidReadingAsync(int kriId, int readingId, KriReadingVoidRequest request);

    Task<KriDetailDto> LinkRiskAsync(int kriId, int riskId);

    Task UnlinkRiskAsync(int kriId, int riskId);

    /// <param name="type">When given, only events of that trigger type.</param>
    /// <param name="limit">1–500; the server's default when null.</param>
    Task<List<ReassessmentEventDto>> GetEventsAsync(ReassessmentTriggerType? type = null, int? limit = null);

    Task<ReassessmentEventDto> DeclareEventAsync(ReassessmentEventRequest request);

    Task<ReassessmentEventDto> AddEventRisksAsync(int eventId, ReassessmentRisksRequest request);

    /// <param name="riskId">When given, only the triggers of that risk.</param>
    /// <param name="pendingOnly">Only the triggers no management review has answered yet.</param>
    Task<List<ReassessmentTriggerDto>> GetTriggersAsync(int? riskId = null, bool pendingOnly = false);

    Task<MethodologyMetricsDto> GetMetricsAsync();
}
