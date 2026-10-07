using JetBrains.Annotations;
using Tools.Risks;
using Tools.TailRisk;
using Xunit;

namespace Tools.Tests.TailRisk;

/// <summary>
/// Stage 9.7 (S48 §4.8, D7, §8 TF1–TF6) — the flag 8 criterion: a low annual probability of a loss year and a
/// catastrophic mean loss of a loss year, on the inherent run.
/// </summary>
[TestSubject(typeof(TailFlag))]
public class TailFlagTest
{
    private static readonly TailFlagThresholds Defaults = new(0.10, 1_000_000);

    /// <summary>TF1 — rare and catastrophic: the criterion holds, with a reproducible basis text.</summary>
    [Fact]
    public void TestTF1_RareAndCatastrophicHolds()
    {
        var basis = TailFlag.Basis(0.032, 4_212_331.4, 10_000, 20260826, Defaults);

        Assert.Equal("inherent run (seed 20260826, 10000 iterations): loss in 3.2% of years <= 10.0%; " +
                     "mean loss of a loss year 4,212,331 >= 1,000,000", basis);
    }

    /// <summary>TF2 — frequent, however large: not "low probability".</summary>
    [Fact]
    public void TestTF2_AFrequentScenarioDoesNotHold() =>
        Assert.Null(TailFlag.Basis(0.4, 9_000_000, 10_000, 1, Defaults));

    /// <summary>TF3 — rare but small: not "catastrophic".</summary>
    [Fact]
    public void TestTF3_ARareSmallScenarioDoesNotHold() =>
        Assert.Null(TailFlag.Basis(0.02, 50_000, 10_000, 1, Defaults));

    /// <summary>TF4 — no loss year at all in the simulation: nothing to call catastrophic.</summary>
    [Fact]
    public void TestTF4_NoLossYearDoesNotHold()
    {
        Assert.Null(TailFlag.Basis(0, null, 10_000, 1, Defaults));
        Assert.Null(TailFlag.Basis(0.01, null, 10_000, 1, Defaults));
    }

    /// <summary>TF5 — both thresholds are inclusive (≤ probability, ≥ loss).</summary>
    [Fact]
    public void TestTF5_TheThresholdsAreInclusive()
    {
        Assert.NotNull(TailFlag.Basis(0.10, 1_000_000, 10_000, 1, Defaults));
        Assert.Null(TailFlag.Basis(0.1001, 1_000_000, 10_000, 1, Defaults));
        Assert.Null(TailFlag.Basis(0.10, 999_999.99, 10_000, 1, Defaults));
    }

    /// <summary>
    /// TF6 — why the loss-year mean and not the CVaR95: a once-in-a-thousand-years catastrophe has a CVaR95 diluted by
    /// loss-free years far below the catastrophic threshold's scale relative to its real impact, and still holds.
    /// </summary>
    [Fact]
    public void TestTF6_AOnceInAThousandYearsCatastropheHoldsThoughItsCvar95IsDiluted()
    {
        var run = MonteCarloRiskSimulator.Run(new CalibratedRange(0.0001, 0.001, 0.002),
            new CalibratedRange(50_000_000, 100_000_000, 200_000_000), 10_000, 3);
        var tail = run.Tail;

        Assert.Equal(0, run.P95);
        Assert.True(tail.ConditionalLoss > 20 * tail.Cvar95,
            $"loss-year mean {tail.ConditionalLoss:N0} vs CVaR95 {tail.Cvar95:N0}");

        var thresholds = new TailFlagThresholds(0.10, 10_000_000);
        Assert.True(tail.Cvar95 < thresholds.CatastrophicLoss); // a CVaR95 criterion would miss it
        Assert.NotNull(TailFlag.Basis(tail.ProbabilityOfLoss, tail.ConditionalLoss, tail.Iterations, 3, thresholds));
    }

    /// <summary>The thresholds from settings: parsed invariantly, each defaulting when missing, malformed or out of range.</summary>
    [Theory]
    [InlineData(null, null, 0.10, 1_000_000.0)]
    [InlineData("0.05", "2500000", 0.05, 2_500_000.0)]
    [InlineData("0,05", "abc", 0.10, 1_000_000.0)]
    [InlineData("0", "-5", 0.10, 1_000_000.0)]
    [InlineData("0.6", "0", 0.10, 1_000_000.0)]
    [InlineData("0.5", "1e7", 0.5, 10_000_000.0)]
    public void TestTheThresholdsResolveFromSettings(string? probability, string? loss, double expectedProbability,
        double expectedLoss)
    {
        var thresholds = TailFlagThresholds.Resolve(probability, loss, 1_000_000);

        Assert.Equal(expectedProbability, thresholds.MaxAnnualProbability);
        Assert.Equal(expectedLoss, thresholds.CatastrophicLoss);
    }
}
