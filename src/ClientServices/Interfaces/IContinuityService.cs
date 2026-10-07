using Model.Continuity;

namespace ClientServices.Interfaces;

/// <summary>
/// The desktop client's view of business impact analysis and continuity (Stage 9.3, S43 §7):
/// <c>/Continuity</c>.
///
/// Refusals keep the server's explanation. A 404 is a <see cref="Model.Exceptions.DataNotFoundException"/>;
/// a 400, 403, 409 or 422 is an <see cref="Model.Exceptions.InvalidHttpRequestException"/> whose message is
/// the server's body — "The RTO cannot exceed the MTPD/MAO…" is something a person can act on.
/// </summary>
public interface IContinuityService
{
    Task<List<ContinuitySubjectDto>> GetSubjectsAsync();

    Task<ContinuityProfileDto> GetProfileAsync(int entityId);

    /// <summary>Creates (201) or replaces (200) the node's BIA; both return the stored analysis.</summary>
    Task<BusinessImpactAnalysisDto> SaveBiaAsync(int entityId, BusinessImpactAnalysisRequest request);

    Task DeleteBiaAsync(int entityId);

    Task<BiaDependencyDto> AddDependencyAsync(int entityId, BiaDependencyCreateRequest request);

    Task DeleteDependencyAsync(int entityId, int dependencyId);

    Task<List<RestorationTestDto>> GetRestorationTestsAsync(int entityId);

    Task<RestorationTestDto> RecordRestorationTestAsync(int entityId, RestorationTestCreateRequest request);

    /// <summary>Voids a test; the reason goes in the body, never in the URL.</summary>
    Task<RestorationTestDto> VoidRestorationTestAsync(int entityId, int testId, string reason);

    Task<RestorationVerificationMetricDto> GetRestorationVerificationMetricAsync();

    Task<ContinuitySettingsDto> GetSettingsAsync();

    Task<ContinuitySettingsDto> SaveSettingsAsync(ContinuitySettingsRequest request);
}
