using System.Collections.Generic;
using System.Threading.Tasks;
using Model.TailRisk;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for tail statistics and portfolio — the tail of a risk, its loss components, the declared correlations,
/// the portfolio aggregation and the appetite's monetary tolerances (Stage 9.7, S48 §7). Not the server's
/// <c>ServerServices.Interfaces.ITailRiskService</c>.
/// </summary>
public interface ITailRiskService
{
    Task<RiskTailDto> GetRiskAsync(int riskId);

    Task<RiskTailDto> SaveLossComponentsAsync(int riskId, LossComponentsRequest request);

    Task<RiskTailDto> DeleteLossComponentsAsync(int riskId);

    /// <param name="riskId">When given, only the correlations of that risk.</param>
    Task<List<RiskCorrelationDto>> GetCorrelationsAsync(int? riskId = null);

    Task<RiskCorrelationDto> SaveCorrelationAsync(RiskCorrelationRequest request);

    Task DeleteCorrelationAsync(int correlationId);

    Task<PortfolioTailDto> AggregatePortfolioAsync(PortfolioTailRequest request);

    Task<RiskAppetiteTailLimitsDto> GetAppetiteLimitsAsync(int appetiteId);

    Task<RiskAppetiteTailLimitsDto> SaveAppetiteLimitsAsync(int appetiteId, RiskAppetiteTailLimitsRequest request);

    Task DeleteAppetiteLimitsAsync(int appetiteId);
}
