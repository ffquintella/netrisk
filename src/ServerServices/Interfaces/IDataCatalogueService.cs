using DAL.Entities;
using DAL.Enums;
using Model.DataCatalogue;

namespace ServerServices.Interfaces;

/// <summary>
/// The LGPD data catalogue (Stage 9.11, S52 §6): the catalogue of each <c>organizationData</c> record — personal-data
/// category, purposes with their legal basis, retention, location and international transfer — with its findings; the
/// legal and contractual requirements; the RIPD (DPIA) linked to data records and processes; and the requirements of a
/// risk, as links instead of free text.
///
/// <b>A catalogue of kinds of data, never of data</b>: nothing here stores or logs a data subject's value, and an expired
/// retention is a finding — nothing is ever deleted because of one (S52 D5).
///
/// The catalogue, the requirements and the RIPDs are the organization's, like the entity map they describe: read by the
/// whole audience of the read policy, and <b>every write needs global scope</b> (S52 D10). The risk links follow the
/// risk's scope.
///
/// Errors: <see cref="Model.Exceptions.InvalidParameterException"/> (400, naming the field),
/// <see cref="Model.Exceptions.PermissionInvalidException"/> (403 — a write without global scope),
/// <see cref="Model.Exceptions.DataNotFoundException"/> (404 — missing or outside the scope),
/// <see cref="Model.Exceptions.DataAlreadyExistsException"/> (409 — a requirement code already used),
/// <see cref="Model.Exceptions.RuleBrokenException"/> (422, naming the rule).
/// </summary>
public interface IDataCatalogueService
{
    // --- data records (T207, T210) ---------------------------------------------------------------------------------

    /// <summary>Every <c>organizationData</c> record with its catalogue state and findings; only those with findings when asked.</summary>
    Task<List<DataRecordSummaryDto>> GetRecordsAsync(bool withFindingsOnly);

    Task<DataRecordDto> GetRecordAsync(int entityId);

    Task<List<AuditLog>> GetRecordHistoryAsync(int entityId, int limit);

    /// <summary>Catalogues the record, or replaces its catalogue whole.</summary>
    Task<DataRecordDto> SaveRecordAsync(int entityId, DataCatalogueEntryRequest request, int actingUserId);

    // --- legal requirements (T209) ---------------------------------------------------------------------------------

    Task<List<LegalRequirementDto>> GetRequirementsAsync();

    Task<List<AuditLog>> GetRequirementHistoryAsync(int requirementId, int limit);

    Task<LegalRequirementDto> CreateRequirementAsync(LegalRequirementRequest request, int actingUserId);

    Task<LegalRequirementDto> UpdateRequirementAsync(int requirementId, LegalRequirementRequest request, int actingUserId);

    /// <summary>Deletes a requirement nothing cites; one in use answers 422 <c>legal_requirement_in_use</c>.</summary>
    Task DeleteRequirementAsync(int requirementId, int actingUserId);

    // --- RIPD / DPIA (T208) ----------------------------------------------------------------------------------------

    Task<List<DpiaSummaryDto>> GetDpiasAsync(DpiaStatus? status);

    Task<DpiaDto> GetDpiaAsync(int dpiaId);

    Task<List<AuditLog>> GetDpiaHistoryAsync(int dpiaId, int limit);

    Task<DpiaDto> CreateDpiaAsync(DpiaRequest request, int actingUserId);

    /// <summary>Replaces a draft; an approved or retired RIPD is frozen (422 <c>dpia_not_draft</c>).</summary>
    Task<DpiaDto> UpdateDpiaAsync(int dpiaId, DpiaRequest request, int actingUserId);

    Task<DpiaDto> LinkDpiaAsync(int dpiaId, int entityId, int actingUserId);

    Task UnlinkDpiaAsync(int dpiaId, int entityId, int actingUserId);

    /// <summary>Approves a complete draft; the approver is the acting user, never the third line.</summary>
    Task<DpiaDto> ApproveDpiaAsync(int dpiaId, int actingUserId);

    Task<DpiaDto> RetireDpiaAsync(int dpiaId, DpiaRetireRequest request, int actingUserId);

    // --- the requirements of a risk (T209) -------------------------------------------------------------------------

    Task<RiskComplianceDto> GetRiskComplianceAsync(int riskId);

    Task<RiskComplianceDto> LinkRiskRequirementAsync(int riskId, int requirementId, RiskLegalRequirementRequest request,
        int actingUserId);

    Task UnlinkRiskRequirementAsync(int riskId, int requirementId, int actingUserId);
}
