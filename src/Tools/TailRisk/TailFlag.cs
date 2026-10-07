using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tools.TailRisk;

/// <summary>The two thresholds of the flag 8 derivation (S48 §4.8).</summary>
/// <param name="MaxAnnualProbability">"Low probability": the annual probability of a loss year at or below this.</param>
/// <param name="CatastrophicLoss">"Catastrophic": the mean loss of a loss year at or above this.</param>
public sealed record TailFlagThresholds(double MaxAnnualProbability, double CatastrophicLoss)
{
    /// <summary>Less than once in ten years.</summary>
    public const double DefaultMaxAnnualProbability = 0.10;

    /// <summary>The largest annual probability a "low probability" setting may name.</summary>
    public const double UpperBoundMaxAnnualProbability = 0.5;

    /// <summary>
    /// The thresholds from the two <c>settings</c> values, each falling back to its default when missing,
    /// malformed or out of range — the rule <c>QuantitativeRiskService</c> applies to the band thresholds. The default
    /// catastrophic loss is the top band threshold, the start of "Very High" on the scale that maps E[L] to the score.
    /// </summary>
    public static TailFlagThresholds Resolve(string? maxAnnualProbability, string? catastrophicLoss,
        double topBandThreshold)
    {
        var probability = double.TryParse(maxAnnualProbability, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var p) && p > 0 && p <= UpperBoundMaxAnnualProbability
            ? p
            : DefaultMaxAnnualProbability;

        var loss = double.TryParse(catastrophicLoss, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) &&
                   double.IsFinite(l) && l > 0
            ? l
            : topBandThreshold;

        return new TailFlagThresholds(probability, loss);
    }
}

/// <summary>
/// Stage 9.7 (S48 §4.8, D7) — flag 8, "low probability, catastrophic impact", derived from the inherent run's tail:
/// a loss in at most <see cref="TailFlagThresholds.MaxAnnualProbability"/> of years, and a mean loss of a loss year of
/// at least <see cref="TailFlagThresholds.CatastrophicLoss"/>.
///
/// The mean loss of a loss year — E[L | L &gt; 0], the CVaR at the level where the tail is exactly the loss years — and
/// not the CVaR95: for a scenario rarer than once in twenty years the 5 % tail is diluted by loss-free years, and a
/// once-in-a-thousand-years catastrophe would read fifty times smaller than it is.
/// </summary>
public static class TailFlag
{
    /// <summary>The basis text when the criterion holds, or null when it does not.</summary>
    public static string? Basis(double probabilityOfLoss, double? conditionalLoss, int iterations, int seed,
        TailFlagThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (probabilityOfLoss <= 0 || conditionalLoss is not { } severity) return null;
        if (probabilityOfLoss > thresholds.MaxAnnualProbability) return null;
        if (severity < thresholds.CatastrophicLoss) return null;

        var invariant = CultureInfo.InvariantCulture;
        return $"inherent run (seed {seed.ToString(invariant)}, {iterations.ToString(invariant)} iterations): " +
               $"loss in {probabilityOfLoss.ToString("0.0%", invariant)} of years <= " +
               $"{thresholds.MaxAnnualProbability.ToString("0.0%", invariant)}; mean loss of a loss year " +
               $"{severity.ToString("N0", invariant)} >= {thresholds.CatastrophicLoss.ToString("N0", invariant)}";
    }
}
