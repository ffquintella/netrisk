using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools.Risks;

/// <summary>
/// A calibrated-range input for one FAIR factor: the minimum, the most likely value and the
/// maximum, as an estimator would state them (Track 8 milestone 8.7.2, Open Group O-RT).
///
/// Ranges rather than point estimates is the whole reason to reach for FAIR: a single number
/// conceals the uncertainty the decision actually turns on, and — per Cox 2008 and Krisper 2021 —
/// an ordinal matrix cell conceals it further by discarding the magnitude as well.
/// </summary>
public readonly record struct CalibratedRange(double Min, double MostLikely, double Max)
{
    /// <summary>Whether the three values form a usable distribution.</summary>
    public bool IsValid => Min >= 0 && Max >= Min && MostLikely >= Min && MostLikely <= Max;
}

/// <summary>One point of a loss-exceedance curve: the probability of losing at least <see cref="Loss"/> in a year.</summary>
public readonly record struct LossExceedancePoint(double Loss, double Probability);

/// <summary>The outcome of one simulation run.</summary>
public sealed class LossExposureResult
{
    /// <summary>Annualized loss exposure at the 10th percentile — the optimistic year.</summary>
    public double P10 { get; init; }

    /// <summary>The median year. Usually well below the mean, which is the point.</summary>
    public double P50 { get; init; }

    /// <summary>The 90th percentile — the bad year a reserve has to survive.</summary>
    public double P90 { get; init; }

    /// <summary>The expected annual loss. Reported alongside the median because the two differ a
    /// lot on a skewed distribution, and quoting only one of them is how a number misleads.</summary>
    public double Mean { get; init; }

    /// <summary>The loss-exceedance curve, ascending by loss.</summary>
    public IReadOnlyList<LossExceedancePoint> LossExceedanceCurve { get; init; } = [];

    /// <summary>The seed the run used, so the exact result can be reproduced.</summary>
    public int Seed { get; init; }

    public int Iterations { get; init; }
}

/// <summary>
/// A FAIR-lite Monte Carlo engine (Track 8 milestone 8.7.2).
///
/// Each iteration samples a loss-event frequency and a per-event loss magnitude from PERT
/// distributions over the estimator's calibrated ranges, draws the year's event count from a Poisson
/// with that frequency, and sums the per-event losses. Across ten thousand iterations that produces
/// an annualized-loss distribution, its percentiles and a loss-exceedance curve.
///
/// Two deliberate choices. The PERT (a re-parameterised Beta) rather than a triangular distribution:
/// the triangular puts far too much weight in the tails for a three-point estimate, which is the
/// standard criticism of it in the estimation literature. And the run is <em>seeded</em>: a risk
/// number an auditor cannot reproduce is a number they have to take on trust, and this one they can
/// recompute exactly.
///
/// Explicitly out of scope, per the milestone: threat-capability and control-strength sub-factors —
/// this is FAIR-lite, not FAIR.
/// </summary>
public static class MonteCarloRiskSimulator
{
    public const int DefaultIterations = 10_000;

    /// <summary>The exceedance probabilities the stored curve is sampled at.</summary>
    private static readonly double[] CurveProbabilities =
        [0.99, 0.95, 0.90, 0.80, 0.70, 0.60, 0.50, 0.40, 0.30, 0.20, 0.10, 0.05, 0.02, 0.01];

    /// <summary>
    /// Runs the simulation.
    /// </summary>
    /// <param name="frequency">Loss events per year.</param>
    /// <param name="magnitude">Loss per event, in currency.</param>
    /// <param name="iterations">Iterations; clamped to at least 1000, since fewer makes the P90 noise.</param>
    /// <param name="seed">Seed for reproducibility.</param>
    /// <param name="mitigationEffectiveness">
    /// 0–1. Applied to the loss magnitude, which is how a control that reduces impact rather than
    /// likelihood is modelled; the before/after comparison the milestone asks for is two runs of this
    /// method differing only in this argument.
    /// </param>
    public static LossExposureResult Run(CalibratedRange frequency, CalibratedRange magnitude,
        int iterations = DefaultIterations, int seed = 20260826, double mitigationEffectiveness = 0)
    {
        if (!frequency.IsValid)
            throw new ArgumentException("The loss-event frequency range is not ordered min ≤ most likely ≤ max, " +
                                        "or contains a negative value.", nameof(frequency));
        if (!magnitude.IsValid)
            throw new ArgumentException("The loss-magnitude range is not ordered min ≤ most likely ≤ max, " +
                                        "or contains a negative value.", nameof(magnitude));
        if (mitigationEffectiveness is < 0 or > 1)
            throw new ArgumentException("Mitigation effectiveness is a fraction between 0 and 1.",
                nameof(mitigationEffectiveness));

        if (iterations < 1000) iterations = 1000;

        var random = new Random(seed);
        var retained = 1.0 - mitigationEffectiveness;
        var losses = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var lambda = SamplePert(random, frequency);
            var events = SamplePoisson(random, lambda);

            var total = 0.0;
            for (var e = 0; e < events; e++) total += SamplePert(random, magnitude) * retained;

            losses[i] = total;
        }

        Array.Sort(losses);

        return new LossExposureResult
        {
            P10 = Percentile(losses, 0.10),
            P50 = Percentile(losses, 0.50),
            P90 = Percentile(losses, 0.90),
            Mean = losses.Average(),
            LossExceedanceCurve = BuildCurve(losses),
            Seed = seed,
            Iterations = iterations
        };
    }

    /// <summary>
    /// The exceedance curve: for each probability p, the loss that is exceeded with probability p.
    /// Read left to right it answers "how likely is a year at least this bad", which is the question
    /// a board asks and the one a matrix cell cannot answer at all.
    /// </summary>
    private static List<LossExceedancePoint> BuildCurve(double[] sortedLosses) =>
        CurveProbabilities
            .Select(p => new LossExceedancePoint(Percentile(sortedLosses, 1 - p), p))
            .OrderBy(pt => pt.Loss)
            .ToList();

    /// <summary>Linear-interpolated percentile of an already-sorted sample.</summary>
    public static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        if (q <= 0) return sorted[0];
        if (q >= 1) return sorted[^1];

        var position = q * (sorted.Length - 1);
        var lower = (int)System.Math.Floor(position);
        var upper = (int)System.Math.Ceiling(position);
        if (lower == upper) return sorted[lower];

        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    /// <summary>
    /// One draw from a PERT distribution over [min, max] with the given mode.
    ///
    /// PERT is Beta(α, β) rescaled onto the range, with α and β derived from the mode using the
    /// conventional shape factor of 4 — the same parameterisation project-estimation tooling uses.
    /// A degenerate range (min == max) short-circuits: the Beta is undefined there and the answer
    /// is obviously the single value.
    /// </summary>
    public static double SamplePert(Random random, CalibratedRange range)
    {
        var (min, mode, max) = (range.Min, range.MostLikely, range.Max);
        if (max - min <= double.Epsilon) return min;

        const double lambda = 4.0;
        var alpha = 1 + lambda * (mode - min) / (max - min);
        var beta = 1 + lambda * (max - mode) / (max - min);

        return min + SampleBeta(random, alpha, beta) * (max - min);
    }

    /// <summary>
    /// Beta(α, β) as the ratio of two Gamma draws — the standard construction, and the one that
    /// stays correct for the small shape parameters a mode near an endpoint produces.
    /// </summary>
    private static double SampleBeta(Random random, double alpha, double beta)
    {
        var x = SampleGamma(random, alpha);
        var y = SampleGamma(random, beta);
        var sum = x + y;
        return sum <= 0 ? 0.5 : x / sum;
    }

    /// <summary>
    /// Marsaglia–Tsang gamma sampler, with the Johnk boost for shape &lt; 1 (the method is only
    /// valid for shape ≥ 1, and a PERT mode at an endpoint produces exactly shape = 1, so the guard
    /// is not theoretical).
    /// </summary>
    private static double SampleGamma(Random random, double shape)
    {
        if (shape < 1)
        {
            var u = NextNonZeroDouble(random);
            return SampleGamma(random, shape + 1) * System.Math.Pow(u, 1.0 / shape);
        }

        var d = shape - 1.0 / 3.0;
        var c = 1.0 / System.Math.Sqrt(9 * d);

        while (true)
        {
            double x, v;
            do
            {
                x = SampleStandardNormal(random);
                v = 1 + c * x;
            } while (v <= 0);

            v = v * v * v;
            var u2 = NextNonZeroDouble(random);

            if (u2 < 1 - 0.0331 * x * x * x * x) return d * v;
            if (System.Math.Log(u2) < 0.5 * x * x + d * (1 - v + System.Math.Log(v))) return d * v;
        }
    }

    /// <summary>Box–Muller. One of the pair is discarded, which costs nothing at this scale.</summary>
    private static double SampleStandardNormal(Random random)
    {
        var u1 = NextNonZeroDouble(random);
        var u2 = random.NextDouble();
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
    }

    /// <summary>
    /// Knuth's Poisson sampler for small λ, switching to a normal approximation above 30 where the
    /// product underflows and the approximation is indistinguishable anyway.
    /// </summary>
    public static int SamplePoisson(Random random, double lambda)
    {
        if (lambda <= 0) return 0;

        if (lambda > 30)
        {
            var approx = (int)System.Math.Round(lambda + System.Math.Sqrt(lambda) * SampleStandardNormal(random));
            return approx < 0 ? 0 : approx;
        }

        var l = System.Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;

        do
        {
            k++;
            p *= random.NextDouble();
        } while (p > l);

        return k - 1;
    }

    private static double NextNonZeroDouble(Random random)
    {
        double u;
        do { u = random.NextDouble(); } while (u <= 0);
        return u;
    }
}
