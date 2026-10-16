using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using DAL.Enums;

namespace Model.RiskIndex;

/// <summary>The presets of S39 §12.3. "Média pura" exists only for the preview and is never activatable.</summary>
public static class RiskIndexPresets
{
    public const string Balanced = "balanced";
    public const string RegisterCentred = "register_centred";
    public const string Ctem = "ctem";
    public const string Conservative = "conservative";
    public const string PureMean = "pure_mean";

    public static RiskIndexParameters Parameters(string key) => key switch
    {
        Balanced => new RiskIndexParameters(),
        RegisterCentred => new RiskIndexParameters
        {
            Omega = new RiskIndexCategoryWeights { Reg = 0.45, Exp = 0.20, Ame = 0.10, Ctl = 0.05, Gov = 0.20 }
        },
        Ctem => new RiskIndexParameters
        {
            Omega = new RiskIndexCategoryWeights { Reg = 0.20, Exp = 0.40, Ame = 0.20, Ctl = 0.10, Gov = 0.10 }
        },
        Conservative => new RiskIndexParameters { P = 8, Tau = 0.90, TauG = 0.85 },
        PureMean => new RiskIndexParameters { P = 1, Tau = 0, TauG = 0 },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown preset.")
    };

    public static readonly IReadOnlyList<string> All = [Balanced, RegisterCentred, Ctem, Conservative, PureMean];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key, StringComparer.Ordinal);

    public static bool IsActivatable(string key) => key != PureMean;

    /// <summary>The preset list of <c>GET /RiskIndex/Profiles/Presets</c> (S40 §8.4).</summary>
    public static IReadOnlyList<RiskIndexPresetDto> List() =>
        All.Select(k => new RiskIndexPresetDto(k, "RiskPreset_" + k, Parameters(k), IsActivatable(k))).ToList();
}

/// <summary>
/// The validation of a profile (S39 §12.2, §12.4; S40 §6.2.3, §8.4) — pure, so the server (authoritative) and the desktop
/// editor (live) can never disagree. Structural and cross-field rules are <c>Errors</c> and invalidate the draft. The pace
/// rules (ω ±0.05 a quarter, at most two parameters per cycle, a preset or a mode change) never invalidate it: they are
/// <c>Warnings</c> that mark the submission a rebaseline proposal, and only the approval needs the explicit confirmation.
/// Only the very first profile (no profile ever approved) has no baseline and no pace rule, and always needs the Board.
/// </summary>
public static class RiskIndexProfileRules
{
    /// <summary>Σω = 1 within this tolerance (S39 §12.4); also the quota sums.</summary>
    public const double SumTolerance = 1e-6;

    /// <summary>The quarter's pace limit on each ω (S39 §12.4).</summary>
    public const double OmegaPace = 0.05;

    /// <summary>The parameters a calibration cycle may change without a rebaseline (S39 §12.4, §13.3).</summary>
    public const int MaxChangedParameters = 2;

    /// <summary>A scalar key of S39 §12.2 with its allowed range (inclusive); <c>ZeroAllowed</c> for "0 or [min; max]".</summary>
    internal sealed record ScalarRule(string Key, string Group, string Type, Func<RiskIndexParameters, double> Get,
        double Min, double Max, bool ZeroAllowed = false, bool Locked = false, string? LockedReasonKey = null);

    internal static readonly IReadOnlyList<ScalarRule> Scalars =
    [
        new("omega.REG", "weights", "double", p => p.Omega.Reg, 0.20, 0.60),
        new("omega.EXP", "weights", "double", p => p.Omega.Exp, 0.05, 0.60, ZeroAllowed: true),
        new("omega.AME", "weights", "double", p => p.Omega.Ame, 0.05, 0.60, ZeroAllowed: true),
        new("omega.CTL", "weights", "double", p => p.Omega.Ctl, 0.05, 0.60, ZeroAllowed: true),
        new("omega.GOV", "weights", "double", p => p.Omega.Gov, 0.05, 0.60, ZeroAllowed: true),
        // TER stays at 0 until M48 (S39 §2.3, §12.2).
        new("omega.TER", "weights", "double", p => p.Omega.Ter, 0, 0, Locked: true, LockedReasonKey: "RiskParamLocked_TerUntilM48"),

        new("p", "aggregation", "int", p => p.P, 2, 16),
        new("tau", "aggregation", "double", p => p.Tau, 0.60, 0.95),
        new("tau_g", "aggregation", "double", p => p.TauG, 0.50, 1.00),
        new("rho", "aggregation", "double", p => p.Rho, 0, 0.5),
        new("k_sinais", "aggregation", "int", p => p.KSignals, 2, 50),

        new("m_a", "criticality", "double", p => p.MA, 0.20, 0.60),
        new("crit_padrao", "criticality", "int", p => p.DefaultCriticality, 2, 4),
        new("crit_padrao_nao_producao", "criticality", "int", p => p.DefaultNonProductionCriticality, 1, 3),
        new("crit_max_vision_one", "criticality", "int", p => p.VisionOneCriticalityCeiling, 3, 5),
        new("crit_ext", "criticality", "int", p => p.ExternalCriticality, 3, 5),
        new("passo_classificacao", "criticality", "int", p => p.ClassificationStep, 0, 2),
        new("beta_rank", "criticality", "double", p => p.BetaRank, 0, 0.6),
        new("validade_rank_ciclos", "criticality", "int", p => p.RankValidityCycles, 1, 4),

        new("m_x_disponivel", "normalization", "double", p => p.ExploitAvailableMultiplier, 1, 1.5),
        new("teto_sem_exploracao", "normalization", "double", p => p.NoExploitationCeiling, 0.70, 0.95),
        new("piso_exploracao", "normalization", "double", p => p.ExploitationFloor, 0.80, 1.0),
        new("epss_limiar", "normalization", "double", p => p.EpssThreshold, 0.01, 0.5),
        new("m_net", "normalization", "double", p => p.InternetMultiplier, 1, 1.5),
        new("fator_quase_incidente", "normalization", "double", p => p.NearMissFactor, 0.25, 1),
        new("irp_limiar_sigma", "normalization", "double", p => p.IrpSigmaThreshold, 0.5, 0.9),
        new("irp_rampa", "normalization", "double", p => p.IrpRamp, 0.05, 0.2),
        new("irp_teste_validade", "normalization", "int", p => p.IrpTestValidityDays, 180, 730),

        new("pi_prior", "time", "double", p => p.PiPrior, 0.3, 1.0),
        new("meia_vida_incidente", "time", "int", p => p.IncidentHalfLifeDays, 7, 90),
        new("janela_incidente", "time", "int", p => p.IncidentWindowDays, 30, 365),
        new("janela_intake_incidentes", "time", "int", p => p.IncidentIntakeWindowDays, 30, 365),
        new("janela_inventario_sinal", "time", "int", p => p.InventorySignalWindowDays, 60, 730),

        new("lambda_credito", "governance", "double", p => p.CreditLambda, 0.25, 2),
        new("credito_minimo", "governance", "double", p => p.MinimumCredit, 0, 0.5),
        new("janela_revisao_derivada", "governance", "int", p => p.DerivedReviewWindowSeconds, 10, 300),
        new("carencia_aceite", "governance", "int", p => p.AcceptanceGraceDays, 0, 30),
        new("acc_drift_inicio", "governance", "double", p => p.AcceptanceDriftStart, 0, 2),
        new("acc_drift_amplitude", "governance", "double", p => p.AcceptanceDriftAmplitude, 0.5, 5),
        new("prazo_contra_assinatura", "governance", "int", p => p.CounterSignatureDays, 1, 30),
        new("prazo_seguimento_decisao", "governance", "int", p => p.DecisionFollowUpDays, 7, 90),
        new("prazo_ref_tarefa", "governance", "int", p => p.TaskReferenceDays, 7, 90),
        new("prazo_revisao_solicitada", "governance", "int", p => p.RequestedReviewDays, 3, 60),
        new("delta_div", "governance", "double", p => p.DivergenceDelta, 0.1, 0.5),
        new("janela_aviso_expiracao", "governance", "int", p => p.ExpiryWarningDays, 7, 60),
        // An acceptance is not mitigation (S39 I10): locked at 0.
        new("desconto_aceite", "governance", "double", p => p.AcceptanceDiscount, 0, 0, Locked: true,
            LockedReasonKey: "RiskParamLocked_AcceptanceIsNotMitigation"),
        new("s.APT_BRK", "governance", "double", p => p.Signals.AppetiteBreach, 0, 1),
        new("s.APT_BRK_TREATED", "governance", "double", p => p.Signals.AppetiteBreachTreated, 0, 1),
        new("s.ACC_APT", "governance", "double", p => p.Signals.AcceptanceBeforeAppetite, 0, 1),
        new("s.ACC_EXP", "governance", "double", p => p.Signals.AcceptanceExpired, 0, 1),
        new("s.ACC_REV", "governance", "double", p => p.Signals.AcceptanceRevoked, 0, 1),
        new("s.APT_DUAL", "governance", "double", p => p.Signals.CounterSignaturePending, 0, 1),
        new("s.CMP_PEND", "governance", "double", p => p.Signals.CampaignPending, 0, 1),
        new("s.CMP_ESC", "governance", "double", p => p.Signals.CampaignEscalated, 0, 1),
        new("s.CMP_MIT", "governance", "double", p => p.Signals.CampaignMitigate, 0, 1),
        new("s.REV_REQ", "governance", "double", p => p.Signals.ReviewRequested, 0, 1),
        new("s.DIV", "governance", "double", p => p.Signals.Divergence, 0, 1),

        // φ_A is never switched off (S39 §7, §12.4).
        new("piso_portao_a", "display", "int", p => p.GateAFloor, 70, 100),
        new("cov_ponto", "display", "double", p => p.CoveragePoint, 0.5, 0.95),
        new("cov_indicativo", "display", "double", p => p.CoverageIndicative, 0.2, 0.6),
        new("qualidade_alta", "display", "double", p => p.QualityHigh, 0.000001, 0.999999),
        new("qualidade_media", "display", "double", p => p.QualityMedium, 0.000001, 0.999999),
        new("limiar_nao_atribuido", "display", "double", p => p.UnassignedThreshold, 0.02, 0.5),
        new("cobertura_quantificacao_min", "display", "double", p => p.MinimumQuantificationCoverage, 0.5, 1),
        new("ewma_n", "display", "int", p => p.EwmaN, 3, 30),
        new("delta_seta", "display", "int", p => p.ArrowDelta, 1, 10),
        new("delta_detalhe", "display", "double", p => p.DetailDelta, 0.5, 5),
        new("top_contribuicoes", "display", "int", p => p.TopContributions, 10, 200),
        new("passos_atribuicao", "display", "int", p => p.AttributionSteps, 16, 1024),
        new("residuo_max", "display", "double", p => p.MaximumResidual, 0.01, 0.5)
    ];

    /// <summary>The keys of S39 §12.2 that are not scalars, with their group (for the schema and the diff).</summary>
    private static readonly IReadOnlyList<(string Key, string Group, string Type)> Composites =
    [
        ("faixas", "bands", "bands"), ("u", "weights", "map"), ("cotas_populacao", "weights", "map"),
        ("ambientes_nao_producao", "criticality", "list"), ("niveis_sensiveis", "criticality", "list"),
        ("ancoras_severidade", "normalization", "map"), ("heuristica_ip_publico", "normalization", "bool"),
        ("faixas_publicas_internas", "normalization", "list"), ("ancoras_ssc", "normalization", "map"),
        ("ancoras_incidente_categoria", "normalization", "map"), ("ancoras_incidente_severidade", "normalization", "map"),
        ("fontes_habilitadas", "time", "list"), ("frescor_W", "time", "map"), ("tipos_avaliaveis", "time", "list"),
        ("cadencia_revisao", "governance", "object"), ("limites_registro", "governance", "object"),
        ("base_cadencia", "governance", "enum")
    ];

    /// <summary>
    /// The keys a 90-day replay from stored object rows reproduces exactly (S39 §9.5): aggregation and display only. Any
    /// other change needs the raw data, so an overlay or a preview under it is labelled partial.
    /// </summary>
    public static readonly IReadOnlySet<string> ReplayableKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "faixas", "omega", "p", "tau", "tau_g", "cotas_populacao", "cov_ponto", "cov_indicativo", "qualidade_alta",
        "qualidade_media", "limiar_nao_atribuido", "cobertura_quantificacao_min", "piso_portao_a", "ewma_n", "delta_seta",
        "delta_detalhe", "top_contribuicoes", "passos_atribuicao", "residuo_max", "janela_aviso_expiracao"
    };

    /// <summary>Every source id S39 §3.2 defines (<see cref="RiskIndexSources"/>).</summary>
    public static readonly IReadOnlySet<string> KnownSources = typeof(RiskIndexSources)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true, FieldType.Name: nameof(String) })
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> KnownFactors =
        RiskIndexFactors.DefaultCeilings().Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The schema of <c>GET /RiskIndex/Profiles/Schema</c>: every key, its group, range, default and lock.</summary>
    public static RiskIndexParameterSchemaDto Schema()
    {
        var defaults = new RiskIndexParameters();
        var items = Scalars.Select(r => new RiskIndexParameterSchemaItemDto(r.Key, r.Group, r.Type,
                Format(r.Min), Format(r.Max), Format(r.Get(defaults)), r.Locked, r.LockedReasonKey, Help(r.Key), "S39 §12.2"))
            .Concat(Composites.Select(c => new RiskIndexParameterSchemaItemDto(c.Key, c.Group, c.Type, null, null,
                TopLevel(defaults)[c.Key]?.ToJsonString() ?? "null", false, null, Help(c.Key), "S39 §12.2")))
            .ToList();
        return new RiskIndexParameterSchemaDto(items);

        static string Help(string key) => "RiskParam_" + key.Replace('.', '_') + "_Help";
    }

    /// <summary>The group a §12.2 key belongs to, for the diff.</summary>
    public static string GroupOf(string key)
    {
        var scalar = Scalars.FirstOrDefault(r => r.Key == key);
        if (scalar is not null) return scalar.Group;
        var top = key.Split('.')[0];
        if (top == "omega") return "weights";
        if (top == "s") return "governance";
        return Composites.FirstOrDefault(c => c.Key == top).Group ?? "other";
    }

    // ------------------------------------------------------------------------------------------------------------------

    public static RiskIndexValidationResultDto Validate(RiskIndexMode draftMode, RiskIndexParameters draft,
        RiskIndexMode? baselineMode, RiskIndexParameters? baseline, RiskIndexParameters? trimesterBaseline,
        string? draftPreset = null, string? baselinePreset = null, int? trimesterBaselineProfileId = null)
    {
        var errors = new List<RiskIndexValidationIssueDto>();
        Structural(draftMode, draft, errors);

        var warnings = new List<RiskIndexValidationIssueDto>();
        var triggers = new List<RiskIndexRebaselineTrigger>();
        IReadOnlyList<string> changed = [];
        var requiresBoard = true;

        // Only the very first profile has no baseline: no pace rule, and the Board approves its bands (S39 §12.4).
        if (baseline is not null)
        {
            changed = ChangedKeys(baseline, draft);
            requiresBoard = !BandsEqual(baseline.Bands, draft.Bands);

            if (draftPreset is not null && !string.Equals(draftPreset, baselinePreset, StringComparison.Ordinal))
            {
                triggers.Add(RiskIndexRebaselineTrigger.PresetChange);
                warnings.Add(Issue("rebaseline_preset_change", null, ("from", baselinePreset ?? ""), ("to", draftPreset)));
            }

            if (baselineMode is not null && baselineMode != draftMode)
            {
                triggers.Add(RiskIndexRebaselineTrigger.ModeChange);
                warnings.Add(Issue("mode_change", null, ("from", ((int)baselineMode).ToString(CultureInfo.InvariantCulture)),
                    ("to", ((int)draftMode).ToString(CultureInfo.InvariantCulture))));
            }

            var quarter = trimesterBaseline ?? baseline;
            var paced = false;
            foreach (var (key, before, after) in OmegaPairs(quarter, draft))
            {
                if (Math.Abs(after - before) <= OmegaPace + SumTolerance) continue;
                paced = true;
                warnings.Add(Issue("rebaseline_omega_pace", key, ("from", Format(before)), ("to", Format(after)),
                    ("limit", Format(OmegaPace))));
            }

            if (paced) triggers.Add(RiskIndexRebaselineTrigger.OmegaPace);

            if (changed.Count > MaxChangedParameters)
            {
                triggers.Add(RiskIndexRebaselineTrigger.ParameterCount);
                warnings.Add(Issue("rebaseline_parameter_count", null,
                    ("count", changed.Count.ToString(CultureInfo.InvariantCulture)),
                    ("limit", MaxChangedParameters.ToString(CultureInfo.InvariantCulture))));
            }
        }

        if (requiresBoard) warnings.Add(Issue("board_ref_required", "faixas"));

        return new RiskIndexValidationResultDto(errors.Count == 0, errors, warnings, changed, triggers.Count > 0, triggers,
            requiresBoard, trimesterBaselineProfileId);
    }

    /// <summary>The §12.2 keys whose value differs: ω counts once, and so do <c>frescor_W</c>, <c>fontes_habilitadas</c>,
    /// <c>faixas</c> and <c>cotas_populacao</c>; each <c>u.‹factor›</c> and <c>s.‹signal›</c> is a key of its own.</summary>
    public static IReadOnlyList<string> ChangedKeys(RiskIndexParameters a, RiskIndexParameters b)
    {
        var left = TopLevel(a);
        var right = TopLevel(b);
        var changed = new List<string>();
        foreach (var key in left.Select(kv => kv.Key).Union(right.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
        {
            var x = left[key];
            var y = right[key];
            if (key is "u" or "s")
            {
                var xo = x as JsonObject ?? new JsonObject();
                var yo = y as JsonObject ?? new JsonObject();
                foreach (var sub in xo.Select(kv => kv.Key).Union(yo.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
                    if (!JsonNode.DeepEquals(xo[sub], yo[sub])) changed.Add(key + "." + sub);
                continue;
            }

            if (!JsonNode.DeepEquals(x, y)) changed.Add(key);
        }

        return changed;
    }

    /// <summary>Every §12.2 key with its value in each profile, for <c>GET /Profiles/Diff</c>.</summary>
    public static IReadOnlyList<RiskIndexParameterChangeDto> Diff(RiskIndexParameters a, RiskIndexParameters b)
    {
        var left = TopLevel(a);
        var right = TopLevel(b);
        var rows = new List<RiskIndexParameterChangeDto>();
        foreach (var key in left.Select(kv => kv.Key).Union(right.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
        {
            if (key is "u" or "s" or "omega")
            {
                var xo = left[key] as JsonObject ?? new JsonObject();
                var yo = right[key] as JsonObject ?? new JsonObject();
                foreach (var sub in xo.Select(kv => kv.Key).Union(yo.Select(kv => kv.Key)).Order(StringComparer.Ordinal))
                {
                    var full = key + "." + sub;
                    rows.Add(new RiskIndexParameterChangeDto(GroupOf(full), full, xo[sub]?.ToJsonString(),
                        yo[sub]?.ToJsonString(), !JsonNode.DeepEquals(xo[sub], yo[sub])));
                }

                continue;
            }

            rows.Add(new RiskIndexParameterChangeDto(GroupOf(key), key, left[key]?.ToJsonString(), right[key]?.ToJsonString(),
                !JsonNode.DeepEquals(left[key], right[key])));
        }

        return rows;
    }

    /// <summary>Whether the change set is reproduced by a replay from stored object rows (S39 §9.5).</summary>
    public static bool IsReplayable(IEnumerable<string> changedKeys) =>
        changedKeys.All(k => ReplayableKeys.Contains(k.Split('.')[0]));

    public static bool BandsEqual(IReadOnlyList<RiskIndexBandDefinition> a, IReadOnlyList<RiskIndexBandDefinition> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);

    // ------------------------------------------------------------------------------------------------------------------

    private static void Structural(RiskIndexMode mode, RiskIndexParameters p, List<RiskIndexValidationIssueDto> errors)
    {
        // The full mode reads the Track 9 sources (Gate A floor, Top Risks, P95/CVaR…, S39 §15.1); this version computes
        // only the interim mode, so a full-mode profile has no engine to run it.
        if (mode != RiskIndexMode.Interim)
            errors.Add(Issue("full_mode_prerequisites_missing", null, ("mode", ((int)mode).ToString(CultureInfo.InvariantCulture))));

        foreach (var rule in Scalars)
        {
            var value = rule.Get(p);
            if (!double.IsFinite(value))
            {
                errors.Add(Issue("parameter_out_of_range", rule.Key, ("value", "NaN")));
                continue;
            }

            if (rule.Locked)
            {
                if (Math.Abs(value - rule.Min) > SumTolerance)
                    errors.Add(Issue("locked_parameter", rule.Key, ("value", Format(value)), ("locked", Format(rule.Min))));
                continue;
            }

            if (rule.ZeroAllowed && value == 0) continue;
            if (value < rule.Min - SumTolerance || value > rule.Max + SumTolerance)
                errors.Add(Issue("parameter_out_of_range", rule.Key, ("value", Format(value)), ("min", Format(rule.Min)),
                    ("max", Format(rule.Max))));
        }

        // ω: sums to 1, at least three categories weighted (S39 §12.4).
        var omega = OmegaPairs(p, p).Select(o => o.After).ToList();
        if (Math.Abs(omega.Sum() - 1) > SumTolerance)
            errors.Add(Issue("omega_sum", "omega", ("sum", Format(omega.Sum()))));
        if (omega.Count(w => w > 0) < 3)
            errors.Add(Issue("omega_min_categories", "omega", ("count", omega.Count(w => w > 0).ToString(CultureInfo.InvariantCulture))));

        Bands(p, errors);

        if (p.NoExploitationCeiling >= p.ExploitationFloor)
            errors.Add(Issue("exploitation_order", "teto_sem_exploracao", ("ceiling", Format(p.NoExploitationCeiling)),
                ("floor", Format(p.ExploitationFloor))));

        if (p.QualityMedium >= p.QualityHigh)
            errors.Add(Issue("quality_order", "qualidade_media"));
        if (p.CoverageIndicative >= p.CoveragePoint)
            errors.Add(Issue("coverage_order", "cov_indicativo"));

        // Severity anchors: 1..4 present, increasing, in (0, 1].
        var severity = Enumerable.Range(1, 4).Select(s => p.SeverityAnchors.TryGetValue(s, out var v) ? v : double.NaN).ToList();
        if (severity.Any(v => !double.IsFinite(v) || v <= 0 || v > 1) || severity.Zip(severity.Skip(1)).Any(x => x.Second <= x.First))
            errors.Add(Issue("anchors_not_monotonic", "ancoras_severidade"));

        // SSC anchors: monotone (s falls as the score rises), s(100) = 0 and s(0) = 1.
        var ssc = p.SscAnchors.OrderBy(kv => kv.Key).ToList();
        if (ssc.Any(kv => kv.Key is < 0 or > 100 || !double.IsFinite(kv.Value) || kv.Value is < 0 or > 1) ||
            ssc.Zip(ssc.Skip(1)).Any(x => x.Second.Value > x.First.Value))
            errors.Add(Issue("anchors_not_monotonic", "ancoras_ssc"));
        if (!p.SscAnchors.TryGetValue(100, out var s100) || s100 != 0 || !p.SscAnchors.TryGetValue(0, out var s0) || s0 != 1)
            errors.Add(Issue("ssc_anchor_endpoints", "ancoras_ssc"));

        foreach (var (key, anchors) in new[]
                 {
                     ("ancoras_incidente_categoria", p.IncidentCategoryAnchors),
                     ("ancoras_incidente_severidade", p.IncidentSeverityAnchors)
                 })
            if (anchors.Values.Any(v => !double.IsFinite(v) || v is < 0 or > 1))
                errors.Add(Issue("parameter_out_of_range", key, ("min", "0"), ("max", "1")));

        foreach (var (factor, ceiling) in p.FactorCeilings)
        {
            if (!KnownFactors.Contains(factor)) errors.Add(Issue("unknown_factor", "u." + factor));
            else if (!double.IsFinite(ceiling) || ceiling < 0.5 || ceiling > 1.0)
                errors.Add(Issue("parameter_out_of_range", "u." + factor, ("value", Format(ceiling)), ("min", "0.5"), ("max", "1")));
        }

        foreach (var (category, quotas) in p.PopulationQuotas)
        {
            if (quotas.Values.Any(q => !double.IsFinite(q) || q < 0.2 - SumTolerance || q > 0.8 + SumTolerance))
                errors.Add(Issue("parameter_out_of_range", "cotas_populacao." + category, ("min", "0.2"), ("max", "0.8")));
            if (Math.Abs(quotas.Values.Sum() - 1) > SumTolerance)
                errors.Add(Issue("quota_sum", "cotas_populacao." + category, ("sum", Format(quotas.Values.Sum()))));
        }

        foreach (var source in p.EnabledSources.Where(s => !KnownSources.Contains(s)))
            errors.Add(Issue("unknown_source", "fontes_habilitadas", ("source", source)));

        foreach (var (source, days) in p.FreshnessDays)
        {
            if (!KnownSources.Contains(source)) errors.Add(Issue("unknown_source", "frescor_W", ("source", source)));
            else if (days is < 1 or > 730)
                errors.Add(Issue("parameter_out_of_range", "frescor_W." + source, ("value", Format(days)), ("min", "1"), ("max", "730")));
        }

        if (p.IncidentWindowDays < 3 * p.IncidentHalfLifeDays)
            errors.Add(Issue("incident_window_too_short", "janela_incidente",
                ("minimum", (3 * p.IncidentHalfLifeDays).ToString(CultureInfo.InvariantCulture))));

        // "≥ 2W of the object's slowest source": the sources that read an inventory object (devices, scanners, the CMDB,
        // SecurityScorecard). Assessments date an entity's CTL.A reading, not an inventory signal, so their year is not one.
        var slowest = p.EnabledSources.Where(s => s != RiskIndexSources.Assessments && p.FreshnessDays.ContainsKey(s))
            .Select(s => p.FreshnessDays[s]).DefaultIfEmpty(0).Max();
        if (p.InventorySignalWindowDays < 2 * slowest)
            errors.Add(Issue("inventory_window_too_short", "janela_inventario_sinal",
                ("minimum", (2 * slowest).ToString(CultureInfo.InvariantCulture))));

        var c = p.ReviewCadence;
        int[] cadence = [c.VeryHigh, c.High, c.Medium, c.Low];
        if (cadence.Any(d => d is < 7 or > 365) || cadence.Zip(cadence.Skip(1)).Any(x => x.Second <= x.First))
            errors.Add(Issue("register_copies_incoherent", "cadencia_revisao"));

        var l = p.RegisterLimits;
        double[] limits = [l.Low, l.Medium, l.High, l.VeryHigh];
        if (limits.Any(v => !double.IsFinite(v) || v is < 0 or > 10) || limits.Zip(limits.Skip(1)).Any(x => x.Second <= x.First))
            errors.Add(Issue("register_copies_incoherent", "limites_registro"));

        if (!Enum.IsDefined(p.CadenceBasis))
            errors.Add(Issue("parameter_out_of_range", "base_cadencia"));
    }

    /// <summary>
    /// The bands (S39 §4.5, §12.4): 2 to 5, the first at 0, the rest increasing in [10, 95], both labels filled and unique
    /// per language; and the joint tail lock <c>τ·τ_G·L_top ≥ L_2</c>, so one object alone in the top band can never leave
    /// the headline in the bottom one.
    /// </summary>
    private static void Bands(RiskIndexParameters p, List<RiskIndexValidationIssueDto> errors)
    {
        var bands = p.Bands;
        if (bands.Count is < 2 or > 5)
        {
            errors.Add(Issue("bands_invalid", "faixas", ("reason", "count")));
            return;
        }

        var ok = true;
        if (bands[0].LowerInclusive != 0)
        {
            errors.Add(Issue("bands_invalid", "faixas", ("reason", "first_not_zero")));
            ok = false;
        }

        for (var i = 1; i < bands.Count; i++)
        {
            if (bands[i].LowerInclusive is < 10 or > 95 || bands[i].LowerInclusive <= bands[i - 1].LowerInclusive)
            {
                errors.Add(Issue("bands_invalid", "faixas", ("reason", "not_increasing"),
                    ("index", i.ToString(CultureInfo.InvariantCulture))));
                ok = false;
            }
        }

        if (bands.Any(b => string.IsNullOrWhiteSpace(b.LabelPtBr) || string.IsNullOrWhiteSpace(b.LabelEnUs)))
            errors.Add(Issue("bands_invalid", "faixas", ("reason", "label_missing")));
        if (bands.Select(b => b.LabelPtBr.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bands.Count ||
            bands.Select(b => b.LabelEnUs.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bands.Count)
            errors.Add(Issue("bands_invalid", "faixas", ("reason", "label_not_unique")));

        if (!ok) return;
        var top = bands[^1].LowerInclusive;
        var second = bands[1].LowerInclusive;
        if (p.Tau * p.TauG * top < second - SumTolerance)
            errors.Add(Issue("tail_lock", "tau_g", ("product", Format(p.Tau * p.TauG * top)),
                ("minimum", second.ToString(CultureInfo.InvariantCulture))));
    }

    private static IEnumerable<(string Key, double Before, double After)> OmegaPairs(RiskIndexParameters before,
        RiskIndexParameters after)
    {
        yield return ("omega.REG", before.Omega.Reg, after.Omega.Reg);
        yield return ("omega.EXP", before.Omega.Exp, after.Omega.Exp);
        yield return ("omega.AME", before.Omega.Ame, after.Omega.Ame);
        yield return ("omega.CTL", before.Omega.Ctl, after.Omega.Ctl);
        yield return ("omega.GOV", before.Omega.Gov, after.Omega.Gov);
        yield return ("omega.TER", before.Omega.Ter, after.Omega.Ter);
    }

    private static JsonObject TopLevel(RiskIndexParameters p) =>
        JsonNode.Parse(RiskIndexParametersJson.Serialize(p))!.AsObject();

    private static RiskIndexValidationIssueDto Issue(string code, string? key, params (string Name, string Value)[] args) =>
        new(code, key, args.ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal));

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
