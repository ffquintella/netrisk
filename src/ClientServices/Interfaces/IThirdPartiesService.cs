using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.ThirdParties;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the third-party register (Stage 9.10, S51 §7): suppliers, clouds and identity providers, what they
/// supply or process, sub-processors, data locations, HECVAT assessments, SBOMs and the concentration. No view consumes it
/// yet — the desktop surface is T308.
/// </summary>
public interface IThirdPartiesService
{
    Task<List<ThirdPartySummaryDto>> GetThirdPartiesAsync(ThirdPartyStatus? status = null, bool includeTerminated = false);

    Task<ThirdPartyDto> GetThirdPartyAsync(int thirdPartyId);

    Task<ThirdPartyConcentrationReportDto> GetConcentrationAsync();

    Task<List<EntityThirdPartyDto>> GetByEntityAsync(int entityId);

    Task<ThirdPartyAssessmentDto> GetAssessmentAsync(int thirdPartyId, int assessmentId);

    Task<ThirdPartySbomDto> GetSbomAsync(int thirdPartyId, int sbomId);

    Task<List<DAL.Entities.AuditLog>> GetHistoryAsync(int thirdPartyId, int limit = 500);

    Task<ThirdPartyDto> CreateAsync(ThirdPartyRequest request);

    Task<ThirdPartyDto> UpdateAsync(int thirdPartyId, ThirdPartyRequest request);

    Task DeleteAsync(int thirdPartyId);

    Task<ThirdPartyDto> LinkAsync(int thirdPartyId, int entityId, ThirdPartyLinkRequest? request = null);

    Task UnlinkAsync(int thirdPartyId, int entityId);

    Task<ThirdPartyDto> SetSubprocessorsAsync(int thirdPartyId, ThirdPartySubprocessorsRequest request);

    Task<ThirdPartyDto> SetDataLocationsAsync(int thirdPartyId, ThirdPartyDataLocationsRequest request);

    Task<ThirdPartyAssessmentDto> RecordAssessmentAsync(int thirdPartyId, ThirdPartyAssessmentRequest request);

    Task<ThirdPartyAssessmentDto> ReplaceAnswersAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentAnswersRequest request);

    Task<ThirdPartyAssessmentDto> VoidAssessmentAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentVoidRequest request);

    Task<ThirdPartySbomDto> ImportSbomAsync(int thirdPartyId, ThirdPartySbomRequest request);

    Task DeleteSbomAsync(int thirdPartyId, int sbomId);
}
