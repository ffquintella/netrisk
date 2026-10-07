using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools.TailRisk;

/// <summary>
/// The validity check of a declared correlation matrix (S48 §4.5): positive semidefinite, by a Cholesky
/// factorisation that tolerates zero pivots. A pivot below −<see cref="PivotTolerance"/> fails; a pivot within the
/// tolerance is accepted only when the rest of its column is zero too — which is what accepts ρ = 1 (two scenarios
/// moving together exactly) and refuses ρ₁₂ = 1, ρ₁₃ ≠ ρ₂₃. Never repaired (S48 D6).
/// </summary>
public static class CorrelationMatrix
{
    public const double PivotTolerance = 1e-9;

    /// <summary>The residual of a column below a zero pivot that still counts as zero.</summary>
    public const double ResidualTolerance = 1e-7;

    /// <summary>The lower-triangular factor L with L·Lᵀ = C, or false when C is not positive semidefinite.</summary>
    public static bool TryCholesky(double[,] matrix, out double[,] factor)
    {
        Validate(matrix);

        var n = matrix.GetLength(0);
        var l = new double[n, n];
        factor = l;

        for (var j = 0; j < n; j++)
        {
            var pivot = matrix[j, j];
            for (var k = 0; k < j; k++) pivot -= l[j, k] * l[j, k];

            if (pivot < -PivotTolerance) return false;

            if (pivot <= PivotTolerance)
            {
                for (var i = j + 1; i < n; i++)
                {
                    var residual = matrix[i, j];
                    for (var k = 0; k < j; k++) residual -= l[i, k] * l[j, k];
                    if (System.Math.Abs(residual) > ResidualTolerance) return false;
                }

                continue;
            }

            var d = System.Math.Sqrt(pivot);
            l[j, j] = d;

            for (var i = j + 1; i < n; i++)
            {
                var value = matrix[i, j];
                for (var k = 0; k < j; k++) value -= l[i, k] * l[j, k];
                l[i, j] = value / d;
            }
        }

        return true;
    }

    public static bool IsPositiveSemidefinite(double[,] matrix) => TryCholesky(matrix, out _);

    /// <summary>
    /// The correlation matrix of <paramref name="members"/> (in that order) from declared pairs: 1 on the diagonal,
    /// the declared coefficient where a pair is declared, 0 elsewhere. Pairs naming a non-member are ignored.
    /// </summary>
    public static double[,] Build(IReadOnlyList<int> members, IEnumerable<CorrelationPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(pairs);

        var index = new Dictionary<int, int>();
        for (var i = 0; i < members.Count; i++) index[members[i]] = i;

        var matrix = new double[members.Count, members.Count];
        for (var i = 0; i < members.Count; i++) matrix[i, i] = 1;

        foreach (var pair in pairs)
        {
            if (!index.TryGetValue(pair.A, out var a) || !index.TryGetValue(pair.B, out var b) || a == b) continue;
            matrix[a, b] = pair.Coefficient;
            matrix[b, a] = pair.Coefficient;
        }

        return matrix;
    }

    private static void Validate(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var n = matrix.GetLength(0);
        if (n != matrix.GetLength(1)) throw new ArgumentException("A correlation matrix is square.", nameof(matrix));

        for (var i = 0; i < n; i++)
        {
            if (System.Math.Abs(matrix[i, i] - 1) > PivotTolerance)
                throw new ArgumentException("A correlation matrix has 1 on its diagonal.", nameof(matrix));

            for (var j = 0; j < i; j++)
            {
                if (!double.IsFinite(matrix[i, j]) || System.Math.Abs(matrix[i, j]) > 1)
                    throw new ArgumentException("A correlation is between −1 and 1.", nameof(matrix));
                if (System.Math.Abs(matrix[i, j] - matrix[j, i]) > PivotTolerance)
                    throw new ArgumentException("A correlation matrix is symmetric.", nameof(matrix));
            }
        }
    }
}

/// <summary>A declared correlation between the annual losses of risks <see cref="A"/> and <see cref="B"/>.</summary>
public readonly record struct CorrelationPair(int A, int B, double Coefficient);

/// <summary>
/// The connected groups of a correlation graph (S48 §4.5–4.6): risks joined by a pair with a non-zero coefficient. A
/// block-diagonal matrix is positive semidefinite exactly when every block is, so validity is checked — and the copula
/// drawn — group by group.
/// </summary>
public static class CorrelationGroups
{
    /// <summary>The groups of <paramref name="nodes"/>, each sorted ascending, ordered by their smallest id.</summary>
    public static List<List<int>> Of(IEnumerable<int> nodes, IEnumerable<CorrelationPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(pairs);

        var parent = new Dictionary<int, int>();
        foreach (var node in nodes) parent[node] = node;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        foreach (var pair in pairs)
        {
            if (pair.Coefficient == 0 || !parent.ContainsKey(pair.A) || !parent.ContainsKey(pair.B)) continue;

            var a = Find(pair.A);
            var b = Find(pair.B);
            if (a != b) parent[System.Math.Max(a, b)] = System.Math.Min(a, b);
        }

        return parent.Keys
            .GroupBy(Find)
            .Select(g => g.OrderBy(id => id).ToList())
            .OrderBy(g => g[0])
            .ToList();
    }
}

/// <summary>A declared correlation matrix that is not positive semidefinite — refused, never repaired (S48 D6).</summary>
public sealed class NotPositiveSemidefiniteException(IReadOnlyList<int> group)
    : ArgumentException("The declared correlations do not form a valid (positive semidefinite) correlation matrix.")
{
    /// <summary>The ids of the correlated group whose matrix failed.</summary>
    public IReadOnlyList<int> Group { get; } = group;
}

/// <summary>A connected group of correlated risks above <c>TailRiskLimits.MaxCorrelationGroup</c>.</summary>
public sealed class CorrelationGroupTooLargeException(int size, int limit)
    : ArgumentException($"A group of {size} correlated risks exceeds the limit of {limit}.")
{
    public int Size { get; } = size;

    public int Limit { get; } = limit;
}
