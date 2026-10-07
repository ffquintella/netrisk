using DAL.Enums;
using JetBrains.Annotations;
using Model.TailRisk;
using Tools.TailRisk;
using Xunit;

namespace Tools.Tests.TailRisk;

/// <summary>
/// Stage 9.7 (S48 §4.7.2, §8 TA1–TA6) — Gate B on the tail: the four states, the strict comparison on the point
/// estimate, the informational marginal flag, and the portfolio's coverage rule.
/// </summary>
[TestSubject(typeof(TailAppetite))]
public class TailAppetiteTest
{
    /// <summary>A low-frequency scenario's statistics: small E[L], zero P95, large CVaR.</summary>
    private static readonly TailStatistics Rare = new()
    {
        Iterations = 10_000, ExpectedLoss = 137_000, ExpectedLossCiLow = 120_000, ExpectedLossCiHigh = 154_000,
        P95 = 0, P95CiLow = 0, P95CiHigh = 0, Cvar95 = 2_740_000, Cvar95CiLow = 2_400_000, Cvar95CiHigh = 3_080_000,
        ProbabilityOfLoss = 0.03, ConditionalLoss = 4_700_000
    };

    /// <summary>TA1 — no appetite, or an appetite without tail tolerances: not configured.</summary>
    [Fact]
    public void TestTA1_WithoutToleranceTheTailIsNotConfigured()
    {
        var none = TailAppetite.EvaluateScenario(null, null, null, TailRun.Inherent, Rare);
        Assert.Equal(TailAppetiteState.NotConfigured, none.State);
        Assert.Null(none.AppetiteId);

        var empty = TailAppetite.EvaluateScenario(4, 100, new TailLimits(null, null, null), TailRun.Inherent, Rare);
        Assert.Equal(TailAppetiteState.NotConfigured, empty.State);
        Assert.Equal(4, empty.AppetiteId);
        Assert.Contains("ordinal ceiling still applies", empty.Explanation);
    }

    /// <summary>TA2 — a tolerance and no statistics: not assessable with the reason — never "within".</summary>
    [Fact]
    public void TestTA2_WithoutStatisticsTheTailIsNotAssessable()
    {
        var result = TailAppetite.EvaluateScenario(4, null, new TailLimits(null, null, 1_000_000), null, null);

        Assert.Equal(TailAppetiteState.NotAssessable, result.State);
        Assert.Equal([TailAppetiteNotAssessableReason.NoTailStatistics], result.Reasons);
        Assert.Null(result.Run);
        Assert.Empty(result.Breaches);
    }

    /// <summary>TA3 — every tolerance holds: within, with the values and limits reported.</summary>
    [Fact]
    public void TestTA3_WithinTolerance()
    {
        var result = TailAppetite.EvaluateScenario(4, null, new TailLimits(500_000, 100_000, 5_000_000),
            TailRun.Residual, Rare);

        Assert.Equal(TailAppetiteState.WithinTolerance, result.State);
        Assert.Equal(TailRun.Residual, result.Run);
        Assert.Equal(2_740_000, result.Cvar95);
        Assert.Equal(5_000_000, result.MaxCvar95);
        Assert.False(result.Marginal);
        Assert.Contains("residual", result.Explanation);
    }

    /// <summary>
    /// TA4 — the reason the gate exists: a rare catastrophic scenario is within an E[L] tolerance and a P95 one, and
    /// exceeds only the CVaR tolerance. The comparison is strict: equal to the limit is within.
    /// </summary>
    [Fact]
    public void TestTA4_OnlyTheTailExceedsAndTheComparisonIsStrict()
    {
        var result = TailAppetite.EvaluateScenario(4, null, new TailLimits(500_000, 100_000, 1_000_000),
            TailRun.Inherent, Rare);

        Assert.Equal(TailAppetiteState.ExceedsTolerance, result.State);
        var breach = Assert.Single(result.Breaches);
        Assert.Equal(TailStatistic.Cvar95, breach.Statistic);
        Assert.Equal(2_740_000, breach.Value);
        Assert.Equal(1_000_000, breach.Limit);
        Assert.Contains("CVaR95 2,740,000 > 1,000,000", result.Explanation);
        Assert.Contains("cannot be accepted", result.Explanation);

        var equal = TailAppetite.EvaluateScenario(4, null, new TailLimits(null, null, 2_740_000), TailRun.Inherent, Rare);
        Assert.Equal(TailAppetiteState.WithinTolerance, equal.State);
    }

    /// <summary>TA5 — a limit inside a confidence interval marks the outcome marginal, whichever way it went.</summary>
    [Fact]
    public void TestTA5_ALimitInsideTheIntervalIsMarginal()
    {
        var exceeds = TailAppetite.EvaluateScenario(4, null, new TailLimits(null, null, 2_500_000), TailRun.Inherent, Rare);
        Assert.Equal(TailAppetiteState.ExceedsTolerance, exceeds.State);
        Assert.True(exceeds.Marginal);

        var within = TailAppetite.EvaluateScenario(4, null, new TailLimits(null, null, 3_000_000), TailRun.Inherent, Rare);
        Assert.Equal(TailAppetiteState.WithinTolerance, within.State);
        Assert.True(within.Marginal);
        Assert.Contains("Monte Carlo noise", within.Explanation);
    }

    /// <summary>
    /// TA6 — the portfolio: unquantified risks can only add loss, so an excess on the quantified part is conclusive
    /// and "within" with incomplete coverage is not assessable; nothing quantified is not assessable either.
    /// </summary>
    [Fact]
    public void TestTA6_PortfolioCoverage()
    {
        var limits = new TailLimits(null, null, 1_000_000);

        var partialExcess = TailAppetite.EvaluatePortfolio(1, null, limits, Rare, 3, 5);
        Assert.Equal(TailAppetiteState.ExceedsTolerance, partialExcess.State);
        Assert.Equal(3, partialExcess.QuantifiedRisks);
        Assert.Equal(5, partialExcess.TotalRisks);
        Assert.Contains("can only add", partialExcess.Explanation);

        var partialWithin = TailAppetite.EvaluatePortfolio(1, null, new TailLimits(null, null, 9_000_000), Rare, 3, 5);
        Assert.Equal(TailAppetiteState.NotAssessable, partialWithin.State);
        Assert.Equal([TailAppetiteNotAssessableReason.IncompleteCoverage], partialWithin.Reasons);

        var complete = TailAppetite.EvaluatePortfolio(1, null, new TailLimits(null, null, 9_000_000), Rare, 5, 5);
        Assert.Equal(TailAppetiteState.WithinTolerance, complete.State);

        var nothing = TailAppetite.EvaluatePortfolio(1, null, limits, null, 0, 5);
        Assert.Equal([TailAppetiteNotAssessableReason.NoQuantifiedRisks], nothing.Reasons);

        var unconfigured = TailAppetite.EvaluatePortfolio(null, null, null, Rare, 5, 5);
        Assert.Equal(TailAppetiteState.NotConfigured, unconfigured.State);
    }
}
