using DAL.Enums;
using Model.RiskIndex;

namespace ServerServices.RiskIndex.Engine;

/// <summary>
/// The category level of the ICR (S39 §4.3): the weighted power mean with population quotas, the tail floor
/// <c>τ·max R</c>, and conservative per-object inclusion of presumed objects — plus the composition, coverage, freshness
/// and ignorance interval published beside the value (S39 §4.8, §8.4–§8.6). Pure: the same inputs give the same output.
/// </summary>
public static class IcrCategoryCalculator
{
    /// <summary>The profile key of a category's population quotas (<c>cotas_populacao</c>), or null for none.</summary>
    public static string QuotaKey(RiskIndexCategory category) => category switch
    {
        RiskIndexCategory.Reg => "REG",
        RiskIndexCategory.Exp => "EXP",
        RiskIndexCategory.Ame => "AME",
        RiskIndexCategory.Ctl => "CTL",
        RiskIndexCategory.Gov => "GOV",
        RiskIndexCategory.Ter => "TER",
        _ => ""
    };

    /// <summary>The category weight ω_k of the profile.</summary>
    public static double Omega(RiskIndexParameters p, RiskIndexCategory category) => category switch
    {
        RiskIndexCategory.Reg => p.Omega.Reg,
        RiskIndexCategory.Exp => p.Omega.Exp,
        RiskIndexCategory.Ame => p.Omega.Ame,
        RiskIndexCategory.Ctl => p.Omega.Ctl,
        RiskIndexCategory.Gov => p.Omega.Gov,
        RiskIndexCategory.Ter => p.Omega.Ter,
        _ => 0
    };

    public static IcrCategoryResult Compute(IcrCategoryInput input, RiskIndexParameters p)
    {
        var omega = Omega(p, input.Category);
        var objects = input.Objects;
        var evaluator = new IcrCategoryEvaluator(p, input.Category);

        var inventory = objects.Count;
        var valued = objects.Where(o => o.Membership != IcrMembership.InventoryOnly).ToList();

        IcrCategoryStatus status;
        if (omega <= 0) status = IcrCategoryStatus.Disabled;
        else if (inventory == 0) status = IcrCategoryStatus.NotApplicable;
        else if (input.ForcedUnavailable || !input.SourceEnabled || valued.Count == 0) status = IcrCategoryStatus.Unavailable;
        else status = IcrCategoryStatus.Available;

        var coverage = status == IcrCategoryStatus.Available ? evaluator.Coverage(objects) : 0;

        if (status != IcrCategoryStatus.Available)
            return new IcrCategoryResult
            {
                Category = input.Category, Status = status, Coverage = coverage, InventoryCount = inventory,
                IntervalLow = 0, IntervalHigh = status == IcrCategoryStatus.Unavailable ? 100 : 0,
                Partial = input.Partial, Note = input.Note
            };

        var items = valued.Select((o, i) => new IcrCategoryEvaluator.Item(i, o.Population, o.R, o.Weight,
            o.Membership == IcrMembership.Presumed)).ToArray();

        var evaluation = evaluator.Evaluate(items);
        var assessedOnly = evaluator.Evaluate(items.Where(i => !i.Presumed).ToArray());
        var value = 100 * evaluation.Value;

        var parts = evaluator.Parts(evaluation, items)
            .Select(t => new IcrObjectPart(valued[t.Index].Key, 100 * t.Share, items[t.Index].Presumed, t.HoldsFloor))
            .ToList();

        var (low, high) = evaluator.Interval(objects);

        return new IcrCategoryResult
        {
            Category = input.Category,
            Status = status,
            Value = value,
            AssessedValue = 100 * assessedOnly.Value,
            PowerMean = 100 * evaluation.PowerMean,
            FloorValue = 100 * evaluation.Floor,
            FloorActive = evaluation.FloorActive,
            HeldBy = evaluation.FloorActive ? evaluation.ArgMax.Select(i => valued[i].Key).ToList() : [],
            PresumedIncluded = evaluation.PrefixLength,
            ObjectCount = evaluation.Included.Length,
            InventoryCount = inventory,
            Coverage = coverage,
            Freshness = evaluator.Freshness(objects),
            IntervalLow = Math.Min(low, value),
            IntervalHigh = Math.Max(high, value),
            Partial = input.Partial,
            Note = input.Note,
            Parts = parts
        };
    }
}

/// <summary>
/// The arithmetic of one category, shared by the value, the composition and the change attribution's gradients. Items
/// carry an index into the caller's list; presumed items are candidates for conservative inclusion.
/// </summary>
internal sealed class IcrCategoryEvaluator
{
    private const double Tie = 1e-12;

    private readonly int _p;
    private readonly double _tau;
    private readonly IReadOnlyDictionary<string, double>? _quotas;

    internal readonly record struct Item(int Index, RiskIndexPopulation Population, double R, double W, bool Presumed);

    internal sealed class Evaluation
    {
        public double Value;
        public double PowerMean;
        public double Floor;
        public bool FloorActive;
        public int PrefixLength;
        public int[] Included = [];
        public int[] ArgMax = [];
        public Dictionary<RiskIndexPopulation, (double W, double S)> Populations = new();
        /// <summary>Each present population's quota times its path presence π (π = 1 off the attribution path).</summary>
        public Dictionary<RiskIndexPopulation, double> RawQuota = new();

        /// <summary>Each present population's quota before presence.</summary>
        public Dictionary<RiskIndexPopulation, double> BaseQuota = new();

        public double QuotaTotal;
    }

    public IcrCategoryEvaluator(RiskIndexParameters p, RiskIndexCategory category)
    {
        _p = Math.Max(1, p.P);
        _tau = p.Tau;
        _quotas = p.PopulationQuotas.TryGetValue(IcrCategoryCalculator.QuotaKey(category), out var q) ? q : null;
    }

    internal IcrCategoryEvaluator(int power, double tau, IReadOnlyDictionary<string, double>? quotas)
    {
        _p = Math.Max(1, power);
        _tau = tau;
        _quotas = quotas;
    }

    public int Power => _p;
    public double Tau => _tau;

    /// <summary>
    /// The raw quota of each population present: the profile's when it names every present population, otherwise equal
    /// shares ("tenants in an equal share when present", S39 §2.3), times the path presence <paramref name="presence"/>.
    /// </summary>
    private Dictionary<RiskIndexPopulation, double> RawQuotas(IEnumerable<RiskIndexPopulation> present,
        Func<RiskIndexPopulation, double>? presence) =>
        BaseQuotas(present).ToDictionary(kv => kv.Key, kv => kv.Value * (presence?.Invoke(kv.Key) ?? 1.0));

    private Dictionary<RiskIndexPopulation, double> BaseQuotas(IEnumerable<RiskIndexPopulation> present)
    {
        var list = present.Distinct().ToList();
        var useProfile = _quotas is not null && list.All(g => _quotas.ContainsKey(g.ToString()));
        return list.ToDictionary(g => g, g => useProfile ? Math.Max(0, _quotas![g.ToString()]) : 1.0);
    }

    private double Mean(Dictionary<RiskIndexPopulation, (double W, double S)> pops,
        Dictionary<RiskIndexPopulation, double> raw, out double quotaTotal)
    {
        quotaTotal = 0;
        foreach (var (g, (w, _)) in pops)
            if (w > 0) quotaTotal += raw.GetValueOrDefault(g);
        if (quotaTotal <= 0) return 0;

        var sum = 0.0;
        foreach (var (g, (w, s)) in pops)
        {
            if (w <= 0) continue;
            sum += raw.GetValueOrDefault(g) / quotaTotal * s / w;
        }

        return sum <= 0 ? 0 : Math.Pow(sum, 1.0 / _p);
    }

    /// <summary>
    /// <c>max_j A(Aval ∪ Pres_(1..j))</c>, <c>A(X) = max(M_p(X), τ·max R)</c>: the presumed items are added in decreasing R
    /// order and the best prefix is kept — the smallest one on a tie, so nothing that only dilutes is included.
    /// </summary>
    public Evaluation Evaluate(IReadOnlyList<Item> items, Func<RiskIndexPopulation, double>? presence = null)
    {
        var assessed = items.Where(i => !i.Presumed).ToList();
        var presumed = items.Where(i => i.Presumed).OrderByDescending(i => i.R).ThenBy(i => i.Index).ToList();

        var pops = new Dictionary<RiskIndexPopulation, (double W, double S)>();
        var maxR = 0.0;
        foreach (var i in assessed) Add(pops, i, ref maxR);

        var bestValue = Value(pops, maxR, presence);
        var bestPrefix = 0;
        for (var j = 0; j < presumed.Count; j++)
        {
            Add(pops, presumed[j], ref maxR);
            var v = Value(pops, maxR, presence);
            if (v > bestValue + Tie)
            {
                bestValue = v;
                bestPrefix = j + 1;
            }
        }

        return Detail(assessed.Concat(presumed.Take(bestPrefix)).ToList(), bestPrefix, presence);
    }

    private void Add(Dictionary<RiskIndexPopulation, (double W, double S)> pops, Item i, ref double maxR)
    {
        var r = IcrMath.Clamp01(i.R);
        var w = Math.Max(0, i.W);
        var (pw, ps) = pops.GetValueOrDefault(i.Population);
        pops[i.Population] = (pw + w, ps + w * Math.Pow(r, _p));
        if (r > maxR) maxR = r;
    }

    private double Value(Dictionary<RiskIndexPopulation, (double W, double S)> pops, double maxR,
        Func<RiskIndexPopulation, double>? presence) =>
        Math.Max(Mean(pops, RawQuotas(pops.Keys, presence), out _), _tau * maxR);

    private Evaluation Detail(List<Item> included, int prefix, Func<RiskIndexPopulation, double>? presence)
    {
        var pops = new Dictionary<RiskIndexPopulation, (double W, double S)>();
        var maxR = 0.0;
        foreach (var i in included) Add(pops, i, ref maxR);

        var raw = RawQuotas(pops.Keys, presence);
        var mean = Mean(pops, raw, out var quotaTotal);
        var floor = _tau * maxR;

        return new Evaluation
        {
            Value = Math.Max(mean, floor),
            PowerMean = mean,
            Floor = floor,
            FloorActive = floor > mean,
            PrefixLength = prefix,
            Included = included.Select(i => i.Index).ToArray(),
            ArgMax = maxR > 0
                ? included.Where(i => Math.Abs(IcrMath.Clamp01(i.R) - maxR) <= Tie).Select(i => i.Index).ToArray()
                : [],
            Populations = pops,
            RawQuota = raw,
            BaseQuota = BaseQuotas(pops.Keys),
            QuotaTotal = quotaTotal
        };
    }

    /// <summary>
    /// The exact Euler shares of <c>A</c> (S39 §4.8): on the mean branch <c>A·w'_o R_o^p / Σ w' R^p</c> (exact because
    /// <c>M_p</c> is homogeneous of degree 1), on the floor branch all of it to the largest R, split on a tie.
    /// </summary>
    public IEnumerable<(int Index, double Share, bool HoldsFloor)> Parts(Evaluation e, IReadOnlyList<Item> items)
    {
        var byIndex = items.ToDictionary(i => i.Index);
        if (e.Value <= 0) yield break;

        if (e.FloorActive)
        {
            foreach (var i in e.ArgMax) yield return (i, e.Value / e.ArgMax.Length, true);
            yield break;
        }

        var weights = new List<(int Index, double X)>();
        foreach (var index in e.Included)
        {
            var item = byIndex[index];
            var (w, _) = e.Populations[item.Population];
            if (w <= 0 || e.QuotaTotal <= 0) continue;
            var wPrime = e.RawQuota.GetValueOrDefault(item.Population) / e.QuotaTotal * Math.Max(0, item.W) / w;
            weights.Add((index, wPrime * Math.Pow(IcrMath.Clamp01(item.R), _p)));
        }

        var total = weights.Sum(t => t.X);
        if (total <= 0) yield break;
        foreach (var (index, x) in weights)
            if (x > 0) yield return (index, e.Value * x / total, false);
    }

    /// <summary>
    /// The gradient of <c>A</c> at an evaluation (S39 §9.4): with respect to each included item's R and w, and to each
    /// population's presence π. Items outside the chosen set have zero gradient.
    /// </summary>
    public void Gradient(Evaluation e, IReadOnlyList<Item> items, double[] dR, double[] dW,
        Dictionary<RiskIndexPopulation, double> dPresence)
    {
        if (e.Value <= 0) return;

        if (e.FloorActive)
        {
            foreach (var index in e.ArgMax) dR[index] += _tau / e.ArgMax.Length;
            return;
        }

        var m = e.PowerMean;
        if (m <= 0 || e.QuotaTotal <= 0) return;

        var mPow = Math.Pow(m, _p);
        var mInvPm1 = Math.Pow(m, 1 - _p);
        var byIndex = items.ToDictionary(i => i.Index);

        foreach (var index in e.Included)
        {
            var item = byIndex[index];
            var (w, s) = e.Populations[item.Population];
            if (w <= 0) continue;

            var qTilde = e.RawQuota.GetValueOrDefault(item.Population) / e.QuotaTotal;
            var r = IcrMath.Clamp01(item.R);
            var wPrime = qTilde * Math.Max(0, item.W) / w;

            dR[index] += wPrime * Math.Pow(r, _p - 1) * mInvPm1;
            dW[index] += qTilde * (Math.Pow(r, _p) - s / w) / (_p * w * Math.Pow(m, _p - 1));
        }

        foreach (var (g, (w, s)) in e.Populations)
        {
            if (w <= 0) continue;
            // ∂(Σ_h q̃_h M_h^p)/∂π_g = (q_g/Q)(M_g^p − M_p^p), with q_g the quota before presence and Q = Σ q_h π_h.
            dPresence[g] = dPresence.GetValueOrDefault(g) +
                           mInvPm1 / _p * (e.BaseQuota.GetValueOrDefault(g) / e.QuotaTotal) * (s / w - mPow);
        }
    }

    /// <summary>
    /// <c>cov_k = Σ_{Aval} w'·e / Σ_{Inv} w'</c> with the quotas computed over the inventory's populations (S39 §8.4).
    /// </summary>
    public double Coverage(IReadOnlyList<IcrCategoryObject> objects)
    {
        if (objects.Count == 0) return 0;

        var raw = RawQuotas(objects.Select(o => o.Population), null);
        var total = 0.0;
        var covered = 0.0;

        foreach (var group in objects.GroupBy(o => o.Population))
        {
            var w = group.Sum(o => Math.Max(0, o.Weight));
            var q = raw.GetValueOrDefault(group.Key);
            if (w <= 0) continue;
            total += q;
            covered += q * group.Where(o => o.Membership == IcrMembership.Assessed)
                .Sum(o => Math.Max(0, o.Weight) * IcrMath.Clamp01(o.CoverageEffect)) / w;
        }

        return total <= 0 ? 0 : covered / total;
    }

    /// <summary><c>fresh_k = Σ_{Aval} w'·v / Σ_{Aval} w'</c> (S39 §8.5).</summary>
    public double Freshness(IReadOnlyList<IcrCategoryObject> objects)
    {
        var assessed = objects.Where(o => o.Membership == IcrMembership.Assessed).ToList();
        if (assessed.Count == 0) return 1;

        var raw = RawQuotas(assessed.Select(o => o.Population), null);
        var total = 0.0;
        var fresh = 0.0;

        foreach (var group in assessed.GroupBy(o => o.Population))
        {
            var w = group.Sum(o => Math.Max(0, o.Weight));
            if (w <= 0) continue;
            var q = raw.GetValueOrDefault(group.Key);
            total += q;
            fresh += q * group.Sum(o => Math.Max(0, o.Weight) * IcrMath.Clamp01(o.Freshness)) / w;
        }

        return total <= 0 ? 1 : fresh / total;
    }

    /// <summary>
    /// The category's ignorance interval (S39 §8.6): the presumed and not-assessed objects valued at 0 (all in) and at
    /// <c>R_max</c> (conservatively included), in points.
    /// </summary>
    public (double Low, double High) Interval(IReadOnlyList<IcrCategoryObject> objects)
    {
        var low = objects.Select((o, i) => new Item(i, o.Population,
            o.Membership == IcrMembership.Assessed ? o.R : 0, o.Weight, false)).ToArray();
        var high = objects.Select((o, i) => new Item(i, o.Population,
            o.Membership == IcrMembership.Assessed ? o.R : Math.Max(o.R, o.RMax), o.Weight,
            o.Membership != IcrMembership.Assessed)).ToArray();

        return (100 * Evaluate(low).Value, 100 * Evaluate(high).Value);
    }
}
