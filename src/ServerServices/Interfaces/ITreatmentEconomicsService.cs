using Model.TreatmentEconomics;

namespace ServerServices.Interfaces;

/// <summary>
/// Treatment economics (Stage 9.6, S47): the treatment option and monetary cost of a mitigation, Gate C on it,
/// the target risk level, and Gate D's portfolio selection.
///
/// A mitigation or risk outside the caller's entity scope is <see cref="Model.Exceptions.DataNotFoundException"/>,
/// the same as a missing one. Gate A precedes everything here (S47 §3): "accept" cannot be declared while it holds,
/// Gate C is informational for a Gate A risk, and Gate D selects Gate A treatments first.
/// </summary>
public interface ITreatmentEconomicsService
{
    /// <summary>The economics of one mitigation, with Gate C and its action plan.</summary>
    Task<MitigationEconomicsDto> GetMitigationAsync(int mitigationId);

    /// <summary>Replaces the option, the monetary cost, the estimates and the prerequisites of a mitigation.</summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">An invalid field, named.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>gate_a_non_discretionary</c> for "accept" while Gate A holds;
    /// <c>dependency_cycle</c> when a prerequisite already depends on this mitigation.</exception>
    Task<MitigationEconomicsDto> SaveMitigationAsync(int mitigationId, MitigationEconomicsRequest request, int actingUserId);

    /// <summary>Gate A, the protected flags, the appetite, the target and every visible mitigation of a risk.</summary>
    Task<RiskTreatmentEconomicsDto> GetRiskAsync(int riskId);

    /// <summary>Sets the target risk level.</summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">An invalid field, named.</exception>
    Task<RiskTargetDto> SaveTargetAsync(int riskId, RiskTargetRequest request, int actingUserId);

    /// <summary>Removes the target risk level.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No visible risk, or no target on it.</exception>
    Task DeleteTargetAsync(int riskId, int actingUserId);

    /// <summary>Gate D: the selection under the constraints, computed and never stored.</summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">An invalid constraint or an unknown mitigation id.</exception>
    Task<PortfolioSelectionDto> SelectPortfolioAsync(PortfolioSelectionRequest request);
}
