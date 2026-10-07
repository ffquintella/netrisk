using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.TailRisk;

namespace Tools.TailRisk;

/// <summary>The monetary tolerances of one appetite that apply to one comparison — per scenario or per portfolio.</summary>
public sealed record TailLimits(double? MaxExpectedLoss, double? MaxP95, double? MaxCvar95)
{
    public bool Any => MaxExpectedLoss is not null || MaxP95 is not null || MaxCvar95 is not null;
}

/// <summary>
/// Gate B on the tail (Stage 9.7, S48 §4.7.2): the appetite's monetary tolerances against E[L], P95 and CVaR95.
///
/// The decision is on the point estimate, strictly above the limit; a limit inside a statistic's confidence
/// interval marks the outcome <c>Marginal</c> but never decides (S48 D9). Without a statistic the outcome is
/// <see cref="TailAppetiteState.NotAssessable"/> — never "within" (S48 D12).
/// </summary>
public static class TailAppetite
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Gate B on one risk's tail.</summary>
    /// <param name="appetiteId">The appetite that governs the risk, or null when none does.</param>
    /// <param name="entityId">That appetite's entity (null for the organization-wide one).</param>
    /// <param name="limits">Its scenario tolerances, or null when it has none.</param>
    /// <param name="run">The run compared — residual where it exists, inherent otherwise; null without statistics.</param>
    /// <param name="statistics">That run's statistics; null when the risk has none.</param>
    public static TailAppetiteEvaluation EvaluateScenario(int? appetiteId, int? entityId, TailLimits? limits,
        TailRun? run, TailStatistics? statistics)
    {
        var evaluation = Start(appetiteId, entityId, limits, statistics);
        evaluation.Run = statistics is null ? null : run;

        if (limits is not { Any: true })
        {
            evaluation.State = TailAppetiteState.NotConfigured;
            evaluation.Explanation = appetiteId is null
                ? "No risk appetite is configured, so the tail is not gated."
                : "The appetite that governs this risk sets no monetary tail tolerance, so the tail is not gated. " +
                  "The ordinal ceiling still applies.";
            return evaluation;
        }

        if (statistics is null)
        {
            evaluation.State = TailAppetiteState.NotAssessable;
            evaluation.Reasons.Add(TailAppetiteNotAssessableReason.NoTailStatistics);
            evaluation.Explanation =
                "The appetite sets a tail tolerance, but this risk has no tail statistics — it has no quantitative " +
                "analysis, or it was computed before schema 96 and not recomputed since. Gate B on the tail cannot be " +
                "assessed; it is not read as within tolerance, and the ordinal ceiling still applies.";
            return evaluation;
        }

        Compare(evaluation, limits, statistics);

        var subject = run == TailRun.Residual ? "residual" : "inherent";
        evaluation.Explanation = evaluation.State == TailAppetiteState.ExceedsTolerance
            ? $"The {subject} tail exceeds the appetite's tolerance: {Describe(evaluation.Breaches)}. Treat or escalate " +
              "(Gate B) — this risk cannot be accepted as it stands."
            : $"The {subject} tail is within the appetite's tolerance.";
        if (evaluation.Marginal)
            evaluation.Explanation += " A tolerance lies inside a confidence interval, so the outcome depends on " +
                                      "Monte Carlo noise; more iterations would settle it.";

        return evaluation;
    }

    /// <summary>
    /// Gate B on a portfolio's aggregated tail. Unquantified risks can only add loss — a non-negative loss added to a
    /// total raises its E[L], P95 and CVaR — so a breach measured on the quantified part is conclusive, and "within"
    /// with incomplete coverage is not assessable.
    /// </summary>
    public static TailAppetiteEvaluation EvaluatePortfolio(int? appetiteId, int? entityId, TailLimits? limits,
        TailStatistics? statistics, int quantifiedRisks, int totalRisks)
    {
        var evaluation = Start(appetiteId, entityId, limits, statistics);
        evaluation.QuantifiedRisks = quantifiedRisks;
        evaluation.TotalRisks = totalRisks;

        if (limits is not { Any: true })
        {
            evaluation.State = TailAppetiteState.NotConfigured;
            evaluation.Explanation = appetiteId is null
                ? "No risk appetite is configured, so the portfolio is not gated."
                : "The governing appetite sets no portfolio tolerance, so the portfolio is not gated.";
            return evaluation;
        }

        if (statistics is null || quantifiedRisks == 0)
        {
            evaluation.State = TailAppetiteState.NotAssessable;
            evaluation.Reasons.Add(TailAppetiteNotAssessableReason.NoQuantifiedRisks);
            evaluation.Explanation = "No risk of the portfolio has tail statistics, so the portfolio cannot be " +
                                     "compared with its tolerance.";
            return evaluation;
        }

        Compare(evaluation, limits, statistics);

        var coverage = $"{quantifiedRisks.ToString(Invariant)} of {totalRisks.ToString(Invariant)} risks quantified";

        if (evaluation.State == TailAppetiteState.ExceedsTolerance)
        {
            evaluation.Explanation = $"The aggregated exposure exceeds the portfolio tolerance: " +
                                     $"{Describe(evaluation.Breaches)} ({coverage}" +
                                     (quantifiedRisks < totalRisks
                                         ? "; the risks not quantified can only add to it)."
                                         : ").");
        }
        else if (quantifiedRisks < totalRisks)
        {
            evaluation.State = TailAppetiteState.NotAssessable;
            evaluation.Reasons.Add(TailAppetiteNotAssessableReason.IncompleteCoverage);
            evaluation.Explanation = $"The quantified part is within the portfolio tolerance, but only {coverage}: " +
                                     "the others can only add loss, so this is not evidence the portfolio is within it.";
        }
        else
        {
            evaluation.Explanation = $"The aggregated exposure is within the portfolio tolerance ({coverage}).";
        }

        if (evaluation.Marginal)
            evaluation.Explanation += " A tolerance lies inside a confidence interval.";

        return evaluation;
    }

    private static TailAppetiteEvaluation Start(int? appetiteId, int? entityId, TailLimits? limits,
        TailStatistics? statistics) => new()
    {
        AppetiteId = appetiteId,
        EntityId = appetiteId is null ? null : entityId,
        ExpectedLoss = statistics?.ExpectedLoss,
        P95 = statistics?.P95,
        Cvar95 = statistics?.Cvar95,
        MaxExpectedLoss = limits?.MaxExpectedLoss,
        MaxP95 = limits?.MaxP95,
        MaxCvar95 = limits?.MaxCvar95
    };

    private static void Compare(TailAppetiteEvaluation evaluation, TailLimits limits, TailStatistics statistics)
    {
        Check(evaluation, TailStatistic.ExpectedLoss, statistics.ExpectedLoss, statistics.ExpectedLossCiLow,
            statistics.ExpectedLossCiHigh, limits.MaxExpectedLoss);
        Check(evaluation, TailStatistic.P95, statistics.P95, statistics.P95CiLow, statistics.P95CiHigh, limits.MaxP95);
        Check(evaluation, TailStatistic.Cvar95, statistics.Cvar95, statistics.Cvar95CiLow, statistics.Cvar95CiHigh,
            limits.MaxCvar95);

        evaluation.State = evaluation.Breaches.Count > 0
            ? TailAppetiteState.ExceedsTolerance
            : TailAppetiteState.WithinTolerance;
    }

    private static void Check(TailAppetiteEvaluation evaluation, TailStatistic statistic, double value, double ciLow,
        double ciHigh, double? limit)
    {
        if (limit is not { } max) return;

        if (value > max) evaluation.Breaches.Add(new TailBreachDto { Statistic = statistic, Value = value, Limit = max });
        if (max >= ciLow && max <= ciHigh) evaluation.Marginal = true;
    }

    private static string Describe(IEnumerable<TailBreachDto> breaches) => string.Join("; ", breaches.Select(b =>
        $"{Name(b.Statistic)} {b.Value.ToString("N0", Invariant)} > {b.Limit.ToString("N0", Invariant)}"));

    private static string Name(TailStatistic statistic) => statistic switch
    {
        TailStatistic.ExpectedLoss => "E[L]",
        TailStatistic.P95 => "P95",
        TailStatistic.Cvar95 => "CVaR95",
        _ => throw new ArgumentOutOfRangeException(nameof(statistic))
    };
}
