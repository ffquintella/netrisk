using DAL.Enums;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Read;

/// <summary>
/// Which factor an object's points belong to (S40 §5.5.1; S39 §3.5, §4.2, §4.8; T253). EXP values an object by the MAX of
/// its factors, so all its Euler points go to the dominant one; the other categories compose factors by MRA, so the points
/// split by the exact MRA shares. Sub-factors: <c>EXP.V.exploited</c> (a known-exploitation signal) and <c>presumed</c>
/// (no valid reading: the prior holds the value, not a factor). Pure: the RiskFactors lines and the object list's
/// <c>factor</c> filter cannot disagree.
/// </summary>
public static class RiskIndexFactorAttribution
{
    /// <summary>The key of objects valued by the presumption prior (S39 §8.1).</summary>
    public const string Presumed = "presumed";

    /// <summary>The suffix of the exploitation sub-factor of a vulnerability reading.</summary>
    public const string ExploitedSuffix = ".exploited";

    /// <summary>The signal flag the collector sets when the exploitation floor applied (S39 §5.2).</summary>
    public const string ExploitedFlag = "exploited";

    /// <summary>The object list's filter that lists the objects of a factor (<c>GET /RiskIndex/Objects?filters=</c>).</summary>
    public static string ObjectFilter(string factorKey) => "factor=" + factorKey;

    /// <summary>The resource key of a factor's line: <c>RiskFactor_EXP_V_exploited</c>, <c>RiskFactor_presumed</c>.</summary>
    public static string LabelKey(string factorKey) => "RiskFactor_" + factorKey.Replace('.', '_');

    /// <summary>
    /// The factor keys an object row's points go to, with the share of each (summing to 1). Empty for an object with no
    /// risk (<c>R = 0</c>, assessed clean, or inventory only): it dominates nothing.
    /// </summary>
    public static IReadOnlyList<(string Key, double Share)> Split(RiskIndexCategory category, RiskIndexObjectState state, double r,
        int criticality, IReadOnlyList<StoredFactor> factors, IReadOnlyList<StoredSignal> signals, RiskIndexParameters p)
    {
        if (r <= 0 || state is RiskIndexObjectState.AssessedClean or RiskIndexObjectState.NotAssessed) return [];
        if (state == RiskIndexObjectState.Presumed) return [(Presumed, 1)];
        if (factors.Count == 0) return [];

        var m = IcrMath.Modulator(criticality, p.MA);
        var values = factors.Select(f =>
        {
            var x = f.Type == RiskIndexFactorType.V ? m * f.X : f.X;
            return Math.Max(0, f.U * IcrMath.EffectiveFactor(x, f.V, p.PiPrior, m));
        }).ToList();
        if (values.All(v => v <= 0)) return [];

        var shares = new double[factors.Count];
        if (category == RiskIndexCategory.Exp)
        {
            // MAX: the dominant factor takes it all; a tie goes to the first key in ordinal order, so the answer is stable.
            var best = Enumerable.Range(0, factors.Count)
                .OrderByDescending(i => values[i]).ThenBy(i => factors[i].Factor, StringComparer.Ordinal).First();
            shares[best] = 1;
        }
        else
        {
            var mra = IcrMath.MraShares(values, p.Rho, p.KSignals);
            var total = mra.Sum();
            for (var i = 0; i < mra.Length; i++) shares[i] = total > 0 ? mra[i] / total : 0;
        }

        return factors.Select((f, i) => (Key: Key(f, signals), Share: shares[i]))
            .Where(s => s.Share > 0)
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Sum(s => s.Share)))
            .ToList();
    }

    private static string Key(StoredFactor factor, IReadOnlyList<StoredSignal> signals) =>
        factor.Factor == RiskIndexFactors.ExpVulnerabilities &&
        signals.Any(s => s.Factor == factor.Factor && s.Flags is { } flags && flags.Contains(ExploitedFlag, StringComparer.Ordinal))
            ? factor.Factor + ExploitedSuffix
            : factor.Factor;

    /// <summary>One object row's contribution to the factor lines.</summary>
    public sealed record Attributed(IcrObjectKey Object, double Points, IReadOnlyList<(string Key, double Share)> Split);

    /// <summary>
    /// The factor lines: per key, the distinct objects it dominates (volume) and the points they carry, ordered by points,
    /// then volume, then key — a composition of the value, never a work list (S39 §1.3).
    /// </summary>
    public static IReadOnlyList<RiskIndexRiskFactorDto> Aggregate(IEnumerable<Attributed> rows)
    {
        var lines = new Dictionary<string, (HashSet<IcrObjectKey> Objects, double Points)>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var (key, share) in row.Split)
            {
                if (!lines.TryGetValue(key, out var line)) line = (new HashSet<IcrObjectKey>(), 0);
                line.Objects.Add(row.Object);
                lines[key] = (line.Objects, line.Points + row.Points * share);
            }

        return lines
            .Select(l => new RiskIndexRiskFactorDto(l.Key, LabelKey(l.Key), l.Value.Objects.Count, IcrMath.Displayed(l.Value.Points),
                ObjectFilter(l.Key)))
            .OrderByDescending(l => l.Points).ThenByDescending(l => l.Count).ThenBy(l => l.FactorKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether <paramref name="text"/> can name a factor line: a factor key, a sub-factor or <c>presumed</c>.</summary>
    public static bool IsFactorKey(string text) =>
        text.Length is > 0 and <= 32 &&
        text.All(ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_');
}
