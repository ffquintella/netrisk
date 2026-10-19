using DAL.Enums;
using Model.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Profiles;

/// <summary>
/// A stored day recomposed under other parameters (S39 §9.5, §13.2): what the sensitivity preview and the bridge's overlay
/// compute. Exact for the aggregation and display keys (<see cref="RiskIndexProfileRules.ReplayableKeys"/>); anything that
/// changes an object's R needs the raw data, so a change outside them makes the result "partial".
/// </summary>
public static class RiskIndexReplay
{
    /// <summary>Every pre-computed node of the day under <paramref name="p"/>: values and composition, no sensitivity.</summary>
    public static RiskIndexDay Day(RiskIndexObjectSet set, RiskIndexParameters p, RiskIndexMode mode, DateTime instant) =>
        RiskIndexDayCalculator.Compose(set, new RiskIndexDayRequest { Parameters = p, Mode = mode, SnapshotInstant = instant },
            RiskIndexComposeOptions.Replay);

    /// <summary>The root alone, for the tornado and the presets: one node instead of hundreds.</summary>
    public static IcrNodeResult Root(RiskIndexObjectSet set, RiskIndexParameters p, RiskIndexMode mode)
    {
        var rows = set.Rows;
        var root = new NodeMembers
        {
            Node = NodeMembership.Root,
            Closure = set.Map.Ids.ToHashSet(),
            Objects = rows.Select(r => r.Key).ToHashSet(),
            ScopeEntities = rows.Where(r => r.ScopeEntityId is not null).Select(r => r.ScopeEntityId!.Value).ToHashSet(),
            HasUnscopedObjects = rows.Any(r => r.ScopeEntityId is null)
        };
        return IcrNodeCalculator.Compute(RiskIndexDayCalculator.BuildNodeInput(root, set, mode, p), p, 0);
    }

    /// <summary>Each object's points in the headline, <c>Σ_k g_k·part_{o,k}</c> (S39 §4.8), over every part.</summary>
    public static Dictionary<(IcrObjectKey Key, RiskIndexCategory Category), double> Contributions(IcrNodeResult result)
    {
        var map = new Dictionary<(IcrObjectKey, RiskIndexCategory), double>();
        foreach (var category in result.Categories.Where(c => c.Status == IcrCategoryStatus.Available && c.Value > 0))
        {
            var gain = result.CategoryContributions.GetValueOrDefault(category.Category) / category.Value;
            foreach (var part in category.Parts)
                map[(part.Key, category.Category)] = map.GetValueOrDefault((part.Key, category.Category)) + gain * part.Part;
        }

        return map;
    }

    /// <summary>
    /// The register's effective weight (S39 §13.2): REG's whole share of the headline plus the scenario population's shares
    /// of GOV and CTL, as a fraction of ICR*.
    /// </summary>
    public static double RegisterWeight(IcrNodeResult result)
    {
        if (result.Unfloored is not { } headline || headline <= 0) return 0;
        var reg = result.CategoryContributions.GetValueOrDefault(RiskIndexCategory.Reg);
        var scenarios = Contributions(result)
            .Where(kv => kv.Key.Key.Kind == RiskIndexObjectKind.Scenario && kv.Key.Category is RiskIndexCategory.Gov or RiskIndexCategory.Ctl)
            .Sum(kv => kv.Value);
        return Math.Clamp((reg + scenarios) / headline, 0, 1);
    }

    /// <summary>The tornado of S39 §13.2 (example §11.6): ±20 % on each ω (the others rescaled to Σω = 1), p, τ, τ_G, ρ and the
    /// population quotas, one at a time, as the root's headline.</summary>
    public static IReadOnlyList<RiskIndexTornadoRowDto> Tornado(RiskIndexObjectSet set, RiskIndexParameters p, RiskIndexMode mode)
    {
        var rows = new List<RiskIndexTornadoRowDto>();
        double Value(RiskIndexParameters v) => Root(set, v, mode).Unfloored ?? 0;
        var baseValue = Value(p);

        foreach (var category in new[] { RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame,
                     RiskIndexCategory.Ctl, RiskIndexCategory.Gov })
        {
            if (IcrCategoryCalculator.Omega(p, category) <= 0) continue;
            rows.Add(new RiskIndexTornadoRowDto("omega." + IcrCategoryCalculator.QuotaKey(category),
                Value(ScaleOmega(p, category, 0.8)) - baseValue, Value(ScaleOmega(p, category, 1.2)) - baseValue));
        }

        rows.Add(new RiskIndexTornadoRowDto("p", Value(p with { P = Math.Max(1, (int)Math.Round(p.P * 0.8)) }) - baseValue,
            Value(p with { P = (int)Math.Round(p.P * 1.2) }) - baseValue));
        rows.Add(new RiskIndexTornadoRowDto("tau", Value(p with { Tau = p.Tau * 0.8 }) - baseValue,
            Value(p with { Tau = Math.Min(1, p.Tau * 1.2) }) - baseValue));
        rows.Add(new RiskIndexTornadoRowDto("tau_g", Value(p with { TauG = p.TauG * 0.8 }) - baseValue,
            Value(p with { TauG = Math.Min(1, p.TauG * 1.2) }) - baseValue));
        rows.Add(new RiskIndexTornadoRowDto("rho", Value(p with { Rho = p.Rho * 0.8 }) - baseValue,
            Value(p with { Rho = p.Rho * 1.2 }) - baseValue));

        foreach (var (category, quotas) in p.PopulationQuotas)
        {
            var first = quotas.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
            if (first is null || quotas.Count < 2) continue;
            rows.Add(new RiskIndexTornadoRowDto($"cotas_populacao.{category}.{first}",
                Value(ScaleQuota(p, category, first, 0.8)) - baseValue, Value(ScaleQuota(p, category, first, 1.2)) - baseValue));
        }

        return rows;
    }

    /// <summary>ω_k × factor, the other weights rescaled so the vector still sums to 1.</summary>
    public static RiskIndexParameters ScaleOmega(RiskIndexParameters p, RiskIndexCategory category, double factor)
    {
        var w = new Dictionary<RiskIndexCategory, double>
        {
            [RiskIndexCategory.Reg] = p.Omega.Reg, [RiskIndexCategory.Exp] = p.Omega.Exp, [RiskIndexCategory.Ame] = p.Omega.Ame,
            [RiskIndexCategory.Ctl] = p.Omega.Ctl, [RiskIndexCategory.Gov] = p.Omega.Gov, [RiskIndexCategory.Ter] = p.Omega.Ter
        };
        var target = Math.Clamp(w[category] * factor, 0, 1);
        var others = w.Where(kv => kv.Key != category).Sum(kv => kv.Value);
        var scale = others > 0 ? (1 - target) / others : 0;
        foreach (var key in w.Keys.ToList()) w[key] = key == category ? target : w[key] * scale;
        return p with
        {
            Omega = new RiskIndexCategoryWeights
            {
                Reg = w[RiskIndexCategory.Reg], Exp = w[RiskIndexCategory.Exp], Ame = w[RiskIndexCategory.Ame],
                Ctl = w[RiskIndexCategory.Ctl], Gov = w[RiskIndexCategory.Gov], Ter = w[RiskIndexCategory.Ter]
            }
        };
    }

    private static RiskIndexParameters ScaleQuota(RiskIndexParameters p, string category, string population, double factor)
    {
        var quotas = p.PopulationQuotas[category].ToDictionary(kv => kv.Key, kv => kv.Value);
        var target = Math.Clamp(quotas[population] * factor, 0, 1);
        var others = quotas.Where(kv => kv.Key != population).Sum(kv => kv.Value);
        var scale = others > 0 ? (1 - target) / others : 0;
        foreach (var key in quotas.Keys.ToList()) quotas[key] = key == population ? target : quotas[key] * scale;
        var all = p.PopulationQuotas.ToDictionary(kv => kv.Key, kv => kv.Value);
        all[category] = quotas;
        return p with { PopulationQuotas = all };
    }

    /// <summary>Kendall's τ-b between two rankings of the same units (S39 §13.2); null under two units.</summary>
    public static double? KendallTau(IReadOnlyList<(double A, double B)> pairs)
    {
        if (pairs.Count < 2) return null;
        long concordant = 0, discordant = 0, tiesA = 0, tiesB = 0;
        for (var i = 0; i < pairs.Count; i++)
        for (var j = i + 1; j < pairs.Count; j++)
        {
            var a = Math.Sign(pairs[i].A - pairs[j].A);
            var b = Math.Sign(pairs[i].B - pairs[j].B);
            if (a == 0 && b == 0) continue;
            if (a == 0) tiesA++;
            else if (b == 0) tiesB++;
            else if (a == b) concordant++;
            else discordant++;
        }

        var denominator = Math.Sqrt((double)(concordant + discordant + tiesA) * (concordant + discordant + tiesB));
        return denominator > 0 ? (concordant - discordant) / denominator : null;
    }
}
