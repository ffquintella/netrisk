using System;
using System.Linq;
using Tools.Risks;

namespace Tools.TailRisk;

/// <summary>
/// The tail statistics of one sample of annual losses (Stage 9.7, S48 §4.3), with their 95 % confidence intervals.
/// The intervals measure Monte Carlo error — how precisely the iterations pin the statistic given the calibrated
/// ranges — and not the uncertainty of the ranges themselves (S48 D2).
/// </summary>
public sealed record TailStatistics
{
    public int Iterations { get; init; }

    public double ExpectedLoss { get; init; }

    public double ExpectedLossCiLow { get; init; }

    public double ExpectedLossCiHigh { get; init; }

    public double P95 { get; init; }

    public double P95CiLow { get; init; }

    public double P95CiHigh { get; init; }

    /// <summary>Expected shortfall at 95 %: the mean of the tail beyond the 95th percentile.</summary>
    public double Cvar95 { get; init; }

    public double Cvar95CiLow { get; init; }

    public double Cvar95CiHigh { get; init; }

    /// <summary>The fraction of years with any loss.</summary>
    public double ProbabilityOfLoss { get; init; }

    /// <summary>E[L | L &gt; 0]; null when no year had a loss.</summary>
    public double? ConditionalLoss { get; init; }
}

/// <summary>
/// The estimators of S48 §4.3, pure and deterministic:
/// <list type="bullet">
/// <item><b>E[L]</b> — the mean, with a central-limit interval;</item>
/// <item><b>P95</b> — <see cref="MonteCarloRiskSimulator.Percentile"/>, the estimator P10/P50/P90 already use, with a
/// distribution-free order-statistic interval (normal approximation to the binomial, Conover §3.2) — no random
/// numbers;</item>
/// <item><b>CVaR95</b> — the Acerbi–Tasche (2002) expected-shortfall estimator, the mean of the tail beyond the
/// percentile with a fractional weight when n·5 % is not whole, with a percentile-bootstrap interval of
/// <see cref="BootstrapReplicates"/> replicates seeded from the run's seed.</item>
/// </list>
/// The CVaR of a scenario that loses in 3 % of years is not zero even though its P95 is: the worst 5 % of years
/// contain the 3 % with a loss. That is the T187 edge case, and the reason this is not the 0.05 point of the curve.
/// </summary>
public static class TailStatisticsCalculator
{
    /// <summary>α of P95 and CVaR95.</summary>
    public const double TailLevel = 0.95;

    /// <summary>The level of every confidence interval.</summary>
    public const double ConfidenceLevel = 0.95;

    /// <summary>The two-sided 95 % standard-normal quantile.</summary>
    public const double Z = 1.959963984540054;

    public const int BootstrapReplicates = 1000;

    /// <summary>XORed into the run's seed so the bootstrap never consumes — or depends on — the simulation's stream.</summary>
    public const int BootstrapSeedSalt = 0x5EED7A11;

    /// <summary>The bootstrap seed derived from a run's seed (S48 §4.3).</summary>
    public static int BootstrapSeed(int seed) => seed ^ BootstrapSeedSalt;

    /// <summary>
    /// Every statistic of an ascending sample of non-negative annual losses.
    /// </summary>
    /// <param name="sortedLosses">Ascending, finite, non-negative; at least one value.</param>
    /// <param name="bootstrapSeed">The seed of the CVaR interval's bootstrap.</param>
    public static TailStatistics Compute(double[] sortedLosses, int bootstrapSeed)
    {
        Validate(sortedLosses);

        var n = sortedLosses.Length;
        var mean = sortedLosses.Average();

        var sumSquares = 0.0;
        foreach (var x in sortedLosses) sumSquares += (x - mean) * (x - mean);
        var sd = n > 1 ? System.Math.Sqrt(sumSquares / (n - 1)) : 0;
        var halfWidth = Z * sd / System.Math.Sqrt(n);

        var p95 = MonteCarloRiskSimulator.Percentile(sortedLosses, TailLevel);
        var (low, high) = QuantileRankInterval(n, TailLevel);

        var cvar = ExpectedShortfall(sortedLosses, TailLevel);
        var (cvarLow, cvarHigh) = BootstrapShortfallInterval(sortedLosses, TailLevel, bootstrapSeed);

        var firstLoss = FirstPositiveIndex(sortedLosses);
        var lossYears = n - firstLoss;
        double? conditional = null;
        if (lossYears > 0)
        {
            var sum = 0.0;
            for (var i = firstLoss; i < n; i++) sum += sortedLosses[i];
            conditional = sum / lossYears;
        }

        return new TailStatistics
        {
            Iterations = n,
            ExpectedLoss = mean,
            ExpectedLossCiLow = System.Math.Max(0, mean - halfWidth),
            ExpectedLossCiHigh = mean + halfWidth,
            P95 = p95,
            P95CiLow = sortedLosses[low - 1],
            P95CiHigh = sortedLosses[high - 1],
            Cvar95 = cvar,
            Cvar95CiLow = cvarLow,
            Cvar95CiHigh = cvarHigh,
            ProbabilityOfLoss = (double)lossYears / n,
            ConditionalLoss = conditional
        };
    }

    /// <summary>
    /// The 1-based ranks of the order statistics bounding the <paramref name="q"/>-quantile at
    /// <see cref="ConfidenceLevel"/>: ⌊nq − z√(nq(1−q))⌋ and ⌈nq + z√(nq(1−q))⌉, clamped to [1, n].
    /// </summary>
    public static (int Low, int High) QuantileRankInterval(int n, double q)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "A sample has at least one value.");
        if (q is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(q), "A quantile level is strictly between 0 and 1.");

        var centre = n * q;
        var half = Z * System.Math.Sqrt(n * q * (1 - q));

        var low = (int)System.Math.Floor(centre - half);
        var high = (int)System.Math.Ceiling(centre + half);

        return (System.Math.Clamp(low, 1, n), System.Math.Clamp(high, 1, n));
    }

    /// <summary>
    /// The Acerbi–Tasche expected shortfall of an ascending sample: with k = n(1 − α) and m = ⌊k⌋, the m largest values
    /// plus (k − m) times the next one, over k. The expected shortfall of the empirical distribution itself, so it is
    /// coherent — subadditive — over the same iterations.
    /// </summary>
    public static double ExpectedShortfall(double[] sortedLosses, double alpha)
    {
        ArgumentNullException.ThrowIfNull(sortedLosses);
        if (sortedLosses.Length == 0) throw new ArgumentException("The sample is empty.", nameof(sortedLosses));
        if (alpha is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(alpha), "α is in [0, 1).");

        var n = sortedLosses.Length;
        var k = TailWeight(n, alpha);
        var m = (int)System.Math.Floor(k);

        var sum = 0.0;
        for (var i = n - m; i < n; i++) sum += sortedLosses[i];

        var fraction = k - m;
        if (fraction > 0 && n - m - 1 >= 0) sum += fraction * sortedLosses[n - m - 1];

        return sum / k;
    }

    /// <summary>
    /// k = n(1 − α), rounded to nine decimals so 10 000 × (1 − 0.95) is 500 and not 500.0000000000004 — otherwise a
    /// 10⁻¹³ weight would land on the 501st value. At least the weight of one value.
    /// </summary>
    public static double TailWeight(int n, double alpha) => System.Math.Max(System.Math.Min(1.0, n), System.Math.Round(n * (1 - alpha), 9));

    /// <summary>
    /// The percentile-bootstrap interval of the expected shortfall: each replicate resamples n values with
    /// replacement as multinomial counts over the sorted sample — O(n), no re-sorting — and takes its shortfall from
    /// the top.
    /// </summary>
    private static (double Low, double High) BootstrapShortfallInterval(double[] sorted, double alpha, int seed)
    {
        var n = sorted.Length;
        var k = TailWeight(n, alpha);
        var random = new Random(seed);
        var counts = new int[n];
        var replicates = new double[BootstrapReplicates];

        for (var r = 0; r < BootstrapReplicates; r++)
        {
            Array.Clear(counts);
            for (var i = 0; i < n; i++) counts[random.Next(n)]++;

            var remaining = k;
            var sum = 0.0;
            for (var j = n - 1; j >= 0 && remaining > 0; j--)
            {
                if (counts[j] == 0) continue;
                var take = System.Math.Min(counts[j], remaining);
                sum += take * sorted[j];
                remaining -= take;
            }

            replicates[r] = sum / k;
        }

        Array.Sort(replicates);
        var tailProbability = (1 - ConfidenceLevel) / 2;
        return (MonteCarloRiskSimulator.Percentile(replicates, tailProbability),
            MonteCarloRiskSimulator.Percentile(replicates, 1 - tailProbability));
    }

    private static int FirstPositiveIndex(double[] sorted)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] > 0) hi = mid;
            else lo = mid + 1;
        }

        return lo;
    }

    private static void Validate(double[] sorted)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Length == 0) throw new ArgumentException("The sample is empty.", nameof(sorted));

        for (var i = 0; i < sorted.Length; i++)
        {
            if (!double.IsFinite(sorted[i]) || sorted[i] < 0)
                throw new ArgumentException("Annual losses are finite and non-negative.", nameof(sorted));
            if (i > 0 && sorted[i] < sorted[i - 1])
                throw new ArgumentException("The sample is not sorted ascending.", nameof(sorted));
        }
    }
}
