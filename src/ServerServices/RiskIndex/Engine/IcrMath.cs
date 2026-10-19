namespace ServerServices.RiskIndex.Engine;

/// <summary>
/// The scalar primitives of the Consolidated Risk Index (ICR, S39 §4–§5, §8.2): the criticality modulator, the
/// max-plus-damped-remainder operator and its exact decomposition, reading validity and the stale-reading prior, and the
/// display rounding. Pure and allocation-light: the engine calls them once per signal and per object every night.
/// </summary>
public static class IcrMath
{
    /// <summary>A value clamped to [0, 1]; NaN reads as 0.</summary>
    public static double Clamp01(double x) => double.IsNaN(x) ? 0 : Math.Clamp(x, 0, 1);

    /// <summary>
    /// <c>m(crit) = a + (1 − a)·(crit − 1)/4</c> (S39 §5.1). The criticality is clamped to the 1–5 scale first: the
    /// entity map accepts any integer (T242), and a value outside the scale must not push a weight past 1 or below a.
    /// </summary>
    public static double Modulator(double criticality, double a)
    {
        var c = double.IsNaN(criticality) ? 1 : Math.Clamp(criticality, 1, 5);
        return a + (1 - a) * (c - 1) / 4;
    }

    /// <summary>
    /// <c>MRA_ρ(x) = x(1) + ρ·(1 − x(1))·[1 − Π_{k=2..K}(1 − x(k))]</c> over the K largest values (S39 §4.2). The pile of
    /// remaining items adds at most <c>ρ·(1 − x(1))</c>, so a count never substitutes for severity.
    /// </summary>
    public static double Mra(IEnumerable<double> values, double rho, int k)
    {
        var sorted = values.Select(Clamp01).Where(v => v > 0).OrderByDescending(v => v).Take(Math.Max(1, k)).ToArray();
        if (sorted.Length == 0) return 0;

        var top = sorted[0];
        var product = 1.0;
        for (var i = 1; i < sorted.Length; i++) product *= 1 - sorted[i];

        return top + rho * (1 - top) * (1 - product);
    }

    /// <summary>
    /// The exact composition of <see cref="Mra"/> (S39 §4.8): the leading item takes <c>x(1)</c>; the remainder
    /// <c>ρ(1 − x(1))[1 − Π]</c> is split among the items ranked 2..K in proportion to <c>−ln(1 − x(k))</c>. Items past K,
    /// and zero items, take 0. The shares are returned in the input order and sum to <see cref="Mra"/>.
    /// </summary>
    public static double[] MraShares(IReadOnlyList<double> values, double rho, int k)
    {
        var shares = new double[values.Count];
        var ranked = Enumerable.Range(0, values.Count)
            .Select(i => (Index: i, Value: Clamp01(values[i])))
            .Where(t => t.Value > 0)
            .OrderByDescending(t => t.Value).ThenBy(t => t.Index)
            .Take(Math.Max(1, k))
            .ToArray();
        if (ranked.Length == 0) return shares;

        var top = ranked[0].Value;
        shares[ranked[0].Index] = top;
        if (ranked.Length == 1) return shares;

        var product = 1.0;
        for (var i = 1; i < ranked.Length; i++) product *= 1 - ranked[i].Value;
        var remainder = rho * (1 - top) * (1 - product);
        if (remainder <= 0) return shares;

        var certain = ranked.Skip(1).Where(t => t.Value >= 1).ToArray();
        if (certain.Length > 0)
        {
            // −ln(0) is infinite: the certain items take the whole remainder, equally.
            foreach (var t in certain) shares[t.Index] = remainder / certain.Length;
            return shares;
        }

        var logs = ranked.Skip(1).Select(t => (t.Index, Log: -Math.Log(1 - t.Value))).ToArray();
        var total = logs.Sum(t => t.Log);
        foreach (var (index, log) in logs) shares[index] = total > 0 ? remainder * log / total : 0;
        return shares;
    }

    /// <summary>
    /// The validity of a reading of age <paramref name="ageDays"/> under window W (S39 §8.2): 1 up to W, linear to 0 at
    /// 2W, 0 beyond. A non-positive window reads as always valid (the profile rules refuse it, S39 §12.4).
    /// </summary>
    public static double Validity(double ageDays, double windowDays)
    {
        if (windowDays <= 0) return 1;
        if (ageDays <= windowDays) return 1;
        if (ageDays >= 2 * windowDays) return 0;
        return 1 - (ageDays - windowDays) / windowDays;
    }

    /// <summary>
    /// <c>F_eff = F + (1 − v)·max(0, π·m(crit) − F)</c> (S39 §4.2, §8.2): a stale good reading slides up towards the
    /// conservative prior; a bad one keeps its value, even at <c>v = 0</c>. Never lowers <paramref name="factor"/>.
    /// </summary>
    public static double EffectiveFactor(double factor, double validity, double prior, double modulator) =>
        factor + (1 - Clamp01(validity)) * Math.Max(0, prior * modulator - factor);

    /// <summary>The displayed integer: half away from zero, always from the full-precision value (S39 §4.5).</summary>
    public static int Displayed(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Days between two instants, as a fraction; negative when <paramref name="to"/> is earlier.</summary>
    public static double Days(DateTime from, DateTime to) => (to - from).TotalDays;
}
