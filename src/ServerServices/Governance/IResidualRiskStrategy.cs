using DAL.Entities;

namespace ServerServices.Governance;

/// <summary>
/// How a risk's post-treatment score is derived from its pre-treatment score
/// (Track 8 milestone 8.2.1).
///
/// An interface rather than a formula in the calculation job because there is no single right
/// answer: <c>inherent × (1 − effective_mitigation)</c> is defensible and is what v1 ships, but an
/// organization scoring quantitatively (8.7) supplies its own, and one that models control
/// effectiveness independently of the treatment percentage will want a third. The strategy is named
/// by the <c>risk_workflow_residual_strategy</c> setting.
/// </summary>
public interface IResidualRiskStrategy
{
    /// <summary>The name this strategy is selected by in <c>settings</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The residual score, or <c>null</c> when the strategy cannot express one for this risk.
    ///
    /// Null is meaningful and is not the same as "residual equals inherent": the first says nobody
    /// has assessed the treatment, the second says the treatment achieves nothing, and an auditor
    /// cares which.
    /// </summary>
    float? Compute(ResidualRiskContext context);
}

/// <summary>Everything a strategy is allowed to see. A record so a strategy cannot mutate state.</summary>
public sealed record ResidualRiskContext(
    Risk Risk,
    RiskScoring Scoring,
    Mitigation? Mitigation,
    IReadOnlyList<MitigationToControl> Controls);
