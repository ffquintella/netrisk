using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.DataCatalogue;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the LGPD data catalogue (Stage 9.11, S52 §7): the catalogue of each data record with its findings, the
/// legal and contractual requirements, the RIPD (DPIA) and the requirements of a risk. No view consumes it yet — the
/// desktop surface is T309.
/// </summary>
public interface IDataCatalogueService
{
    Task<List<DataRecordSummaryDto>> GetRecordsAsync(bool withFindingsOnly = false);

    Task<DataRecordDto> GetRecordAsync(int entityId);

    Task<List<DAL.Entities.AuditLog>> GetRecordHistoryAsync(int entityId, int limit = 500);

    Task<DataRecordDto> SaveRecordAsync(int entityId, DataCatalogueEntryRequest request);

    Task<List<LegalRequirementDto>> GetRequirementsAsync();

    Task<List<DAL.Entities.AuditLog>> GetRequirementHistoryAsync(int requirementId, int limit = 500);

    Task<LegalRequirementDto> CreateRequirementAsync(LegalRequirementRequest request);

    Task<LegalRequirementDto> UpdateRequirementAsync(int requirementId, LegalRequirementRequest request);

    Task DeleteRequirementAsync(int requirementId);

    Task<List<DpiaSummaryDto>> GetDpiasAsync(DpiaStatus? status = null);

    Task<DpiaDto> GetDpiaAsync(int dpiaId);

    Task<List<DAL.Entities.AuditLog>> GetDpiaHistoryAsync(int dpiaId, int limit = 500);

    Task<DpiaDto> CreateDpiaAsync(DpiaRequest request);

    Task<DpiaDto> UpdateDpiaAsync(int dpiaId, DpiaRequest request);

    Task<DpiaDto> LinkDpiaAsync(int dpiaId, int entityId);

    Task UnlinkDpiaAsync(int dpiaId, int entityId);

    Task<DpiaDto> ApproveDpiaAsync(int dpiaId);

    Task<DpiaDto> RetireDpiaAsync(int dpiaId, DpiaRetireRequest request);

    Task<RiskComplianceDto> GetRiskComplianceAsync(int riskId);

    Task<RiskComplianceDto> LinkRiskRequirementAsync(int riskId, int requirementId, RiskLegalRequirementRequest? request = null);

    Task UnlinkRiskRequirementAsync(int riskId, int requirementId);
}
