using System;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Tools.TailRisk;
using Xunit;

namespace Tools.Tests.TailRisk;

/// <summary>
/// Stage 9.7 (S48 §4.3, §8) — the tail estimators: P95 with an order-statistic interval, the Acerbi–Tasche CVaR95 with
/// a seeded bootstrap interval, E[L] with a central-limit interval, the probability of a loss year and the mean loss of
/// a loss year. Every sample is seeded, so each assertion is a hard one; the tolerances are on Monte Carlo quantities
/// against their analytic values and are wide enough to hold for any seed, narrow enough to catch a wrong estimator.
/// </summary>
[TestSubject(typeof(TailStatisticsCalculator))]
public class TailStatisticsCalculatorTest
{
    private static double[] Sorted(params double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy;
    }

    /// <summary>TS1 — CVaR ≥ P95 ≥ 0, and P95 is the same interpolated percentile P10/P50/P90 use.</summary>
    [Fact]
    public void TestTS1_CvarIsAtLeastTheP95WhichIsTheSharedPercentile()
    {
        var result = MonteCarloRiskSimulator.Run(new CalibratedRange(0.5, 2, 5),
            new CalibratedRange(10_000, 50_000, 500_000), 10_000, 20260826);

        var sorted = (double[])MonteCarloRiskSimulator.Simulate(new CalibratedRange(0.5, 2, 5),
            [new CalibratedRange(10_000, 50_000, 500_000)], 10_000, 20260826).Losses.Clone();
        Array.Sort(sorted);

        Assert.Equal(MonteCarloRiskSimulator.Percentile(sorted, 0.95), result.Tail.P95, 9);
        Assert.Equal(result.P95, result.Tail.P95, 9);
        Assert.True(result.P95 >= result.P90);
        Assert.True(result.Tail.Cvar95 >= result.Tail.P95);
        Assert.True(result.Tail.P95 > 0);
    }

    /// <summary>
    /// TS2 (T187) — a scenario that loses in about 3 % of years: its P10, P50, P90 and P95 are all zero, and its CVaR95
    /// is not. The worst 5 % of years contain the loss years, so the CVaR is close to P(loss) × E[loss | loss] / 0.05.
    /// Reading the 0.05 point of the exceedance curve — the obvious implementation — gives zero here.
    /// </summary>
    [Fact]
    public void TestTS2_TheCvarOfALowFrequencyScenarioIsNotZeroWhereItsP95Is()
    {
        var result = MonteCarloRiskSimulator.Run(new CalibratedRange(0.01, 0.03, 0.06),
            new CalibratedRange(1_000_000, 4_000_000, 10_000_000), 10_000, 20260826);

        Assert.Equal(0, result.P10);
        Assert.Equal(0, result.P50);
        Assert.Equal(0, result.P90);
        Assert.Equal(0, result.P95);
        Assert.Equal(0, result.Tail.P95CiLow);

        // The 0.05 point of the stored curve — what "P95" would read without this stage — is zero too.
        Assert.Equal(0, result.LossExceedanceCurve.Single(p => System.Math.Abs(p.Probability - 0.05) < 1e-9).Loss);

        var tail = result.Tail;
        Assert.InRange(tail.ProbabilityOfLoss, 0.02, 0.045);
        Assert.True(tail.Cvar95 > 1_000_000, $"CVaR95 {tail.Cvar95:N0} should be well above zero");

        // E[magnitude] of PERT(1M, 4M, 10M) = (1 + 4·4 + 10)/6 M = 4.5M; a loss year averages a little more (two events).
        Assert.NotNull(tail.ConditionalLoss);
        Assert.InRange(tail.ConditionalLoss!.Value, 4_000_000, 5_500_000);

        // Every loss year sits in the 5 % tail, so CVaR95 = P(loss)·E[L | L > 0] / 0.05 exactly on this sample.
        Assert.Equal(tail.ProbabilityOfLoss * tail.ConditionalLoss.Value / 0.05, tail.Cvar95, 1e-6 * tail.Cvar95);
        Assert.True(tail.Cvar95CiLow > 0);
    }

    /// <summary>TS3 — the Acerbi–Tasche estimator by hand: a whole tail weight, and a fractional one.</summary>
    [Fact]
    public void TestTS3_TheExpectedShortfallEstimatorByHand()
    {
        // n = 40, α = 0.95: k = 2, the two largest.
        var forty = Enumerable.Range(1, 40).Select(i => (double)i).ToArray();
        Assert.Equal((40 + 39) / 2.0, TailStatisticsCalculator.ExpectedShortfall(forty, 0.95), 12);

        // n = 30: k = 1.5 — the largest at weight 1 and the next at 0.5, over 1.5.
        var thirty = Enumerable.Range(1, 30).Select(i => (double)i).ToArray();
        Assert.Equal((30 + 0.5 * 29) / 1.5, TailStatisticsCalculator.ExpectedShortfall(thirty, 0.95), 12);

        // n·5 % that floating point would make 500.0000000000004 is 500: no weight leaks onto the 501st value.
        var tenThousand = Enumerable.Range(1, 10_000).Select(i => (double)i).ToArray();
        Assert.Equal(500, TailStatisticsCalculator.TailWeight(10_000, 0.95));
        Assert.Equal(Enumerable.Range(9_501, 500).Average(), TailStatisticsCalculator.ExpectedShortfall(tenThousand, 0.95), 9);

        // α = 0 is the mean.
        Assert.Equal(15.5, TailStatisticsCalculator.ExpectedShortfall(thirty, 0), 12);
    }

    /// <summary>TS4 — the order-statistic interval of the P95: the ranks of S48 §4.3, and it contains the estimate.</summary>
    [Fact]
    public void TestTS4_TheP95IntervalUsesTheOrderStatisticRanks()
    {
        Assert.Equal((9457, 9543), TailStatisticsCalculator.QuantileRankInterval(10_000, 0.95));
        Assert.Equal((1, 1), TailStatisticsCalculator.QuantileRankInterval(1, 0.95));

        var sample = Enumerable.Range(1, 10_000).Select(i => (double)i).ToArray();
        var stats = TailStatisticsCalculator.Compute(sample, 1);

        Assert.Equal(9457, stats.P95CiLow);
        Assert.Equal(9543, stats.P95CiHigh);
        Assert.InRange(stats.P95, stats.P95CiLow, stats.P95CiHigh);

        // Clamped into the sample on a small one.
        var (low, high) = TailStatisticsCalculator.QuantileRankInterval(10, 0.95);
        Assert.InRange(low, 1, 10);
        Assert.Equal(10, high);

        Assert.Throws<ArgumentOutOfRangeException>(() => TailStatisticsCalculator.QuantileRankInterval(0, 0.95));
        Assert.Throws<ArgumentOutOfRangeException>(() => TailStatisticsCalculator.QuantileRankInterval(10, 1));
    }

    /// <summary>TS5 — the bootstrap interval is reproducible from its seed, contains the estimate, and uses the seed.</summary>
    [Fact]
    public void TestTS5_TheBootstrapIntervalIsSeeded()
    {
        var sorted = (double[])MonteCarloRiskSimulator.Simulate(new CalibratedRange(0.5, 2, 5),
            [new CalibratedRange(10_000, 50_000, 500_000)], 10_000, 3).Losses.Clone();
        Array.Sort(sorted);

        var first = TailStatisticsCalculator.Compute(sorted, 42);
        var again = TailStatisticsCalculator.Compute(sorted, 42);
        var other = TailStatisticsCalculator.Compute(sorted, 43);

        Assert.Equal(first, again);
        Assert.InRange(first.Cvar95, first.Cvar95CiLow, first.Cvar95CiHigh);
        Assert.True(first.Cvar95CiHigh > first.Cvar95CiLow);
        Assert.NotEqual(first.Cvar95CiLow, other.Cvar95CiLow);

        // The interval is a few percent of the estimate at 10 000 iterations — not a degenerate point, not a guess.
        var relativeWidth = (first.Cvar95CiHigh - first.Cvar95CiLow) / first.Cvar95;
        Assert.InRange(relativeWidth, 0.01, 0.25);

        // The bootstrap never consumes the simulation's stream: the run's seed and the bootstrap's are different.
        Assert.NotEqual(7, TailStatisticsCalculator.BootstrapSeed(7));
    }

    /// <summary>TS6 — more iterations, narrower intervals: they measure Monte Carlo error.</summary>
    [Fact]
    public void TestTS6_TheIntervalsNarrowWithMoreIterations()
    {
        var frequency = new CalibratedRange(0.5, 2, 5);
        var magnitude = new CalibratedRange(10_000, 50_000, 500_000);

        var small = MonteCarloRiskSimulator.Run(frequency, magnitude, 1_000, 9).Tail;
        var large = MonteCarloRiskSimulator.Run(frequency, magnitude, 40_000, 9).Tail;

        Assert.True(large.P95CiHigh - large.P95CiLow < small.P95CiHigh - small.P95CiLow);
        Assert.True(large.Cvar95CiHigh - large.Cvar95CiLow < small.Cvar95CiHigh - small.Cvar95CiLow);
        Assert.True(large.ExpectedLossCiHigh - large.ExpectedLossCiLow < small.ExpectedLossCiHigh - small.ExpectedLossCiLow);
    }

    /// <summary>TS7 — the E[L] interval is mean ± z·s/√n, truncated at zero.</summary>
    [Fact]
    public void TestTS7_TheExpectedLossIntervalIsTheCentralLimitOne()
    {
        double[] sample = Sorted(0, 0, 0, 10, 20, 30, 40, 50, 60, 1000);
        var stats = TailStatisticsCalculator.Compute(sample, 1);

        var mean = sample.Average();
        var sd = System.Math.Sqrt(sample.Sum(x => (x - mean) * (x - mean)) / (sample.Length - 1));
        var half = TailStatisticsCalculator.Z * sd / System.Math.Sqrt(sample.Length);

        Assert.Equal(mean, stats.ExpectedLoss, 12);
        Assert.Equal(System.Math.Max(0, mean - half), stats.ExpectedLossCiLow, 9);
        Assert.Equal(mean + half, stats.ExpectedLossCiHigh, 9);
        Assert.Equal(0, stats.ExpectedLossCiLow); // truncated: a loss is never negative
        Assert.Equal(0.7, stats.ProbabilityOfLoss, 12);
        Assert.Equal((10 + 20 + 30 + 40 + 50 + 60 + 1000) / 7.0, stats.ConditionalLoss!.Value, 9);
    }

    /// <summary>TS8 — a sample with no loss at all: every statistic zero, and no loss-year mean.</summary>
    [Fact]
    public void TestTS8_AnAllZeroSample()
    {
        var stats = TailStatisticsCalculator.Compute(new double[1000], 1);

        Assert.Equal(0, stats.ExpectedLoss);
        Assert.Equal(0, stats.P95);
        Assert.Equal(0, stats.Cvar95);
        Assert.Equal(0, stats.Cvar95CiHigh);
        Assert.Equal(0, stats.ProbabilityOfLoss);
        Assert.Null(stats.ConditionalLoss);
    }

    /// <summary>TS9 — an empty, unsorted, negative or non-finite sample is refused, not summarised.</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("unsorted")]
    [InlineData("negative")]
    [InlineData("nan")]
    public void TestTS9_AnInvalidSampleIsRefused(string kind)
    {
        double[] sample = kind switch
        {
            "empty" => [],
            "unsorted" => [3, 1, 2],
            "negative" => [-1, 0, 1],
            _ => [0, double.NaN]
        };

        Assert.Throws<ArgumentException>(() => TailStatisticsCalculator.Compute(sample, 1));
    }
}
