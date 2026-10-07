using System;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// Stage 9.7 (S48 §4.4, §8 MC1–MC5) — what the simulator gained: the tail statistics of every run and the loss
/// magnitude decomposed into components, without changing a single number of a run that has no components.
/// </summary>
[TestSubject(typeof(MonteCarloRiskSimulator))]
public class MonteCarloTailAndComponentsTest
{
    private static readonly CalibratedRange Frequency = new(0.5, 2, 5);
    private static readonly CalibratedRange Magnitude = new(10_000, 50_000, 500_000);

    /// <summary>
    /// MC1 — a single-range run is unchanged, bit for bit: these are the values the Track 8 simulator produced before
    /// Stage 9.7 for the default seed (pinned from the pre-change build). A component loop that drew in another order,
    /// or a mean taken over the unsorted array, fails here.
    /// </summary>
    [Theory]
    [InlineData(0.5, 2.0, 5.0, 10_000.0, 50_000.0, 500_000.0, 0.0, 0.0, 222066.57215714225, 587780.8432557816, 268823.0280976009)]
    [InlineData(0.01, 0.03, 0.06, 1_000_000.0, 4_000_000.0, 10_000_000.0, 0.0, 0.0, 0.0, 0.0, 136877.99662533574)]
    [InlineData(0.5, 2.0, 5.0, 10_000.0, 50_000.0, 500_000.0, 0.4, 0.0, 133239.94329428533, 352668.5059534689, 161293.81685856028)]
    public void TestMC1_ASingleRangeRunIsUnchangedBitForBit(double fMin, double fMode, double fMax, double mMin,
        double mMode, double mMax, double effectiveness, double p10, double p50, double p90, double mean)
    {
        var result = MonteCarloRiskSimulator.Run(new CalibratedRange(fMin, fMode, fMax),
            new CalibratedRange(mMin, mMode, mMax), 10_000, 20260826, effectiveness);

        Assert.Equal(p10, result.P10);
        Assert.Equal(p50, result.P50);
        Assert.Equal(p90, result.P90);
        Assert.Equal(mean, result.Mean);
    }

    /// <summary>MC2 — with components, their contributions add up to the run's E[L] and to its CVaR95.</summary>
    [Fact]
    public void TestMC2_ComponentContributionsAddUpToTheMeanAndTheCvar()
    {
        var result = MonteCarloRiskSimulator.Run(Frequency,
            [new CalibratedRange(5_000, 10_000, 40_000), new CalibratedRange(0, 20_000, 300_000), new CalibratedRange(0, 0, 50_000)],
            10_000, 11);

        Assert.Equal(3, result.ComponentContributions.Count);
        Assert.Equal(result.Mean, result.ComponentContributions.Sum(c => c.ExpectedLoss), 1e-6 * result.Mean);
        Assert.Equal(result.Tail.Cvar95, result.ComponentContributions.Sum(c => c.Cvar95), 1e-6 * result.Tail.Cvar95);
        Assert.All(result.ComponentContributions, c => Assert.True(c.ExpectedLoss >= 0 && c.Cvar95 >= 0));
        Assert.Equal([0, 1, 2], result.ComponentContributions.Select(c => c.Index));
    }

    /// <summary>MC3 — the residual run is the inherent one scaled by the retained fraction, in the tail too.</summary>
    [Fact]
    public void TestMC3_TheResidualTailIsTheInherentTailScaled()
    {
        var inherent = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 10_000, 5);
        var residual = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 10_000, 5, mitigationEffectiveness: 0.25);

        Assert.Equal(inherent.Tail.P95 * 0.75, residual.Tail.P95, 1e-6 * inherent.Tail.P95);
        Assert.Equal(inherent.Tail.Cvar95 * 0.75, residual.Tail.Cvar95, 1e-6 * inherent.Tail.Cvar95);
        Assert.Equal(inherent.Tail.ProbabilityOfLoss, residual.Tail.ProbabilityOfLoss);
    }

    /// <summary>
    /// MC4 — what the decomposition is for: a component with a heavy right tail (a fine: usually nothing, sometimes a
    /// lot) carries a larger share of the CVaR95 than of the E[L]; a steady one the opposite.
    /// </summary>
    [Fact]
    public void TestMC4_TheHeavyTailedComponentDominatesTheCvarMoreThanTheMean()
    {
        var result = MonteCarloRiskSimulator.Run(Frequency,
            [new CalibratedRange(20_000, 20_000, 20_000), new CalibratedRange(0, 0, 1_000_000)], 10_000, 5);

        var steady = result.ComponentContributions[0];
        var fine = result.ComponentContributions[1];

        var fineShareOfMean = fine.ExpectedLoss / result.Mean;
        var fineShareOfCvar = fine.Cvar95 / result.Tail.Cvar95;

        Assert.True(fineShareOfCvar > fineShareOfMean + 0.02,
            $"fine: {fineShareOfMean:P1} of E[L] but {fineShareOfCvar:P1} of CVaR95");
        Assert.True(steady.Cvar95 / result.Tail.Cvar95 < steady.ExpectedLoss / result.Mean);
    }

    /// <summary>MC5 — one component is the single range: same sample, same statistics, one contribution equal to the whole.</summary>
    [Fact]
    public void TestMC5_OneComponentIsTheSingleRange()
    {
        var single = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 10_000, 8);
        var listed = MonteCarloRiskSimulator.Run(Frequency, [Magnitude], 10_000, 8);

        Assert.Equal(single.Mean, listed.Mean);
        Assert.Equal(single.Tail, listed.Tail);

        var only = Assert.Single(listed.ComponentContributions);
        Assert.Equal(listed.Mean, only.ExpectedLoss, 1e-9 * listed.Mean);
        Assert.Equal(listed.Tail.Cvar95, only.Cvar95, 1e-9 * listed.Tail.Cvar95);
    }

    /// <summary>The raw sample: one value per iteration, components adding up to the total, guards on the inputs.</summary>
    [Fact]
    public void TestTheSampleIsInIterationOrderAndItsComponentsAddUp()
    {
        var sample = MonteCarloRiskSimulator.Simulate(Frequency,
            [new CalibratedRange(1_000, 2_000, 3_000), new CalibratedRange(0, 500, 9_000)], 2_000, 4);

        Assert.Equal(2_000, sample.Losses.Length);
        for (var i = 0; i < sample.Losses.Length; i++)
            Assert.Equal(sample.Losses[i], sample.ComponentLosses[0][i] + sample.ComponentLosses[1][i], 1e-6);

        Assert.Throws<ArgumentException>(() => MonteCarloRiskSimulator.Simulate(Frequency, [], 1_000, 1));
        Assert.Throws<ArgumentException>(() =>
            MonteCarloRiskSimulator.Simulate(Frequency, [Magnitude, new CalibratedRange(3, 2, 1)], 1_000, 1));
    }
}
