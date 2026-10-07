using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Tools.TailRisk;
using Xunit;

namespace Tools.Tests.TailRisk;

/// <summary>
/// Stage 9.7 (S48 §4.6, §8 PA1–PA10) — the portfolio aggregation by Gaussian copula and rank reordering, and the
/// somability table of S48 §3: E[L] adds up, P95 does not (in either direction), CVaR is subadditive. Seeded
/// throughout: each inequality is checked on a fixed sample, and the tolerances only absorb floating-point rounding.
/// </summary>
[TestSubject(typeof(PortfolioAggregator))]
public class PortfolioAggregatorTest
{
    private const int Iterations = 10_000;

    private static readonly CalibratedRange Frequent = new(0.5, 2, 5);
    private static readonly CalibratedRange Moderate = new(10_000, 50_000, 500_000);
    private static readonly CalibratedRange Rare = new(0.02, 0.04, 0.06);
    private static readonly CalibratedRange Large = new(1_000_000, 2_000_000, 4_000_000);

    private static PortfolioMemberInput Member(int id, CalibratedRange frequency, CalibratedRange magnitude, int seed) =>
        new(id, frequency, [magnitude], 0, seed);

    private static PortfolioAggregate Two(CalibratedRange frequency, CalibratedRange magnitude, double rho, int seed = 7) =>
        PortfolioAggregator.Aggregate([Member(1, frequency, magnitude, 11), Member(2, frequency, magnitude, 22)],
            rho > 0 ? [new CorrelationPair(1, 2, rho)] : [], Iterations, seed);

    /// <summary>
    /// PA1 (T187) — with no correlation, the portfolio P95 of two frequent scenarios is well below the sum of their P95s
    /// (diversification), so summing them would overstate it by a margin no rounding explains.
    /// </summary>
    [Fact]
    public void TestPA1_AZeroCorrelationPortfolioP95IsNotTheSumOfTheP95s()
    {
        var portfolio = Two(Frequent, Moderate, 0);

        Assert.Equal(0, portfolio.DeclaredPairs);
        Assert.True(portfolio.Statistics.P95 < 0.9 * portfolio.SumOfP95,
            $"portfolio P95 {portfolio.Statistics.P95:N0} vs Σ P95 {portfolio.SumOfP95:N0}");
    }

    /// <summary>
    /// PA2 (T187) — and not even an upper bound: two independent scenarios that each lose in about 4 % of years have a
    /// P95 of zero each, but together they lose in about 8 % of years, so the portfolio P95 is positive. VaR is not
    /// subadditive; the sum of P95s (zero) understates the portfolio.
    /// </summary>
    [Fact]
    public void TestPA2_TwoRareScenariosWithZeroP95MakeAPortfolioWithAPositiveP95()
    {
        var portfolio = Two(Rare, Large, 0);

        Assert.All(portfolio.Members, m => Assert.Equal(0, m.P95));
        Assert.Equal(0, portfolio.SumOfP95);
        Assert.True(portfolio.Statistics.ProbabilityOfLoss > 0.05);
        Assert.True(portfolio.Statistics.P95 > 1_000_000, $"portfolio P95 {portfolio.Statistics.P95:N0}");
    }

    /// <summary>PA3 — E[L] is summable under any dependence: the portfolio mean is the sum of the members' means.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TestPA3_ExpectedLossIsSummableUnderAnyCorrelation(double rho)
    {
        var portfolio = Two(Frequent, Moderate, rho);

        Assert.Equal(portfolio.SumOfExpectedLoss, portfolio.Statistics.ExpectedLoss, 1e-9 * portfolio.SumOfExpectedLoss);
    }

    /// <summary>PA4 — CVaR is subadditive (Σ CVaR is an upper bound under any dependence) and grows with the correlation.</summary>
    [Fact]
    public void TestPA4_CvarIsSubadditiveAndGrowsWithTheCorrelation()
    {
        var independent = Two(Frequent, Moderate, 0);
        var half = Two(Frequent, Moderate, 0.5);
        var full = Two(Frequent, Moderate, 1);

        foreach (var p in new[] { independent, half, full })
            Assert.True(p.Statistics.Cvar95 <= p.SumOfCvar95 * (1 + 1e-12));

        Assert.True(independent.Statistics.Cvar95 < half.Statistics.Cvar95);
        Assert.True(half.Statistics.Cvar95 < full.Statistics.Cvar95);

        var rareIndependent = Two(Rare, Large, 0);
        var rareCorrelated = Two(Rare, Large, 0.5);
        Assert.True(rareIndependent.Statistics.Cvar95 < rareCorrelated.Statistics.Cvar95);
    }

    /// <summary>PA5 — ρ = 1 is comonotonic: the portfolio P95 and CVaR95 are exactly the sums.</summary>
    [Fact]
    public void TestPA5_PerfectCorrelationMakesP95AndCvarAdditive()
    {
        var full = Two(Frequent, Moderate, 1);

        Assert.Equal(full.SumOfP95, full.Statistics.P95, 1e-9 * full.SumOfP95);
        Assert.Equal(full.SumOfCvar95, full.Statistics.Cvar95, 1e-9 * full.SumOfCvar95);
    }

    /// <summary>PA6 — a fixed seed reproduces the aggregate, not only the members, whatever the input order.</summary>
    [Fact]
    public void TestPA6_AFixedSeedReproducesTheAggregateInAnyOrder()
    {
        PortfolioMemberInput[] members =
        [
            Member(3, Rare, Large, 33), Member(1, Frequent, Moderate, 11), Member(2, Frequent, Moderate, 22)
        ];
        CorrelationPair[] pairs = [new(2, 1, 0.6), new(3, 1, 0.3)];

        var first = PortfolioAggregator.Aggregate(members, pairs, Iterations, 99);
        var reversed = PortfolioAggregator.Aggregate(members.Reverse().ToList(), pairs.Reverse().ToList(), Iterations, 99);
        var again = PortfolioAggregator.Aggregate(members, pairs, Iterations, 99);

        Assert.Equal(first.Statistics, reversed.Statistics);
        Assert.Equal(first.Statistics, again.Statistics);
        Assert.Equal(first.Members, reversed.Members);
        Assert.Equal(2, first.DeclaredPairs);
    }

    /// <summary>PA7 — another portfolio seed changes the aggregated tail, never the members or Σ E[L].</summary>
    [Fact]
    public void TestPA7_AnotherSeedChangesTheTailNotTheMembers()
    {
        var a = Two(Frequent, Moderate, 0.3, seed: 1);
        var b = Two(Frequent, Moderate, 0.3, seed: 2);

        Assert.NotEqual(a.Statistics.P95, b.Statistics.P95);
        Assert.Equal(a.SumOfExpectedLoss, b.SumOfExpectedLoss);
        Assert.Equal(a.Members.Select(m => m.P95), b.Members.Select(m => m.P95));
    }

    /// <summary>PA8 — the members' Euler contributions add up to the portfolio CVaR95.</summary>
    [Fact]
    public void TestPA8_ContributionsAddUpToThePortfolioCvar()
    {
        var portfolio = PortfolioAggregator.Aggregate(
            [Member(1, Frequent, Moderate, 1), Member(2, Rare, Large, 2), Member(3, Frequent, Large, 3)],
            [new CorrelationPair(1, 3, 0.4)], Iterations, 5);

        Assert.Equal(portfolio.Statistics.Cvar95, portfolio.Members.Sum(m => m.Cvar95Contribution),
            1e-9 * portfolio.Statistics.Cvar95);
    }

    /// <summary>PA9 — a portfolio of one member is that member: its marginal is reproduced exactly.</summary>
    [Fact]
    public void TestPA9_APortfolioOfOneIsTheMember()
    {
        var own = MonteCarloRiskSimulator.Run(Frequent, Moderate, Iterations, 11);
        var portfolio = PortfolioAggregator.Aggregate([Member(1, Frequent, Moderate, 11)], [], Iterations, 3);

        Assert.Equal(own.Tail.P95, portfolio.Statistics.P95);
        Assert.Equal(own.Tail.Cvar95, portfolio.Statistics.Cvar95, 1e-9 * own.Tail.Cvar95);
        Assert.Equal(own.Mean, portfolio.Statistics.ExpectedLoss, 1e-9 * own.Mean);
        Assert.Equal(portfolio.Statistics.Cvar95, Assert.Single(portfolio.Members).Cvar95Contribution,
            1e-9 * portfolio.Statistics.Cvar95);
    }

    /// <summary>PA10 — a declared matrix that is not positive semidefinite, or a group above the limit, is refused.</summary>
    [Fact]
    public void TestPA10_AnInvalidMatrixOrAnOversizedGroupIsRefused()
    {
        PortfolioMemberInput[] members =
            [Member(1, Frequent, Moderate, 1), Member(2, Frequent, Moderate, 2), Member(3, Frequent, Moderate, 3)];

        var ex = Assert.Throws<NotPositiveSemidefiniteException>(() => PortfolioAggregator.Aggregate(members,
            [new CorrelationPair(1, 2, 0.9), new CorrelationPair(1, 3, 0.9)], Iterations, 1));
        Assert.Equal([1, 2, 3], ex.Group);

        Assert.Throws<CorrelationGroupTooLargeException>(() => PortfolioAggregator.Aggregate(members,
            [new CorrelationPair(1, 2, 0.2), new CorrelationPair(2, 3, 0.2)], Iterations, 1, maxGroupSize: 2));
    }

    /// <summary>The input guards: no member, a repeated member, too few iterations, a coefficient out of range, a pair twice.</summary>
    [Fact]
    public void TestTheInputGuards()
    {
        var one = Member(1, Frequent, Moderate, 1);
        var two = Member(2, Frequent, Moderate, 2);

        Assert.Throws<ArgumentException>(() => PortfolioAggregator.Aggregate([], [], Iterations, 1));
        Assert.Throws<ArgumentException>(() => PortfolioAggregator.Aggregate([one, one], [], Iterations, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PortfolioAggregator.Aggregate([one], [], 500, 1));
        Assert.Throws<ArgumentException>(() =>
            PortfolioAggregator.Aggregate([one, two], [new CorrelationPair(1, 2, -0.1)], Iterations, 1));
        Assert.Throws<ArgumentException>(() => PortfolioAggregator.Aggregate([one, two],
            [new CorrelationPair(1, 2, 0.1), new CorrelationPair(2, 1, 0.2)], Iterations, 1));

        // A pair naming a risk outside the portfolio is not part of this portfolio's matrix.
        var ignored = PortfolioAggregator.Aggregate([one, two], [new CorrelationPair(1, 99, 0.9)], Iterations, 1);
        Assert.Equal(0, ignored.DeclaredPairs);
    }
}
