using System;
using System.Collections.Generic;
using System.Linq;
using Tools.Risks;

namespace Tools.TailRisk;

/// <summary>One risk of a portfolio: the inputs of the run to reproduce (S48 §4.6).</summary>
public sealed record PortfolioMemberInput(
    int Id,
    CalibratedRange Frequency,
    IReadOnlyList<CalibratedRange> MagnitudeComponents,
    double MitigationEffectiveness,
    int Seed);

/// <summary>One aggregated risk: its own statistics and its share of the portfolio's CVaR95.</summary>
public sealed record PortfolioMemberResult(int Id, double ExpectedLoss, double P95, double Cvar95, double Cvar95Contribution);

/// <summary>The aggregated tail of a portfolio (S48 §4.6).</summary>
public sealed class PortfolioAggregate
{
    public TailStatistics Statistics { get; init; } = null!;

    public IReadOnlyList<PortfolioMemberResult> Members { get; init; } = [];

    /// <summary>Σ of the members' E[L] — equal to the portfolio's E[L] under any dependence.</summary>
    public double SumOfExpectedLoss { get; init; }

    /// <summary>Σ of the members' P95 — a reference only: VaR is not additive, nor even subadditive.</summary>
    public double SumOfP95 { get; init; }

    /// <summary>Σ of the members' CVaR95 — an upper bound on the portfolio's CVaR95 under any dependence.</summary>
    public double SumOfCvar95 { get; init; }

    /// <summary>Pairs between members that were declared, a coefficient of 0 included.</summary>
    public int DeclaredPairs { get; init; }

    public int Iterations { get; init; }

    public int Seed { get; init; }
}

/// <summary>
/// Stage 9.7 (S48 §4.6) — the joint annual loss of a portfolio of risk scenarios under a declared correlation, by a
/// Gaussian copula applied through rank reordering (Iman–Conover, without the score correction).
///
/// Each member is re-simulated from the inputs of its stored run, so its marginal sample is exactly that run's. For
/// every connected group of correlated members, <c>n</c> vectors of independent standard normals are drawn from the
/// portfolio's seed and multiplied by the Cholesky factor of the group's matrix; the iteration holding a member's
/// r-th smallest normal receives that member's r-th smallest annual loss. Only the order changes — every marginal is
/// preserved — and the dependence between the losses is the rank dependence of the correlated normals. A member with
/// no correlated partner is a group of one: a random permutation, i.e. independence.
///
/// Deterministic: the same members, pairs, iteration count and seed give the same aggregate in any input order.
/// </summary>
public static class PortfolioAggregator
{
    public static PortfolioAggregate Aggregate(IReadOnlyList<PortfolioMemberInput> members,
        IReadOnlyList<CorrelationPair> pairs, int iterations, int seed, int maxGroupSize = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(pairs);

        if (members.Count == 0) throw new ArgumentException("A portfolio has at least one member.", nameof(members));
        if (iterations < 1000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "A portfolio is simulated with at least 1 000 iterations.");
        if (members.Select(m => m.Id).Distinct().Count() != members.Count)
            throw new ArgumentException("A risk appears twice in the portfolio.", nameof(members));

        var ordered = members.OrderBy(m => m.Id).ToList();
        var ids = ordered.Select(m => m.Id).ToHashSet();

        var memberPairs = new Dictionary<(int, int), CorrelationPair>();
        foreach (var pair in pairs)
        {
            if (pair.A == pair.B || !ids.Contains(pair.A) || !ids.Contains(pair.B)) continue;
            if (!double.IsFinite(pair.Coefficient) || pair.Coefficient is < 0 or > 1)
                throw new ArgumentException("A declared correlation is between 0 and 1.", nameof(pairs));

            var key = (System.Math.Min(pair.A, pair.B), System.Math.Max(pair.A, pair.B));
            if (!memberPairs.TryAdd(key, pair with { A = key.Item1, B = key.Item2 }))
                throw new ArgumentException("A pair of risks is declared twice.", nameof(pairs));
        }

        var groups = CorrelationGroups.Of(ordered.Select(m => m.Id), memberPairs.Values);
        var factors = new Dictionary<int, double[,]>();

        foreach (var group in groups)
        {
            if (group.Count > maxGroupSize) throw new CorrelationGroupTooLargeException(group.Count, maxGroupSize);

            var matrix = CorrelationMatrix.Build(group, memberPairs.Values);
            if (!CorrelationMatrix.TryCholesky(matrix, out var factor)) throw new NotPositiveSemidefiniteException(group);
            factors[group[0]] = factor;
        }

        var byId = ordered.ToDictionary(m => m.Id);
        var random = new Random(seed);
        var totals = new double[iterations];
        var assigned = new Dictionary<int, double[]>();
        var own = new Dictionary<int, (double Mean, double P95, double Cvar)>();

        foreach (var group in groups)
        {
            var factor = factors[group[0]];
            var size = group.Count;

            // z[j][i] = Σ_k L[j,k]·x[k] for the i-th vector of independent normals x.
            var z = new double[size][];
            for (var j = 0; j < size; j++) z[j] = new double[iterations];

            var x = new double[size];
            for (var i = 0; i < iterations; i++)
            {
                for (var k = 0; k < size; k++) x[k] = MonteCarloRiskSimulator.SampleStandardNormal(random);

                for (var j = 0; j < size; j++)
                {
                    var value = 0.0;
                    for (var k = 0; k <= j; k++) value += factor[j, k] * x[k];
                    z[j][i] = value;
                }
            }

            for (var j = 0; j < size; j++)
            {
                var member = byId[group[j]];
                var sample = MonteCarloRiskSimulator.Simulate(member.Frequency, member.MagnitudeComponents, iterations,
                    member.Seed, member.MitigationEffectiveness).Losses;

                var sorted = (double[])sample.Clone();
                Array.Sort(sorted);
                own[member.Id] = (sorted.Average(), MonteCarloRiskSimulator.Percentile(sorted, TailStatisticsCalculator.TailLevel),
                    TailStatisticsCalculator.ExpectedShortfall(sorted, TailStatisticsCalculator.TailLevel));

                var keys = (double[])z[j].Clone();
                var order = new int[iterations];
                for (var i = 0; i < iterations; i++) order[i] = i;
                Array.Sort(keys, order);

                var losses = new double[iterations];
                for (var r = 0; r < iterations; r++) losses[order[r]] = sorted[r];

                for (var i = 0; i < iterations; i++) totals[i] += losses[i];
                assigned[member.Id] = losses;
            }
        }

        var sortedTotals = (double[])totals.Clone();
        Array.Sort(sortedTotals);
        var statistics = TailStatisticsCalculator.Compute(sortedTotals, TailStatisticsCalculator.BootstrapSeed(seed));

        var parts = ordered.Select(m => assigned[m.Id]).ToList();
        var shares = TailContributions.Euler(totals, parts, TailStatisticsCalculator.TailLevel);

        var results = ordered
            .Select((m, index) => new PortfolioMemberResult(m.Id, own[m.Id].Mean, own[m.Id].P95, own[m.Id].Cvar,
                shares[index]))
            .ToList();

        return new PortfolioAggregate
        {
            Statistics = statistics,
            Members = results,
            SumOfExpectedLoss = results.Sum(r => r.ExpectedLoss),
            SumOfP95 = results.Sum(r => r.P95),
            SumOfCvar95 = results.Sum(r => r.Cvar95),
            DeclaredPairs = memberPairs.Count,
            Iterations = iterations,
            Seed = seed
        };
    }
}
