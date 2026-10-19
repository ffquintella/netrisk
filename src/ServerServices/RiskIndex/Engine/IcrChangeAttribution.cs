using DAL.Enums;
using Model.RiskIndex;

namespace ServerServices.RiskIndex.Engine;

/// <summary>One object's share of a node's day-over-day change in one category (S39 §9.4), in points of ICR*.</summary>
public sealed record IcrAttributionEntry(IcrObjectKey Key, RiskIndexCategory Category, IcrMembership Membership,
    double RiskEffect, double ContextEffect, bool Reattribution)
{
    /// <summary>The object's whole share: the risk effect (its R moved) plus the context effect (its weight moved).</summary>
    public double Total => RiskEffect + ContextEffect;
}

/// <summary>"What moved" for one node between two days (S39 §9.4): ΔICR* = A_cfg + A_cov + A_estr + Σ A_{o,k} + A_na.</summary>
public sealed record IcrChangeAttributionResult
{
    /// <summary>ICR* on the earlier and the later day; null when the node had no data on that day.</summary>
    public double? Before { get; init; }

    public double? After { get; init; }

    /// <summary><c>ΔICR*</c>, or null when either day has no data (nothing to attribute).</summary>
    public double? Change { get; init; }

    /// <summary><c>A_cfg</c>: always 0 here — on a profile change the caller sets it from the bridge (S39 §9.5).</summary>
    public double ConfigurationEffect { get; init; }

    /// <summary><c>A_cov</c>: the effect of categories entering or leaving <c>K(S)</c>.</summary>
    public double CoverageEffect { get; init; }

    /// <summary><c>A_estr</c>: the shares of objects that entered or left the node only by reattribution.</summary>
    public double RestructuringEffect { get; init; }

    /// <summary><c>A_na</c>: the residual the integration did not resolve, published as such.</summary>
    public double Unattributed { get; init; }

    /// <summary>Every object share that is not zero, largest magnitude first (reattributed ones flagged).</summary>
    public IReadOnlyList<IcrAttributionEntry> Entries { get; init; } = [];

    /// <summary>Σ of the object shares (risk and context, reattribution excluded) per category.</summary>
    public IReadOnlyDictionary<RiskIndexCategory, double> ByCategory { get; init; } = new Dictionary<RiskIndexCategory, double>();

    /// <summary>Midpoint steps per segment used, and the number of segments between switch points.</summary>
    public int Steps { get; init; }

    public int Segments { get; init; }
}

/// <summary>
/// Aumann–Shapley path attribution of a node's change (S39 §9.4): integrated gradients along the straight path between
/// the two days' object inputs, by segments between the switch points of prefix, argmax and branch (bisection), with
/// the midpoint rule inside and the steps doubled while the residual exceeds <c>residuo_max</c> (up to 1024).
/// </summary>
public static class IcrChangeAttribution
{
    private const int MaxSteps = 1024;
    private const int Grid = 64;
    private const int Bisections = 48;

    // One path variable: an object in one membership. Entering objects start at (0, 0), leaving ones end there, and
    // presumed ↔ assessed is a leave plus an enter (S39 §9.4 "caminho").
    private sealed class Variable
    {
        public required IcrObjectKey Key;
        public required IcrMembership Membership;
        public required RiskIndexPopulation Population;
        public double R0, W0, R1, W1;
        public double Risk, Context;
    }

    private sealed class CategoryPath
    {
        public required RiskIndexCategory Category;
        public required IcrCategoryEvaluator Evaluator;
        public required List<Variable> Variables;

        /// <summary>+1 for a population that enters, −1 for one that leaves, 0 for one present on both days.</summary>
        public required Dictionary<RiskIndexPopulation, int> PresenceSlope;
    }

    public static IcrChangeAttributionResult Attribute(IcrNodeInput before, IcrNodeInput after, RiskIndexParameters p,
        IReadOnlySet<IcrObjectKey>? reattributed = null)
    {
        var beforeResults = before.Categories.Select(c => IcrCategoryCalculator.Compute(c, p)).ToList();
        var afterResults = after.Categories.Select(c => IcrCategoryCalculator.Compute(c, p)).ToList();

        var hBefore = IcrNodeCalculator.Headline(Values(beforeResults, null), p).Value;
        var hAfter = IcrNodeCalculator.Headline(Values(afterResults, null), p).Value;

        if (hBefore is null || hAfter is null)
            return new IcrChangeAttributionResult { Before = hBefore, After = hAfter };

        var common = Available(beforeResults).Intersect(Available(afterResults)).ToHashSet();

        var afterOnCommon = IcrNodeCalculator.Headline(Values(afterResults, common), p).Value ?? 0;
        var beforeOnCommon = IcrNodeCalculator.Headline(Values(beforeResults, common), p).Value ?? 0;
        var coverageEffect = common.Count == 0
            ? hAfter.Value - hBefore.Value
            : hAfter.Value - afterOnCommon + (beforeOnCommon - hBefore.Value);

        var paths = common.OrderBy(c => c).Select(c => BuildPath(c, Find(before, c), Find(after, c), p)).ToList();

        var target = common.Count == 0 ? 0 : afterOnCommon - beforeOnCommon;
        var boundaries = paths.Count == 0 ? [0.0, 1.0] : Boundaries(paths, p);

        var steps = Math.Max(16, p.AttributionSteps);
        double residual;
        while (true)
        {
            foreach (var v in paths.SelectMany(path => path.Variables)) v.Risk = v.Context = 0;
            if (paths.Count > 0) Integrate(paths, boundaries, steps, p);

            residual = target - paths.SelectMany(path => path.Variables).Sum(v => v.Risk + v.Context);
            if (Math.Abs(residual) <= p.MaximumResidual || steps >= MaxSteps || paths.Count == 0) break;
            steps = Math.Min(MaxSteps, steps * 2);
        }

        var entries = paths
            .SelectMany(path => path.Variables.Select(v => new IcrAttributionEntry(v.Key, path.Category, v.Membership,
                v.Risk, v.Context,
                reattributed is not null && reattributed.Contains(v.Key) && (v.W0 <= 0 || v.W1 <= 0))))
            .Where(e => Math.Abs(e.Total) > 1e-12)
            .OrderByDescending(e => Math.Abs(e.Total)).ThenBy(e => e.Category).ThenBy(e => e.Key.Kind)
            .ThenBy(e => e.Key.Ref, StringComparer.Ordinal)
            .ToList();

        return new IcrChangeAttributionResult
        {
            Before = hBefore,
            After = hAfter,
            Change = hAfter - hBefore,
            CoverageEffect = coverageEffect,
            RestructuringEffect = entries.Where(e => e.Reattribution).Sum(e => e.Total),
            Unattributed = residual,
            Entries = entries,
            ByCategory = entries.Where(e => !e.Reattribution).GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Total)),
            Steps = steps,
            Segments = boundaries.Count - 1
        };
    }

    private static IEnumerable<(RiskIndexCategory, IcrCategoryStatus, double)> Values(
        IEnumerable<IcrCategoryResult> results, IReadOnlySet<RiskIndexCategory>? only) =>
        results.Select(r => (r.Category,
            only is null || only.Contains(r.Category) ? r.Status : IcrCategoryStatus.Unavailable, r.Value));

    private static HashSet<RiskIndexCategory> Available(IEnumerable<IcrCategoryResult> results) =>
        results.Where(r => r.Status == IcrCategoryStatus.Available).Select(r => r.Category).ToHashSet();

    private static IcrCategoryInput? Find(IcrNodeInput node, RiskIndexCategory category) =>
        node.Categories.FirstOrDefault(c => c.Category == category);

    private static CategoryPath BuildPath(RiskIndexCategory category, IcrCategoryInput? before, IcrCategoryInput? after,
        RiskIndexParameters p)
    {
        var variables = new Dictionary<(IcrObjectKey, IcrMembership, RiskIndexPopulation), Variable>();

        Variable Get(IcrCategoryObject o) =>
            variables.TryGetValue((o.Key, o.Membership, o.Population), out var v)
                ? v
                : variables[(o.Key, o.Membership, o.Population)] = new Variable
                {
                    Key = o.Key, Membership = o.Membership, Population = o.Population
                };

        foreach (var o in before?.Objects.Where(o => o.Membership != IcrMembership.InventoryOnly) ?? [])
        {
            var v = Get(o);
            (v.R0, v.W0) = (IcrMath.Clamp01(o.R), Math.Max(0, o.Weight));
        }

        foreach (var o in after?.Objects.Where(o => o.Membership != IcrMembership.InventoryOnly) ?? [])
        {
            var v = Get(o);
            (v.R1, v.W1) = (IcrMath.Clamp01(o.R), Math.Max(0, o.Weight));
        }

        var list = variables.Values
            .OrderBy(v => v.Key.Kind).ThenBy(v => v.Key.Ref, StringComparer.Ordinal).ThenBy(v => v.Membership)
            .ToList();

        var presentBefore = list.Where(v => v.W0 > 0).Select(v => v.Population).ToHashSet();
        var presentAfter = list.Where(v => v.W1 > 0).Select(v => v.Population).ToHashSet();
        var slopes = presentBefore.Union(presentAfter).ToDictionary(g => g,
            g => presentBefore.Contains(g) && presentAfter.Contains(g) ? 0 : presentAfter.Contains(g) ? 1 : -1);

        return new CategoryPath
        {
            Category = category,
            Evaluator = new IcrCategoryEvaluator(p, category),
            Variables = list,
            PresenceSlope = slopes
        };
    }

    private static IcrCategoryEvaluator.Item[] ItemsAt(CategoryPath path, double theta)
    {
        var items = new IcrCategoryEvaluator.Item[path.Variables.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var v = path.Variables[i];
            items[i] = new IcrCategoryEvaluator.Item(i, v.Population, v.R0 + theta * (v.R1 - v.R0),
                v.W0 + theta * (v.W1 - v.W0), v.Membership == IcrMembership.Presumed);
        }

        return items;
    }

    private static Func<RiskIndexPopulation, double> PresenceAt(CategoryPath path, double theta) =>
        g => path.PresenceSlope.GetValueOrDefault(g) switch { 1 => theta, -1 => 1 - theta, _ => 1.0 };

    /// <summary>Which branch, argmax and prefix every category and the headline are on at θ.</summary>
    private static string Signature(IReadOnlyList<CategoryPath> paths, double theta, RiskIndexParameters p)
    {
        var parts = new List<string>(paths.Count + 1);
        var values = new List<(RiskIndexCategory, IcrCategoryStatus, double)>();

        foreach (var path in paths)
        {
            var e = path.Evaluator.Evaluate(ItemsAt(path, theta), PresenceAt(path, theta));
            parts.Add($"{(int)path.Category}:{e.PrefixLength}:{(e.FloorActive ? 1 : 0)}:" +
                      (e.FloorActive ? string.Join(",", e.ArgMax) : ""));
            values.Add((path.Category, IcrCategoryStatus.Available, 100 * e.Value));
        }

        var h = IcrNodeCalculator.Headline(values, p);
        parts.Add($"H:{(h.FloorActive ? 1 : 0)}:" + (h.FloorActive ? string.Join(",", h.Gains.Keys.OrderBy(k => k)) : ""));
        return string.Join("|", parts);
    }

    /// <summary>The switch points of the path, located by bisection between grid samples whose signatures differ.</summary>
    private static List<double> Boundaries(IReadOnlyList<CategoryPath> paths, RiskIndexParameters p)
    {
        var points = new List<double> { 0 };
        var previousTheta = 0.0;
        var previous = Signature(paths, 1e-9, p);

        for (var i = 1; i <= Grid; i++)
        {
            var theta = i == Grid ? 1 - 1e-9 : (double)i / Grid;
            var current = Signature(paths, theta, p);
            if (current != previous)
            {
                var (lo, hi) = (previousTheta, theta);
                for (var b = 0; b < Bisections; b++)
                {
                    var mid = (lo + hi) / 2;
                    if (Signature(paths, mid, p) == previous) lo = mid;
                    else hi = mid;
                }

                var switchAt = (lo + hi) / 2;
                if (switchAt > points[^1] + 1e-12) points.Add(switchAt);
            }

            previous = current;
            previousTheta = theta;
        }

        points.Add(1);
        return points;
    }

    private static void Integrate(IReadOnlyList<CategoryPath> paths, IReadOnlyList<double> boundaries, int steps,
        RiskIndexParameters p)
    {
        for (var s = 0; s + 1 < boundaries.Count; s++)
        {
            var (a, b) = (boundaries[s], boundaries[s + 1]);
            if (b <= a) continue;
            var dTheta = (b - a) / steps;

            for (var j = 0; j < steps; j++)
                Step(paths, a + (j + 0.5) * dTheta, dTheta, p);
        }
    }

    private static void Step(IReadOnlyList<CategoryPath> paths, double theta, double dTheta, RiskIndexParameters p)
    {
        var evaluations = paths.Select(path =>
        {
            var items = ItemsAt(path, theta);
            return (Path: path, Items: items, Eval: path.Evaluator.Evaluate(items, PresenceAt(path, theta)));
        }).ToList();

        var gains = IcrNodeCalculator.Headline(
            evaluations.Select(e => (e.Path.Category, IcrCategoryStatus.Available, 100 * e.Eval.Value)), p).Gains;

        foreach (var (path, items, eval) in evaluations)
        {
            var g = gains.GetValueOrDefault(path.Category);
            if (g == 0) continue;

            var dR = new double[items.Length];
            var dW = new double[items.Length];
            var dPresence = new Dictionary<RiskIndexPopulation, double>();
            path.Evaluator.Gradient(eval, items, dR, dW, dPresence);

            var scale = 100 * g * dTheta;
            for (var i = 0; i < items.Length; i++)
            {
                var v = path.Variables[i];
                if (dR[i] != 0) v.Risk += scale * dR[i] * (v.R1 - v.R0);
                if (dW[i] != 0) v.Context += scale * dW[i] * (v.W1 - v.W0);
            }

            foreach (var (population, slope) in path.PresenceSlope)
            {
                if (slope == 0 || !dPresence.TryGetValue(population, out var gradient) || gradient == 0) continue;

                // The presence share goes to the objects that brought (entering) or took (leaving) the population.
                var carriers = path.Variables
                    .Where(v => v.Population == population && (slope > 0 ? v.W1 : v.W0) > 0).ToList();
                var total = carriers.Sum(v => slope > 0 ? v.W1 : v.W0);
                if (total <= 0) continue;
                foreach (var v in carriers)
                    v.Risk += scale * gradient * slope * (slope > 0 ? v.W1 : v.W0) / total;
            }
        }
    }
}
