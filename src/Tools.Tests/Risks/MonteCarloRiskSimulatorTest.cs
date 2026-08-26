using System;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// Track 8 milestone 8.7.2 — the FAIR-lite Monte Carlo engine.
///
/// Pure and seeded, so every property below is a hard assertion rather than a statistical hope. The
/// two that matter most for the milestone's credibility are reproducibility (a risk number an auditor
/// cannot recompute is one they have to take on trust) and monotonicity in the inputs (a worse input
/// must never produce a better answer).
/// </summary>
[TestSubject(typeof(MonteCarloRiskSimulator))]
public class MonteCarloRiskSimulatorTest
{
    private static readonly CalibratedRange Frequency = new(0.5, 2, 5);
    private static readonly CalibratedRange Magnitude = new(10_000, 50_000, 500_000);

    [Fact]
    public void TestPercentilesAreOrdered()
    {
        var result = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 5000, seed: 1);

        Assert.True(result.P10 <= result.P50);
        Assert.True(result.P50 <= result.P90);
        Assert.True(result.Mean > 0);
    }

    [Fact]
    public void TestTheSameSeedProducesTheSameAnswer()
    {
        var first = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 4000, seed: 99);
        var second = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 4000, seed: 99);

        Assert.Equal(first.P10, second.P10, 10);
        Assert.Equal(first.P50, second.P50, 10);
        Assert.Equal(first.P90, second.P90, 10);
        Assert.Equal(first.Mean, second.Mean, 10);
    }

    [Fact]
    public void TestADifferentSeedProducesADifferentSample()
    {
        var first = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 4000, seed: 1);
        var second = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 4000, seed: 2);

        // Not a distributional claim — just that the seed is actually used.
        Assert.NotEqual(first.P50, second.P50);
    }

    [Fact]
    public void TestMitigationEffectivenessReducesTheExposureProportionally()
    {
        var untreated = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 6000, seed: 5);
        var halved = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 6000, seed: 5,
            mitigationEffectiveness: 0.5);

        // Same seed, same draws, magnitude scaled by the retained fraction — so this is exact rather
        // than approximate, which is what makes a before/after comparison a control-ROI statement.
        Assert.Equal(untreated.Mean * 0.5, halved.Mean, 6);
        Assert.Equal(untreated.P90 * 0.5, halved.P90, 6);
    }

    [Fact]
    public void TestFullMitigationRemovesTheExposureEntirely()
    {
        var result = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 2000, seed: 5,
            mitigationEffectiveness: 1.0);

        Assert.Equal(0, result.Mean, 6);
        Assert.Equal(0, result.P90, 6);
    }

    [Fact]
    public void TestAHigherFrequencyProducesAHigherExpectedLoss()
    {
        var rare = MonteCarloRiskSimulator.Run(new CalibratedRange(0.05, 0.1, 0.2), Magnitude, 8000, 7);
        var common = MonteCarloRiskSimulator.Run(new CalibratedRange(2, 4, 8), Magnitude, 8000, 7);

        Assert.True(common.Mean > rare.Mean);
    }

    [Fact]
    public void TestAHigherMagnitudeProducesAHigherExpectedLoss()
    {
        var small = MonteCarloRiskSimulator.Run(Frequency, new CalibratedRange(1_000, 2_000, 5_000), 8000, 7);
        var large = MonteCarloRiskSimulator.Run(Frequency,
            new CalibratedRange(1_000_000, 2_000_000, 5_000_000), 8000, 7);

        Assert.True(large.Mean > small.Mean);
    }

    /// <summary>
    /// A degenerate range — a point estimate expressed as a range — has to work. It is what an
    /// estimator writes when they genuinely know the number, and the Beta distribution underneath is
    /// undefined there.
    /// </summary>
    [Fact]
    public void TestADegenerateRangeIsAPointEstimate()
    {
        var result = MonteCarloRiskSimulator.Run(new CalibratedRange(1, 1, 1),
            new CalibratedRange(100_000, 100_000, 100_000), 3000, seed: 3);

        // Exactly one event a year at exactly 100k, so the mean is very close to 100k. The Poisson
        // still varies the event count, so this is a range rather than an equality.
        Assert.InRange(result.Mean, 80_000, 130_000);
    }

    /// <summary>A mode at an endpoint gives the Beta a shape parameter of exactly 1, which is the
    /// boundary the gamma sampler has to handle.</summary>
    [Theory]
    [InlineData(0.0, 0.0, 3.0)]
    [InlineData(0.0, 3.0, 3.0)]
    public void TestAModeAtAnEndpointDoesNotBreakTheSampler(double min, double mode, double max)
    {
        var result = MonteCarloRiskSimulator.Run(new CalibratedRange(min, mode, max), Magnitude, 2000, 4);

        Assert.True(result.Mean >= 0);
        Assert.All(result.LossExceedanceCurve, point => Assert.True(point.Loss >= 0));
    }

    [Fact]
    public void TestTheLossExceedanceCurveIsAscendingByLossAndDescendingByProbability()
    {
        var curve = MonteCarloRiskSimulator.Run(Frequency, Magnitude, 6000, seed: 8)
            .LossExceedanceCurve;

        Assert.NotEmpty(curve);

        for (var i = 1; i < curve.Count; i++)
        {
            Assert.True(curve[i].Loss >= curve[i - 1].Loss);
            Assert.True(curve[i].Probability <= curve[i - 1].Probability);
        }

        Assert.All(curve, point => Assert.InRange(point.Probability, 0, 1));
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(1, 4, 3)]
    public void TestAnUnorderedFrequencyRangeIsRejected(double min, double mode, double max)
    {
        Assert.Throws<ArgumentException>(() =>
            MonteCarloRiskSimulator.Run(new CalibratedRange(min, mode, max), Magnitude, 1000, 1));
    }

    [Fact]
    public void TestAnUnorderedMagnitudeRangeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            MonteCarloRiskSimulator.Run(Frequency, new CalibratedRange(5, 1, 2), 1000, 1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void TestMitigationEffectivenessOutsideZeroToOneIsRejected(double effectiveness)
    {
        Assert.Throws<ArgumentException>(() =>
            MonteCarloRiskSimulator.Run(Frequency, Magnitude, 1000, 1, effectiveness));
    }

    /// <summary>A P90 estimated from a handful of draws is noise, so the iteration count is floored.</summary>
    [Fact]
    public void TestTheIterationCountIsFloored()
    {
        var result = MonteCarloRiskSimulator.Run(Frequency, Magnitude, iterations: 5, seed: 1);

        Assert.Equal(1000, result.Iterations);
    }

    [Fact]
    public void TestValidityOfARange()
    {
        Assert.True(new CalibratedRange(1, 2, 3).IsValid);
        Assert.True(new CalibratedRange(0, 0, 0).IsValid);
        Assert.False(new CalibratedRange(-1, 0, 1).IsValid);
        Assert.False(new CalibratedRange(1, 0, 2).IsValid);
        Assert.False(new CalibratedRange(1, 2, 0).IsValid);
    }

    [Fact]
    public void TestPercentileInterpolatesBetweenSamples()
    {
        double[] sorted = [0, 10, 20, 30, 40];

        Assert.Equal(0, MonteCarloRiskSimulator.Percentile(sorted, 0), 6);
        Assert.Equal(40, MonteCarloRiskSimulator.Percentile(sorted, 1), 6);
        Assert.Equal(20, MonteCarloRiskSimulator.Percentile(sorted, 0.5), 6);
        Assert.Equal(15, MonteCarloRiskSimulator.Percentile(sorted, 0.375), 6);
        Assert.Equal(0, MonteCarloRiskSimulator.Percentile([], 0.5), 6);
    }

    [Fact]
    public void TestPoissonReturnsZeroForANonPositiveRate()
    {
        var random = new Random(1);

        Assert.Equal(0, MonteCarloRiskSimulator.SamplePoisson(random, 0));
        Assert.Equal(0, MonteCarloRiskSimulator.SamplePoisson(random, -3));
    }

    /// <summary>
    /// The Poisson mean has to come out near λ, including above the threshold where the sampler
    /// switches to a normal approximation — the switch is an optimisation, not a change of model.
    /// </summary>
    [Theory]
    [InlineData(2.0)]
    [InlineData(25.0)]
    [InlineData(50.0)]
    public void TestPoissonMeanApproximatesTheRate(double lambda)
    {
        var random = new Random(12345);

        var total = 0L;
        const int draws = 20_000;
        for (var i = 0; i < draws; i++) total += MonteCarloRiskSimulator.SamplePoisson(random, lambda);

        var mean = (double)total / draws;

        Assert.InRange(mean, lambda * 0.9, lambda * 1.1);
    }

    [Fact]
    public void TestPertSamplesStayInsideTheRange()
    {
        var random = new Random(7);
        var range = new CalibratedRange(10, 40, 100);

        for (var i = 0; i < 5000; i++)
        {
            var sample = MonteCarloRiskSimulator.SamplePert(random, range);
            Assert.InRange(sample, range.Min, range.Max);
        }
    }

    /// <summary>The PERT's mean sits near (min + 4·mode + max)/6, which is what makes it a PERT.</summary>
    [Fact]
    public void TestPertMeanIsNearTheWeightedThreePointEstimate()
    {
        var random = new Random(31);
        var range = new CalibratedRange(10, 40, 100);

        var total = 0.0;
        const int draws = 40_000;
        for (var i = 0; i < draws; i++) total += MonteCarloRiskSimulator.SamplePert(random, range);

        var expected = (range.Min + 4 * range.MostLikely + range.Max) / 6.0;

        Assert.InRange(total / draws, expected * 0.95, expected * 1.05);
    }
}
