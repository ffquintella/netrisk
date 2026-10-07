using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.RiskFlags;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the eleven mandatory flags, Gate A, the Phase 4 decision and the Top Risks list
/// (Stage 9.5, S46 §7). The desktop surface that uses it is T303.
/// </summary>
public interface IRiskFlagsService
{
    Task<List<RiskFlagDescriptor>> GetCatalogueAsync();

    Task<RiskFlagsStateDto> GetRiskFlagsAsync(int riskId);

    Task<RiskFlagsStateDto> RefreshAsync(int riskId);

    Task<RiskFlagsStateDto> DeclareAsync(int riskId, RiskFlagCode code, string reason);

    Task<RiskFlagsStateDto> WithdrawAsync(int riskId, RiskFlagCode code, string reason);

    Task<List<RiskDecisionDto>> GetDecisionsAsync(int riskId);

    Task<RiskDecisionDto> RecordDecisionAsync(int riskId, RiskDecisionKind decision, string reason);

    Task<List<FlaggedRiskDto>> GetFlaggedAsync(RiskFlagCode? flag = null, bool? gateA = null);

    Task<TopRisksDto> GetTopRisksAsync(int limit = 10);
}
