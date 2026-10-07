using System;
using System.Collections.Generic;

namespace Tools.TailRisk;

/// <summary>
/// Euler allocation of the expected shortfall (S48 §4.4, §4.6): the share of a total's CVaR that each part — a loss
/// component of one risk, or a risk of a portfolio — carries is the part's weighted mean over the iterations in the
/// tail of the total. The tail is the m = ⌊n(1 − α)⌋ largest totals at weight 1 plus the next one at weight
/// n(1 − α) − m, exactly the values <see cref="TailStatisticsCalculator.ExpectedShortfall"/> averages, so the shares add
/// up to the total's CVaR. Ties are broken by iteration index, so the allocation is deterministic.
/// </summary>
public static class TailContributions
{
    /// <summary>The iterations in the tail of <paramref name="totals"/>, with their weights, and the total weight k.</summary>
    public static (IReadOnlyList<(int Iteration, double Weight)> Tail, double K) TailWeights(double[] totals, double alpha)
    {
        ArgumentNullException.ThrowIfNull(totals);
        if (totals.Length == 0) throw new ArgumentException("The sample is empty.", nameof(totals));

        var n = totals.Length;
        var k = TailStatisticsCalculator.TailWeight(n, alpha);
        var m = (int)System.Math.Floor(k);

        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;

        // Largest total first; equal totals by iteration index, so the same sample always yields the same tail.
        Array.Sort(order, (a, b) =>
        {
            var byValue = totals[b].CompareTo(totals[a]);
            return byValue != 0 ? byValue : a.CompareTo(b);
        });

        var tail = new List<(int, double)>(m + 1);
        for (var i = 0; i < m && i < n; i++) tail.Add((order[i], 1.0));

        var fraction = k - m;
        if (fraction > 0 && m < n) tail.Add((order[m], fraction));

        return (tail, k);
    }

    /// <summary>Each part's share of the CVaR at <paramref name="alpha"/> of <paramref name="totals"/>.</summary>
    /// <param name="totals">The total per iteration.</param>
    /// <param name="parts">Per part, its value per iteration; the parts add up to <paramref name="totals"/>.</param>
    /// <param name="alpha">The tail level.</param>
    public static double[] Euler(double[] totals, IReadOnlyList<double[]> parts, double alpha)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var (tail, k) = TailWeights(totals, alpha);
        var shares = new double[parts.Count];

        for (var p = 0; p < parts.Count; p++)
        {
            var part = parts[p];
            if (part.Length != totals.Length)
                throw new ArgumentException("Every part has one value per iteration.", nameof(parts));

            var sum = 0.0;
            foreach (var (iteration, weight) in tail) sum += weight * part[iteration];
            shares[p] = sum / k;
        }

        return shares;
    }
}
