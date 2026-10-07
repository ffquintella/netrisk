using System.Threading.Tasks;
using Model.TreatmentEconomics;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for treatment economics — the treatment option and monetary cost of a mitigation, Gate C, the target
/// risk level and Gate D's portfolio selection (Stage 9.6, S47 §7). The desktop surface that uses it is T304.
/// </summary>
public interface ITreatmentEconomicsService
{
    Task<MitigationEconomicsDto> GetMitigationAsync(int mitigationId);

    Task<MitigationEconomicsDto> SaveMitigationAsync(int mitigationId, MitigationEconomicsRequest request);

    Task<RiskTreatmentEconomicsDto> GetRiskAsync(int riskId);

    Task<RiskTargetDto> SaveTargetAsync(int riskId, RiskTargetRequest request);

    Task DeleteTargetAsync(int riskId);

    Task<PortfolioSelectionDto> SelectPortfolioAsync(PortfolioSelectionRequest request);
}
