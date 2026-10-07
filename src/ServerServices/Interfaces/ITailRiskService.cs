using Model.TailRisk;

namespace ServerServices.Interfaces;

/// <summary>
/// Stage 9.7 (S48) — tail statistics and portfolio: the loss magnitude by form of loss, the tail of a risk with Gate B
/// and the flag 8 criterion, the declared correlations between scenarios, the portfolio aggregation and the appetite's
/// monetary tolerances.
///
/// Errors: <see cref="Model.Exceptions.InvalidParameterException"/> (400) for an invalid request, naming the field;
/// <see cref="Model.Exceptions.DataNotFoundException"/> (404) for a risk, correlation or appetite that does not exist
/// or is outside the caller's scope; <see cref="Model.Exceptions.RuleBrokenException"/> (422) with
/// <c>correlation_not_positive_semidefinite</c> or <c>correlation_group_too_large</c>.
/// </summary>
public interface ITailRiskService
{
    /// <summary>The tail of a risk: its runs, declared components, Gate B on the tail and the flag 8 criterion.</summary>
    Task<RiskTailDto> GetRiskAsync(int riskId);

    /// <summary>Replaces a risk's loss components and recomputes its analysis when it has one.</summary>
    Task<RiskTailDto> SaveLossComponentsAsync(int riskId, LossComponentsRequest request, int actingUserId);

    /// <summary>Removes a risk's loss components (404 when it has none) and recomputes its analysis when it has one.</summary>
    Task<RiskTailDto> DeleteLossComponentsAsync(int riskId, int actingUserId);

    /// <summary>The declared correlations whose two risks the caller can see, optionally those of one risk.</summary>
    Task<List<RiskCorrelationDto>> GetCorrelationsAsync(int? riskId);

    /// <summary>Declares or updates the correlation of a pair, refusing a matrix that is not positive semidefinite.</summary>
    Task<RiskCorrelationDto> SaveCorrelationAsync(RiskCorrelationRequest request, int actingUserId);

    Task DeleteCorrelationAsync(int correlationId, int actingUserId);

    /// <summary>Aggregates a portfolio's annual losses under the declared correlation. Computed, never stored.</summary>
    Task<PortfolioTailDto> AggregatePortfolioAsync(PortfolioTailRequest request);

    /// <summary>The monetary tolerances of an appetite (404 when it has none).</summary>
    Task<RiskAppetiteTailLimitsDto> GetAppetiteLimitsAsync(int appetiteId);

    Task<RiskAppetiteTailLimitsDto> SaveAppetiteLimitsAsync(int appetiteId, RiskAppetiteTailLimitsRequest request,
        int actingUserId);

    Task DeleteAppetiteLimitsAsync(int appetiteId, int actingUserId);
}
