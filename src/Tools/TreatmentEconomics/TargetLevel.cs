using System.Globalization;
using Model.TreatmentEconomics;

namespace Tools.TreatmentEconomics;

/// <summary>
/// The target risk level against where the risk is now and against the appetite (Stage 9.6, S47 §4.9) —
/// MIGR-TI/IA Phase 0's "target (after the planned treatment, within appetite)".
///
/// A target above the appetite is reported, not refused (S47 D9): an owner may plan a treatment that cannot reach
/// the appetite, and that is exactly the case that has to be seen and escalated.
/// </summary>
public static class TargetLevel
{
    /// <param name="currentScore">Residual score, or inherent where no residual exists (the appetite's rule).</param>
    /// <param name="currentExpectedLoss">Residual mean annual loss, or the inherent mean.</param>
    /// <param name="appetiteCeiling">The ceiling of the appetite in force; null when none is configured.</param>
    public static RiskTargetStatusDto Evaluate(decimal? targetScore, decimal? targetExpectedLoss, DateOnly? targetDate,
        double? currentScore, double? currentExpectedLoss, double? appetiteCeiling, DateOnly today)
    {
        var status = new RiskTargetStatusDto
        {
            CurrentScore = currentScore,
            CurrentExpectedLoss = currentExpectedLoss,
            AppetiteCeiling = appetiteCeiling
        };

        if (targetScore is { } score && currentScore is { } nowScore)
        {
            status.ScoreGap = nowScore - (double)score;
            status.ScoreMet = nowScore <= (double)score;
        }

        if (targetExpectedLoss is { } loss && currentExpectedLoss is { } nowLoss)
        {
            status.ExpectedLossGap = nowLoss - (double)loss;
            status.ExpectedLossMet = nowLoss <= (double)loss;
        }

        if (targetScore is { } declared && appetiteCeiling is { } ceiling)
            status.WithinAppetite = (double)declared <= ceiling;

        status.Overdue = targetDate is { } due && today > due && (status.ScoreMet == false || status.ExpectedLossMet == false);

        var parts = new List<string>();

        if (status.ScoreMet is { } scoreMet)
            parts.Add(scoreMet
                ? $"The score ({Num(currentScore!.Value)}) is at or below the target ({Num((double)targetScore!.Value)})."
                : $"The score ({Num(currentScore!.Value)}) is {Num(status.ScoreGap!.Value)} above the target ({Num((double)targetScore!.Value)}).");
        else if (targetScore is not null)
            parts.Add("The risk has no score to compare with the target score.");

        if (status.ExpectedLossMet is { } lossMet)
            parts.Add(lossMet
                ? "The expected loss is at or below the target."
                : $"The expected loss is {Num(status.ExpectedLossGap!.Value)} above the target.");
        else if (targetExpectedLoss is not null)
            parts.Add("The risk has no quantitative analysis to compare with the target expected loss.");

        if (status.WithinAppetite == false)
            parts.Add($"The target score is above the appetite ceiling ({Num(appetiteCeiling!.Value)}): even when met, the " +
                      "risk stays outside the appetite and has to be escalated.");

        if (status.Overdue)
            parts.Add($"The target date ({targetDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) has passed.");

        status.Explanation = string.Join(" ", parts);
        return status;
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
