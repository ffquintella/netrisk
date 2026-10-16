using DAL.Enums;
using Model.RiskIndex;
using Tools.Risks;

namespace ServerServices.RiskIndex.Engine;

/// <summary>
/// The node level of the ICR (S39 §4.4–§4.8, §8.4–§8.6): the headline over the available categories with its tail floor,
/// the coverage, freshness, quality and ignorance interval, the display state and band, the exact Euler composition down
/// to objects, and the indicator sensitivity. The same formulas at every node: a node is a set of objects (S39 §10.1).
/// </summary>
public static class IcrNodeCalculator
{
    /// <summary>The categories the engine scores, in their stored order.</summary>
    public static readonly RiskIndexCategory[] Categories =
    [
        RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl,
        RiskIndexCategory.Gov, RiskIndexCategory.Ter
    ];

    /// <param name="sensitivityCount">How many (object, category) pairs of largest contribution get a sensitivity;
    /// defaults to the profile's <c>top_contribuicoes</c>; 0 skips the computation.</param>
    public static IcrNodeResult Compute(IcrNodeInput input, RiskIndexParameters p, int? sensitivityCount = null)
    {
        var results = input.Categories.Select(c => Adjust(IcrCategoryCalculator.Compute(c, p), input)).ToList();
        var headline = Headline(results.Select(r => (r.Category, r.Status, r.Value)), p);

        var weighted = results
            .Where(r => r.Status is IcrCategoryStatus.Available or IcrCategoryStatus.Unavailable)
            .ToList();
        var available = results.Where(r => r.Status == IcrCategoryStatus.Available).ToList();

        var omegaA = weighted.Sum(r => IcrCategoryCalculator.Omega(p, r.Category));
        var coverage = omegaA > 0
            ? weighted.Sum(r => IcrCategoryCalculator.Omega(p, r.Category) * r.Coverage) / omegaA
            : 0;
        var omegaK = available.Sum(r => IcrCategoryCalculator.Omega(p, r.Category));
        var freshness = omegaK > 0
            ? available.Sum(r => IcrCategoryCalculator.Omega(p, r.Category) * r.Freshness) / omegaK
            : 1;

        double? low = null, high = null;
        if (weighted.Count > 0 && headline.Value is not null)
        {
            low = Bound(weighted, p, r => r.Status == IcrCategoryStatus.Available ? r.IntervalLow : 0);
            high = Bound(weighted, p, r => r.Status == IcrCategoryStatus.Available ? r.IntervalHigh : 100);
        }

        var floored = headline.Value is { } star && input.GateAFloorTriggered ? Math.Max(star, p.GateAFloor) : headline.Value;
        var displayed = floored is { } f ? IcrMath.Displayed(f) : (int?)null;

        var state = headline.Value is null ? IcrDisplayState.NoData
            : coverage < p.CoverageIndicative ? IcrDisplayState.Indicative
            : coverage < p.CoveragePoint || weighted.Any(r => r.Status == IcrCategoryStatus.Unavailable)
                ? IcrDisplayState.WithInterval
                : IcrDisplayState.Point;

        var band = displayed is { } d && state is not (IcrDisplayState.Indicative or IcrDisplayState.NoData)
            ? RiskBand.Faixa(d, p.Bands.Select(b => (double)b.LowerInclusive).ToList())
            : -1;

        var gains = headline.Gains;
        var contributions = available
            .SelectMany(r => r.Parts.Select(part => new IcrContribution(part.Key, r.Category,
                gains.GetValueOrDefault(r.Category) * part.Part, part.Presumed, part.HoldsFloor)))
            .Where(c => c.Points > 0)
            .OrderByDescending(c => c.Points).ThenBy(c => c.Category).ThenBy(c => c.Key.Kind).ThenBy(c => c.Key.Ref,
                StringComparer.Ordinal)
            .ToList();

        var top = Math.Max(0, p.TopContributions);
        var sensitivity = headline.Value is { } current
            ? Sensitivity(input, results, contributions.Take(sensitivityCount ?? top).ToList(), current, p)
            : [];

        return new IcrNodeResult
        {
            Unfloored = headline.Value,
            Floored = floored,
            Displayed = displayed,
            BandIndex = band,
            WeightedMean = headline.Mean,
            HeadlineFloor = headline.Floor,
            HeadlineFloorActive = headline.FloorActive,
            Coverage = coverage,
            Freshness = freshness,
            Quality = coverage * freshness,
            IntervalLow = low,
            IntervalHigh = high,
            DisplayState = state,
            ObjectCount = input.Categories.SelectMany(c => c.Objects).Select(o => o.Key).Distinct().Count(),
            PresumptionPoints = available.Sum(r => gains.GetValueOrDefault(r.Category) * r.PresumptionPoints),
            NoCriticalProcess = input.CriticalProcessWeight <= 0,
            Categories = results,
            CategoryContributions = available.ToDictionary(r => r.Category, r => gains.GetValueOrDefault(r.Category) * r.Value),
            TopContributions = contributions.Take(top).ToList(),
            Sensitivity = sensitivity
        };
    }

    /// <summary>
    /// REG's coverage is <c>cov_tratamento × cov_processos</c>, and a node whose only REG inventory is a critical
    /// process with no open scenario has REG unavailable, not "not applicable" (S39 §8.3, §8.4).
    /// </summary>
    private static IcrCategoryResult Adjust(IcrCategoryResult result, IcrNodeInput input)
    {
        if (result.Category != RiskIndexCategory.Reg) return result;

        var processCoverage = input.CriticalProcessWeight > 0
            ? Math.Clamp(input.CriticalProcessWeightCovered / input.CriticalProcessWeight, 0, 1)
            : 1;

        if (result.Status == IcrCategoryStatus.NotApplicable && input.CriticalProcessWeight > 0)
            return result with { Status = IcrCategoryStatus.Unavailable, IntervalHigh = 100, Coverage = 0 };

        return result with { Coverage = result.Coverage * processCoverage };
    }

    internal sealed record HeadlineValue(double? Value, double Mean, double Floor, bool FloorActive,
        IReadOnlyDictionary<RiskIndexCategory, double> Gains);

    /// <summary>
    /// <c>ICR* = max(Σ_K ω_k C_k / Σ_K ω_k, τ_G·max_K C_k)</c> (S39 §4.4) and the gains <c>g_k = ∂ICR*/∂C_k</c>: the
    /// renormalized weight on the mean branch, <c>τ_G</c> on the floor's category (split on a tie).
    /// </summary>
    internal static HeadlineValue Headline(IEnumerable<(RiskIndexCategory Category, IcrCategoryStatus Status, double Value)> categories,
        RiskIndexParameters p)
    {
        var k = categories.Where(c => c.Status == IcrCategoryStatus.Available)
            .Select(c => (c.Category, Omega: IcrCategoryCalculator.Omega(p, c.Category), c.Value))
            .Where(c => c.Omega > 0)
            .ToList();
        if (k.Count == 0) return new HeadlineValue(null, 0, 0, false, new Dictionary<RiskIndexCategory, double>());

        var omega = k.Sum(c => c.Omega);
        var mean = k.Sum(c => c.Omega * c.Value) / omega;
        var max = k.Max(c => c.Value);
        var floor = p.TauG * max;

        if (floor > mean)
        {
            var holders = k.Where(c => Math.Abs(c.Value - max) <= 1e-12).ToList();
            return new HeadlineValue(floor, mean, floor, true,
                holders.ToDictionary(c => c.Category, _ => p.TauG / holders.Count));
        }

        return new HeadlineValue(mean, mean, floor, false, k.ToDictionary(c => c.Category, c => c.Omega / omega));
    }

    /// <summary>The headline formula over the weighted categories without renormalization (S39 §8.6).</summary>
    private static double Bound(IReadOnlyList<IcrCategoryResult> weighted, RiskIndexParameters p,
        Func<IcrCategoryResult, double> valueOf)
    {
        var omega = weighted.Sum(r => IcrCategoryCalculator.Omega(p, r.Category));
        if (omega <= 0) return 0;
        var mean = weighted.Sum(r => IcrCategoryCalculator.Omega(p, r.Category) * valueOf(r)) / omega;
        return Math.Max(mean, p.TauG * weighted.Max(valueOf));
    }

    /// <summary>
    /// <c>ICR*(S | R_{o,k} = cleared) − ICR*(S)</c> for each pair (S39 §4.8): the object becomes assessed clean in that
    /// category (an event object, one half-life after closure), everything else unchanged.
    /// </summary>
    private static List<IcrSensitivity> Sensitivity(IcrNodeInput input, IReadOnlyList<IcrCategoryResult> results,
        IReadOnlyList<IcrContribution> pairs, double current, RiskIndexParameters p)
    {
        var list = new List<IcrSensitivity>(pairs.Count);
        var inputs = input.Categories.ToDictionary(c => c.Category);

        foreach (var pair in pairs)
        {
            if (!inputs.TryGetValue(pair.Category, out var category)) continue;

            var changed = category with
            {
                Objects = category.Objects
                    .Select(o => o.Key == pair.Key && o.Membership != IcrMembership.InventoryOnly
                        ? o with { R = Math.Min(o.R, o.ClearedR), Membership = IcrMembership.Assessed }
                        : o)
                    .ToList()
            };

            var evaluator = new IcrCategoryEvaluator(p, pair.Category);
            var valued = changed.Objects.Where(o => o.Membership != IcrMembership.InventoryOnly).ToList();
            var items = valued.Select((o, i) => new IcrCategoryEvaluator.Item(i, o.Population, o.R, o.Weight,
                o.Membership == IcrMembership.Presumed)).ToArray();
            var evaluation = evaluator.Evaluate(items);

            var values = results.Select(r => r.Category == pair.Category
                ? (r.Category, r.Status, 100 * evaluation.Value)
                : (r.Category, r.Status, r.Value));
            var after = Headline(values, p).Value ?? 0;

            list.Add(new IcrSensitivity(pair.Key, pair.Category, after - current,
                evaluation.FloorActive ? evaluation.ArgMax.Select(i => valued[i].Key).ToList() : []));
        }

        return list;
    }
}
