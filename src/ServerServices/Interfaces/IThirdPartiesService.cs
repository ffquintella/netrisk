using DAL.Enums;
using Model.ThirdParties;

namespace ServerServices.Interfaces;

/// <summary>
/// The third-party register (Stage 9.10, S51 §6): suppliers, clouds and identity providers as first-class records, with
/// their contract terms, what they supply or process (IT services, processes, data records), sub-processors, data
/// locations, HECVAT assessments and SBOMs, and the concentration by supplier, cloud and identity.
///
/// Reads follow the caller's entity scope (a third party with no entity is the organization's and seen by every
/// reader); the concentration counts of what is listed are computed over the whole organization (S51 D8). Writes are
/// refused by the write guard (403) when they would file a record outside the caller's scope.
///
/// Errors: <see cref="Model.Exceptions.InvalidParameterException"/> (400, naming the field),
/// <see cref="Model.Exceptions.DataNotFoundException"/> (404 — missing or outside the scope),
/// <see cref="Model.Exceptions.DataAlreadyExistsException"/> (409 — a name or an SBOM already registered),
/// <see cref="Model.Exceptions.RuleBrokenException"/> (422, naming the rule).
/// </summary>
public interface IThirdPartiesService
{
    Task<List<ThirdPartySummaryDto>> GetThirdPartiesAsync(ThirdPartyStatus? status, bool includeTerminated);

    Task<ThirdPartyDto> GetThirdPartyAsync(int thirdPartyId);

    /// <summary>Concentration by supplier, cloud and identity (T203), over the third parties the caller sees.</summary>
    Task<ThirdPartyConcentrationReportDto> GetConcentrationAsync();

    /// <summary>The third parties linked to an IT service, a process or a data record (T204), read from its side.</summary>
    Task<List<EntityThirdPartyDto>> GetByEntityAsync(int entityId);

    Task<ThirdPartyAssessmentDto> GetAssessmentAsync(int thirdPartyId, int assessmentId);

    Task<ThirdPartySbomDto> GetSbomAsync(int thirdPartyId, int sbomId);

    /// <summary>
    /// The field-level trail of a third party and of what it currently declares (links, sub-processors, data locations,
    /// assessments, SBOMs), newest first — read here, after the visibility check, because the generic
    /// <c>/AuditTrail/{type}/{id}</c> reader cannot apply the caller's scope (S51 §4.10).
    /// </summary>
    Task<List<DAL.Entities.AuditLog>> GetHistoryAsync(int thirdPartyId, int limit);

    Task<ThirdPartyDto> CreateAsync(ThirdPartyRequest request, int actingUserId);

    Task<ThirdPartyDto> UpdateAsync(int thirdPartyId, ThirdPartyRequest request, int actingUserId);

    /// <summary>Deletes a third party nothing refers to; one in use is refused with <c>third_party_in_use</c>.</summary>
    Task DeleteAsync(int thirdPartyId, int actingUserId);

    Task<ThirdPartyDto> LinkAsync(int thirdPartyId, int entityId, ThirdPartyLinkRequest request, int actingUserId);

    Task UnlinkAsync(int thirdPartyId, int entityId, int actingUserId);

    Task<ThirdPartyDto> SetSubprocessorsAsync(int thirdPartyId, ThirdPartySubprocessorsRequest request, int actingUserId);

    Task<ThirdPartyDto> SetDataLocationsAsync(int thirdPartyId, ThirdPartyDataLocationsRequest request, int actingUserId);

    Task<ThirdPartyAssessmentDto> RecordAssessmentAsync(int thirdPartyId, ThirdPartyAssessmentRequest request,
        int actingUserId);

    Task<ThirdPartyAssessmentDto> ReplaceAnswersAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentAnswersRequest request, int actingUserId);

    Task<ThirdPartyAssessmentDto> VoidAssessmentAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentVoidRequest request, int actingUserId);

    Task<ThirdPartySbomDto> ImportSbomAsync(int thirdPartyId, ThirdPartySbomRequest request, int actingUserId);

    Task DeleteSbomAsync(int thirdPartyId, int sbomId, int actingUserId);
}
