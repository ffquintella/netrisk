using ServerServices.Interfaces;

namespace ServerServices.Governance;

/// <summary>
/// The v1 residual formula (Track 8 milestone 8.2.1): <c>residual = inherent × (1 − effective)</c>,
/// where the effective mitigation percentage is the treatment's own percentage combined with the
/// validated percentages of the controls attached to it.
///
/// The controls compose as independent reducers rather than adding up:
/// <c>1 − Π(1 − pᵢ)</c>. Adding them lets three 40% controls "remove" 120% of a risk, which is the
/// arithmetic mistake that makes a residual number stop being believable. Composing them means the
/// score approaches zero and never reaches it, which is also the honest answer — a treated risk is
/// not an absent one.
///
/// The treatment percentage and the control-derived percentage are combined the same way, so a
/// mitigation that claims 50% and carries a control validated at 50% lands at 75%, not 100%.
/// </summary>
public class MitigationPercentResidualStrategy : IResidualRiskStrategy
{
    public const string StrategyName = "MitigationPercent";

    public string Name => StrategyName;

    public float? Compute(ResidualRiskContext context)
    {
        // No treatment at all: residual is genuinely unassessed rather than equal to inherent, and
        // the caller distinguishes those.
        if (context.Mitigation is null) return null;

        var effective = EffectiveMitigation(context);
        var residual = context.Scoring.CalculatedRisk * (1 - effective);

        return residual < 0 ? 0 : (float)residual;
    }

    /// <summary>
    /// The combined effectiveness, 0–1. Exposed because 8.7.2's before/after simulation applies the
    /// same number to the loss magnitude, and the two must not drift apart.
    /// </summary>
    public static double EffectiveMitigation(ResidualRiskContext context)
    {
        var retained = 1.0;

        var planned = Clamp(context.Mitigation?.MitigationPercent ?? 0);
        retained *= 1 - planned;

        foreach (var control in context.Controls)
        {
            var validated = Clamp(control.ValidationMitigationPercent ?? 0);
            retained *= 1 - validated;
        }

        return 1 - retained;
    }

    /// <summary>Percentages arrive as 0–100 integers and are not validated at the edge, so a row
    /// carrying 250 must not turn into a negative residual.</summary>
    private static double Clamp(int percent) => percent switch
    {
        <= 0 => 0,
        >= 100 => 1,
        _ => percent / 100.0
    };
}
