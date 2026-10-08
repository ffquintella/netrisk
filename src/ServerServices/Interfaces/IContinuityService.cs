using System.Security.Claims;
using Model.Continuity;

namespace ServerServices.Interfaces;

/// <summary>
/// Business impact analysis and continuity (Stage 9.3, S43 §6): BIA objectives on processes and IT
/// services, the dependencies between them and their cascade, restoration tests, the Phase 7 metric
/// and the two parameters.
///
/// Every <b>write</b> needs unrestricted (global) scope, checked before any query runs: a scoped caller
/// gets the same <see cref="Model.Exceptions.PermissionInvalidException"/> (permission
/// <c>global_scope</c>) for an existing entity and a missing one, so no write route is an existence
/// oracle. Reads cover the whole organisation, as the entity map does.
/// </summary>
public interface IContinuityService
{
    /// <summary>Every business process and IT service, with or without a BIA.</summary>
    Task<List<ContinuitySubjectDto>> GetSubjectsAsync();

    /// <summary>The full continuity profile of one node.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">Entity missing.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>entity_not_bia_subject</c>.</exception>
    Task<ContinuityProfileDto> GetProfileAsync(int entityId, ClaimsPrincipal? user);

    /// <summary>Creates or replaces the BIA of a node.</summary>
    Task<BusinessImpactAnalysisWriteResult> SaveBiaAsync(int entityId, BusinessImpactAnalysisRequest request,
        int? actingUserId);

    Task DeleteBiaAsync(int entityId);

    Task<BiaDependencyDto> AddDependencyAsync(int entityId, BiaDependencyCreateRequest request, int? actingUserId);

    Task DeleteDependencyAsync(int entityId, int dependencyId);

    /// <summary>The node's restoration tests, most recent first, voided ones included.</summary>
    Task<List<RestorationTestDto>> GetRestorationTestsAsync(int entityId);

    Task<RestorationTestDto> RecordRestorationTestAsync(int entityId, RestorationTestCreateRequest request,
        int? actingUserId);

    Task<RestorationTestDto> VoidRestorationTestAsync(int entityId, int testId, RestorationTestVoidRequest request,
        int? actingUserId);

    /// <summary>The Phase 7 metric "restoration tested vs declared RTO/RPO".</summary>
    Task<RestorationVerificationMetricDto> GetRestorationVerificationMetricAsync();

    /// <summary>
    /// Every active critical process whose RTO/RPO is threatened (weight &gt; 0), with the transitive closure of
    /// its providers — the flag 4 basis Stage 9.5 derives from (S46 §4.5). Organisation-wide, like every read
    /// here: processes and services carry no scope column.
    /// </summary>
    Task<List<CriticalProcessThreatDto>> GetCriticalProcessThreatsAsync();

    /// <summary>The two parameters in force; a missing or invalid row reads as the default.</summary>
    Task<ContinuitySettingsDto> GetSettingsAsync();

    /// <summary>Stores both parameters in one save, or refuses both.</summary>
    Task<ContinuitySettingsDto> SaveSettingsAsync(ContinuitySettingsRequest request);

    /// <summary>
    /// The organisation's continuity graph — processes and services with their effective criticality and objectives, and
    /// the declared dependencies between them — exactly as the profile and the metric read it. Stage 9.10 (S51 §4.8)
    /// computes third-party concentration and the contracted RTO/RPO requirement over it, so the two cannot disagree.
    /// </summary>
    Task<Tools.Continuity.ContinuityGraph> GetGraphAsync();
}
