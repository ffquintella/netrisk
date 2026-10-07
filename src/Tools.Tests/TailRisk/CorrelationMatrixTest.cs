using System;
using JetBrains.Annotations;
using Tools.TailRisk;
using Xunit;

namespace Tools.Tests.TailRisk;

/// <summary>
/// Stage 9.7 (S48 §4.5, §8 CM1–CM5) — the validity check of a declared correlation matrix: positive semidefinite by a
/// Cholesky factorisation that accepts zero pivots, refused (never repaired) otherwise, group by group.
/// </summary>
[TestSubject(typeof(CorrelationMatrix))]
public class CorrelationMatrixTest
{
    private static void AssertReproduces(double[,] matrix, double[,] factor)
    {
        var n = matrix.GetLength(0);
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            var value = 0.0;
            for (var k = 0; k < n; k++) value += factor[i, k] * factor[j, k];
            Assert.Equal(matrix[i, j], value, 1e-9);
        }
    }

    /// <summary>CM1 — the identity (nothing declared) is valid, and its factor is the identity.</summary>
    [Fact]
    public void TestCM1_TheIdentityIsValid()
    {
        var identity = CorrelationMatrix.Build([1, 2, 3], []);

        Assert.True(CorrelationMatrix.TryCholesky(identity, out var factor));
        AssertReproduces(identity, factor);
    }

    /// <summary>CM2 — ρ = 1 (two scenarios moving together exactly) is semidefinite and accepted, and L·Lᵀ = C.</summary>
    [Fact]
    public void TestCM2_PerfectCorrelationIsAcceptedAndTheFactorReproducesTheMatrix()
    {
        var pairs = new[] { new CorrelationPair(1, 2, 1), new CorrelationPair(1, 3, 0.4), new CorrelationPair(2, 3, 0.4) };
        var matrix = CorrelationMatrix.Build([1, 2, 3], pairs);

        Assert.True(CorrelationMatrix.TryCholesky(matrix, out var factor));
        AssertReproduces(matrix, factor);

        var moderate = CorrelationMatrix.Build([1, 2, 3],
            [new CorrelationPair(1, 2, 0.5), new CorrelationPair(1, 3, 0.3), new CorrelationPair(2, 3, 0.2)]);
        Assert.True(CorrelationMatrix.TryCholesky(moderate, out var moderateFactor));
        AssertReproduces(moderate, moderateFactor);
    }

    /// <summary>
    /// CM3 — two scenarios each moving closely with a third cannot be independent of each other: 0.9 / 0.9 / 0 has a
    /// negative eigenvalue (1 − 0.9·√2) and is refused. So is ρ₁₂ = 1 with ρ₁₃ ≠ ρ₂₃ (a zero pivot with a non-zero
    /// residual).
    /// </summary>
    [Fact]
    public void TestCM3_AMatrixThatIsNotPositiveSemidefiniteIsRefused()
    {
        var impossible = CorrelationMatrix.Build([1, 2, 3],
            [new CorrelationPair(1, 2, 0.9), new CorrelationPair(1, 3, 0.9), new CorrelationPair(2, 3, 0)]);
        Assert.False(CorrelationMatrix.IsPositiveSemidefinite(impossible));

        var inconsistent = CorrelationMatrix.Build([1, 2, 3],
            [new CorrelationPair(1, 2, 1), new CorrelationPair(1, 3, 0.5), new CorrelationPair(2, 3, 0)]);
        Assert.False(CorrelationMatrix.IsPositiveSemidefinite(inconsistent));

        // The boundary: 0.7 / 0.7 / 0 is valid (1 − 0.7·√2 ≈ 0.01 > 0).
        Assert.True(CorrelationMatrix.IsPositiveSemidefinite(CorrelationMatrix.Build([1, 2, 3],
            [new CorrelationPair(1, 2, 0.7), new CorrelationPair(1, 3, 0.7), new CorrelationPair(2, 3, 0)])));
    }

    /// <summary>CM4 — connected groups: joined by non-zero pairs only, sorted, ordered by their smallest id.</summary>
    [Fact]
    public void TestCM4_TheConnectedGroups()
    {
        var groups = CorrelationGroups.Of([9, 1, 2, 3, 4, 7],
            [new CorrelationPair(3, 1, 0.5), new CorrelationPair(4, 9, 0.2), new CorrelationPair(2, 7, 0),
             new CorrelationPair(1, 99, 0.5)]);

        // {1, 3}, {2} (a zero coefficient joins nothing), {4, 9}, {7}; the pair to 99 names a non-node.
        Assert.Equal(4, groups.Count);
        Assert.Equal([1, 3], groups[0]);
        Assert.Equal([2], groups[1]);
        Assert.Equal([4, 9], groups[2]);
        Assert.Equal([7], groups[3]);
    }

    /// <summary>CM5 — a matrix that is not a correlation matrix at all is an argument error, not a "not PSD".</summary>
    [Fact]
    public void TestCM5_AMalformedMatrixIsAnArgumentError()
    {
        Assert.Throws<ArgumentException>(() => CorrelationMatrix.TryCholesky(new double[2, 3], out _));
        Assert.Throws<ArgumentException>(() => CorrelationMatrix.TryCholesky(new double[,] { { 2, 0 }, { 0, 1 } }, out _));
        Assert.Throws<ArgumentException>(() => CorrelationMatrix.TryCholesky(new double[,] { { 1, 1.5 }, { 1.5, 1 } }, out _));
        Assert.Throws<ArgumentException>(() => CorrelationMatrix.TryCholesky(new double[,] { { 1, 0.2 }, { 0.3, 1 } }, out _));
    }
}
