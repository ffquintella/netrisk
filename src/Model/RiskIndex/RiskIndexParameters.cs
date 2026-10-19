using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Model.RiskIndex;

/// <summary>
/// The typed mirror of a profile's <c>parameters</c> document (S39 §12.2; S40 §8.4): one property per key, serialized
/// under the key's own name, with the "Equilibrado" defaults. <c>new RiskIndexParameters()</c> is the default profile.
/// Ranges, locks and pace rules are not checked here; that is the profile rules' job (T250, S39 §12.4).
/// </summary>
public sealed record RiskIndexParameters
{
    // ---------- group 7 and group 1: bands, categories and weights ----------

    /// <summary><c>faixas</c>: inclusive lower bound of each communication band on the displayed integer, labelled in pt-BR and en-US.</summary>
    [JsonPropertyName("faixas")]
    public IReadOnlyList<RiskIndexBandDefinition> Bands { get; init; } =
    [
        new() { LabelPtBr = "Baixo", LabelEnUs = "Low", LowerInclusive = 0 },
        new() { LabelPtBr = "Médio", LabelEnUs = "Medium", LowerInclusive = 31 },
        new() { LabelPtBr = "Alto", LabelEnUs = "High", LowerInclusive = 70 }
    ];

    /// <summary><c>omega</c>: the category weights, summing to 1.</summary>
    [JsonPropertyName("omega")]
    public RiskIndexCategoryWeights Omega { get; init; } = new();

    /// <summary><c>u</c>: the ceiling of each factor (<see cref="RiskIndexFactors"/>).</summary>
    [JsonPropertyName("u")]
    public IReadOnlyDictionary<string, double> FactorCeilings { get; init; } = RiskIndexFactors.DefaultCeilings();

    /// <summary><c>cotas_populacao</c>: per mixed category (GOV, CTL), each population's share, by <c>RiskIndexPopulation</c> name.</summary>
    [JsonPropertyName("cotas_populacao")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PopulationQuotas { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["GOV"] = new Dictionary<string, double> { ["Scenarios"] = 0.5, ["Assets"] = 0.5 },
            ["CTL"] = new Dictionary<string, double> { ["Assessments"] = 0.5, ["Scenarios"] = 0.5 }
        };

    // ---------- group 2: aggregation ----------

    /// <summary><c>p</c>: the power-mean exponent across objects.</summary>
    [JsonPropertyName("p")]
    public int P { get; init; } = 4;

    /// <summary><c>tau</c>: the category tail floor.</summary>
    [JsonPropertyName("tau")]
    public double Tau { get; init; } = 0.80;

    /// <summary><c>tau_g</c>: the headline tail floor.</summary>
    [JsonPropertyName("tau_g")]
    public double TauG { get; init; } = 0.70;

    /// <summary><c>rho</c>: the damped remainder of max-plus-damped-remainder.</summary>
    [JsonPropertyName("rho")]
    public double Rho { get; init; } = 0.30;

    /// <summary><c>k_sinais</c>: signals the MRA considers (50 are stored).</summary>
    [JsonPropertyName("k_sinais")]
    public int KSignals { get; init; } = 10;

    // ---------- group 3: criticality ----------

    /// <summary><c>m_a</c>: the floor of <c>m(crit) = a + (1 − a)(crit − 1)/4</c>.</summary>
    [JsonPropertyName("m_a")]
    public double MA { get; init; } = 0.40;

    [JsonPropertyName("crit_padrao")]
    public int DefaultCriticality { get; init; } = 3;

    [JsonPropertyName("crit_padrao_nao_producao")]
    public int DefaultNonProductionCriticality { get; init; } = 2;

    [JsonPropertyName("crit_max_vision_one")]
    public int VisionOneCriticalityCeiling { get; init; } = 4;

    /// <summary><c>ambientes_nao_producao</c>: the controlled vocabulary of <c>hosts.environment</c>.</summary>
    [JsonPropertyName("ambientes_nao_producao")]
    public IReadOnlyList<string> NonProductionEnvironments { get; init; } = ["dev", "test", "homolog", "lab"];

    [JsonPropertyName("crit_ext")]
    public int ExternalCriticality { get; init; } = 4;

    [JsonPropertyName("passo_classificacao")]
    public int ClassificationStep { get; init; } = 1;

    /// <summary><c>niveis_sensiveis</c>: ids of <c>securityClassificationLevel</c> that add the step.</summary>
    [JsonPropertyName("niveis_sensiveis")]
    public IReadOnlyList<int> SensitiveLevels { get; init; } = [];

    [JsonPropertyName("beta_rank")]
    public double BetaRank { get; init; } = 0.30;

    [JsonPropertyName("validade_rank_ciclos")]
    public int RankValidityCycles { get; init; } = 2;

    // ---------- group 4: normalization and anchors ----------

    /// <summary><c>ancoras_severidade</c>: finding base without VPR/CVSS, by severity 1–4.</summary>
    [JsonPropertyName("ancoras_severidade")]
    public IReadOnlyDictionary<int, double> SeverityAnchors { get; init; } =
        new Dictionary<int, double> { [1] = 0.20, [2] = 0.50, [3] = 0.75, [4] = 0.95 };

    [JsonPropertyName("m_x_disponivel")]
    public double ExploitAvailableMultiplier { get; init; } = 1.15;

    [JsonPropertyName("teto_sem_exploracao")]
    public double NoExploitationCeiling { get; init; } = 0.90;

    [JsonPropertyName("piso_exploracao")]
    public double ExploitationFloor { get; init; } = 0.95;

    [JsonPropertyName("epss_limiar")]
    public double EpssThreshold { get; init; } = 0.10;

    [JsonPropertyName("m_net")]
    public double InternetMultiplier { get; init; } = 1.2;

    [JsonPropertyName("heuristica_ip_publico")]
    public bool PublicIpHeuristic { get; init; }

    /// <summary><c>faixas_publicas_internas</c>: public CIDR ranges used internally, excluded from the heuristic.</summary>
    [JsonPropertyName("faixas_publicas_internas")]
    public IReadOnlyList<string> InternalPublicRanges { get; init; } = [];

    /// <summary><c>ancoras_ssc</c>: SecurityScorecard score → <c>s</c>, piecewise linear.</summary>
    [JsonPropertyName("ancoras_ssc")]
    public IReadOnlyDictionary<int, double> SscAnchors { get; init; } = new Dictionary<int, double>
    {
        [100] = 0.00, [90] = 0.10, [80] = 0.35, [70] = 0.55, [60] = 0.75, [50] = 0.90, [0] = 1.00
    };

    /// <summary><c>ancoras_incidente_categoria</c>: incident category → <c>s</c>.</summary>
    [JsonPropertyName("ancoras_incidente_categoria")]
    public IReadOnlyDictionary<string, double> IncidentCategoryAnchors { get; init; } = new Dictionary<string, double>
    {
        ["data_breach"] = 0.90,
        ["unauthorized_access"] = 0.75, ["malware_infection"] = 0.75, ["insider_threats"] = 0.75,
        ["denial_of_service"] = 0.60,
        ["phishing"] = 0.50, ["other_social_engineering"] = 0.50, ["not_specified"] = 0.50,
        ["other"] = 0.40
    };

    /// <summary><c>ancoras_incidente_severidade</c>: incident severity → <c>s</c>, replacing the category's when present.</summary>
    [JsonPropertyName("ancoras_incidente_severidade")]
    public IReadOnlyDictionary<string, double> IncidentSeverityAnchors { get; init; } = new Dictionary<string, double>
    {
        ["critical"] = 1.00, ["high"] = 0.80, ["medium"] = 0.50, ["low"] = 0.20
    };

    /// <summary><c>fator_quase_incidente</c>: <c>u_AME.Q</c>.</summary>
    [JsonPropertyName("fator_quase_incidente")]
    public double NearMissFactor { get; init; } = 0.5;

    [JsonPropertyName("irp_limiar_sigma")]
    public double IrpSigmaThreshold { get; init; } = 0.70;

    [JsonPropertyName("irp_rampa")]
    public double IrpRamp { get; init; } = 0.10;

    /// <summary><c>irp_teste_validade</c>, days.</summary>
    [JsonPropertyName("irp_teste_validade")]
    public int IrpTestValidityDays { get; init; } = 365;

    // ---------- group 5: time and freshness ----------

    /// <summary><c>fontes_habilitadas</c>: the S39 §3.2 sources feeding the categories (<see cref="RiskIndexSources"/>).</summary>
    [JsonPropertyName("fontes_habilitadas")]
    public IReadOnlyList<string> EnabledSources { get; init; } = RiskIndexSources.DefaultEnabled();

    /// <summary><c>frescor_W</c>: validity window per source, days.</summary>
    [JsonPropertyName("frescor_W")]
    public IReadOnlyDictionary<string, int> FreshnessDays { get; init; } = RiskIndexSources.DefaultFreshnessDays();

    [JsonPropertyName("pi_prior")]
    public double PiPrior { get; init; } = 0.5;

    /// <summary><c>meia_vida_incidente</c>, days.</summary>
    [JsonPropertyName("meia_vida_incidente")]
    public int IncidentHalfLifeDays { get; init; } = 30;

    /// <summary><c>janela_incidente</c>, days.</summary>
    [JsonPropertyName("janela_incidente")]
    public int IncidentWindowDays { get; init; } = 90;

    /// <summary><c>janela_intake_incidentes</c>, days.</summary>
    [JsonPropertyName("janela_intake_incidentes")]
    public int IncidentIntakeWindowDays { get; init; } = 90;

    /// <summary><c>janela_inventario_sinal</c>, days.</summary>
    [JsonPropertyName("janela_inventario_sinal")]
    public int InventorySignalWindowDays { get; init; } = 400;

    /// <summary>
    /// <c>tipos_avaliaveis</c>: entity types in CTL.A's inventory. Its default — "types with a Submitted run" — depends on
    /// the organization, so the draft that creates a profile fills it (T250); the typed default is empty.
    /// </summary>
    [JsonPropertyName("tipos_avaliaveis")]
    public IReadOnlyList<string> AssessableTypes { get; init; } = [];

    // ---------- group 6: reviews and governance ----------

    [JsonPropertyName("lambda_credito")]
    public double CreditLambda { get; init; } = 1.0;

    [JsonPropertyName("credito_minimo")]
    public double MinimumCredit { get; init; }

    /// <summary><c>cadencia_revisao</c>: the approved copy of <c>review_levels</c>, days.</summary>
    [JsonPropertyName("cadencia_revisao")]
    public RiskIndexReviewCadence ReviewCadence { get; init; } = new();

    /// <summary><c>limites_registro</c>: the approved copy of <c>risk_levels</c>, with a reachable Very High (D-02).</summary>
    [JsonPropertyName("limites_registro")]
    public RiskIndexRegisterLimits RegisterLimits { get; init; } = new();

    /// <summary><c>base_cadencia</c>: the approved copy of <c>next_review_date_uses</c>.</summary>
    [JsonPropertyName("base_cadencia")]
    [JsonConverter(typeof(JsonStringEnumConverter<RiskIndexCadenceBasis>))]
    public RiskIndexCadenceBasis CadenceBasis { get; init; } = RiskIndexCadenceBasis.InherentRisk;

    /// <summary><c>janela_revisao_derivada</c>, seconds.</summary>
    [JsonPropertyName("janela_revisao_derivada")]
    public int DerivedReviewWindowSeconds { get; init; } = 60;

    /// <summary><c>s</c>: the signal values of GOV.A, GOV.C, GOV.R and GOV.G.</summary>
    [JsonPropertyName("s")]
    public RiskIndexGovernanceSignals Signals { get; init; } = new();

    /// <summary><c>carencia_aceite</c>, days.</summary>
    [JsonPropertyName("carencia_aceite")]
    public int AcceptanceGraceDays { get; init; } = 7;

    [JsonPropertyName("acc_drift_inicio")]
    public double AcceptanceDriftStart { get; init; } = 0.5;

    [JsonPropertyName("acc_drift_amplitude")]
    public double AcceptanceDriftAmplitude { get; init; } = 1.5;

    /// <summary><c>prazo_contra_assinatura</c>, days.</summary>
    [JsonPropertyName("prazo_contra_assinatura")]
    public int CounterSignatureDays { get; init; } = 7;

    /// <summary><c>prazo_seguimento_decisao</c>, days.</summary>
    [JsonPropertyName("prazo_seguimento_decisao")]
    public int DecisionFollowUpDays { get; init; } = 30;

    /// <summary><c>prazo_ref_tarefa</c>, days.</summary>
    [JsonPropertyName("prazo_ref_tarefa")]
    public int TaskReferenceDays { get; init; } = 30;

    /// <summary><c>prazo_revisao_solicitada</c>, days.</summary>
    [JsonPropertyName("prazo_revisao_solicitada")]
    public int RequestedReviewDays { get; init; } = 14;

    [JsonPropertyName("delta_div")]
    public double DivergenceDelta { get; init; } = 0.3;

    /// <summary><c>janela_aviso_expiracao</c>, days; display only.</summary>
    [JsonPropertyName("janela_aviso_expiracao")]
    public int ExpiryWarningDays { get; init; } = 30;

    /// <summary><c>desconto_aceite</c>: locked at 0 — an acceptance is not mitigation (S39 I10).</summary>
    [JsonPropertyName("desconto_aceite")]
    public double AcceptanceDiscount { get; init; }

    // ---------- group 7 and group 8: floors, display and coverage ----------

    /// <summary><c>piso_portao_a</c>: φ_A, never switched off.</summary>
    [JsonPropertyName("piso_portao_a")]
    public int GateAFloor { get; init; } = 70;

    [JsonPropertyName("cov_ponto")]
    public double CoveragePoint { get; init; } = 0.75;

    [JsonPropertyName("cov_indicativo")]
    public double CoverageIndicative { get; init; } = 0.40;

    [JsonPropertyName("qualidade_alta")]
    public double QualityHigh { get; init; } = 0.75;

    [JsonPropertyName("qualidade_media")]
    public double QualityMedium { get; init; } = 0.50;

    [JsonPropertyName("limiar_nao_atribuido")]
    public double UnassignedThreshold { get; init; } = 0.10;

    [JsonPropertyName("cobertura_quantificacao_min")]
    public double MinimumQuantificationCoverage { get; init; } = 0.80;

    [JsonPropertyName("ewma_n")]
    public int EwmaN { get; init; } = 7;

    [JsonPropertyName("delta_seta")]
    public int ArrowDelta { get; init; } = 3;

    [JsonPropertyName("delta_detalhe")]
    public double DetailDelta { get; init; } = 1;

    [JsonPropertyName("top_contribuicoes")]
    public int TopContributions { get; init; } = 50;

    [JsonPropertyName("passos_atribuicao")]
    public int AttributionSteps { get; init; } = 64;

    [JsonPropertyName("residuo_max")]
    public double MaximumResidual { get; init; } = 0.05;
}

/// <summary>A communication band: its inclusive lower bound on the displayed integer, and its two labels.</summary>
public sealed record RiskIndexBandDefinition
{
    [JsonPropertyName("rotulo_pt_br")]
    public string LabelPtBr { get; init; } = "";

    [JsonPropertyName("rotulo_en_us")]
    public string LabelEnUs { get; init; } = "";

    [JsonPropertyName("limite_inferior")]
    public int LowerInclusive { get; init; }
}

/// <summary>The category weights ω (S39 §2.3). TER stays 0 until M48.</summary>
public sealed record RiskIndexCategoryWeights
{
    [JsonPropertyName("REG")]
    public double Reg { get; init; } = 0.30;

    [JsonPropertyName("EXP")]
    public double Exp { get; init; } = 0.30;

    [JsonPropertyName("AME")]
    public double Ame { get; init; } = 0.15;

    [JsonPropertyName("CTL")]
    public double Ctl { get; init; } = 0.10;

    [JsonPropertyName("GOV")]
    public double Gov { get; init; } = 0.15;

    [JsonPropertyName("TER")]
    public double Ter { get; init; }
}

/// <summary>The approved review cadence per register level, in days (copy of <c>review_levels</c>).</summary>
public sealed record RiskIndexReviewCadence
{
    [JsonPropertyName("very_high")]
    public int VeryHigh { get; init; } = 30;

    [JsonPropertyName("high")]
    public int High { get; init; } = 90;

    [JsonPropertyName("medium")]
    public int Medium { get; init; } = 120;

    [JsonPropertyName("low")]
    public int Low { get; init; } = 240;
}

/// <summary>The approved lower bounds of the register levels on the 0–10 score (copy of <c>risk_levels</c>).</summary>
public sealed record RiskIndexRegisterLimits
{
    [JsonPropertyName("low")]
    public double Low { get; init; }

    [JsonPropertyName("medium")]
    public double Medium { get; init; } = 4.0;

    [JsonPropertyName("high")]
    public double High { get; init; } = 7.0;

    [JsonPropertyName("very_high")]
    public double VeryHigh { get; init; } = 9.0;
}

/// <summary>The governance signal values <c>s.*</c> (S39 §6.3, §12.2).</summary>
public sealed record RiskIndexGovernanceSignals
{
    /// <summary>Above the appetite ceiling with no valid acceptance and no treatment.</summary>
    [JsonPropertyName("APT_BRK")]
    public double AppetiteBreach { get; init; } = 1.0;

    /// <summary>Above the appetite ceiling with no valid acceptance, under treatment.</summary>
    [JsonPropertyName("APT_BRK_TREATED")]
    public double AppetiteBreachTreated { get; init; } = 0.5;

    [JsonPropertyName("ACC_APT")]
    public double AcceptanceBeforeAppetite { get; init; } = 0.5;

    [JsonPropertyName("ACC_EXP")]
    public double AcceptanceExpired { get; init; } = 1.0;

    [JsonPropertyName("ACC_REV")]
    public double AcceptanceRevoked { get; init; } = 0.5;

    [JsonPropertyName("APT_DUAL")]
    public double CounterSignaturePending { get; init; } = 0.5;

    [JsonPropertyName("CMP_PEND")]
    public double CampaignPending { get; init; } = 1.0;

    [JsonPropertyName("CMP_ESC")]
    public double CampaignEscalated { get; init; } = 0.7;

    [JsonPropertyName("CMP_MIT")]
    public double CampaignMitigate { get; init; } = 0.5;

    [JsonPropertyName("REV_REQ")]
    public double ReviewRequested { get; init; } = 1.0;

    [JsonPropertyName("DIV")]
    public double Divergence { get; init; } = 0.8;
}

/// <summary>Which register value the cadence is measured on (<c>next_review_date_uses</c>).</summary>
public enum RiskIndexCadenceBasis
{
    InherentRisk = 1,
    ResidualRisk = 2
}

/// <summary>
/// The one serializer of <c>risk_index_profiles.parameters</c>. Strict: an unknown key is refused rather than dropped,
/// because an approved profile must mean exactly what the committee read; a missing key reads as its default.
/// </summary>
public static class RiskIndexParametersJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        WriteIndented = false
    };

    public static string Serialize(RiskIndexParameters parameters) => JsonSerializer.Serialize(parameters, Options);

    /// <exception cref="JsonException">The document is not a parameters object, or names a key the profile does not have.</exception>
    public static RiskIndexParameters Deserialize(string json) =>
        JsonSerializer.Deserialize<RiskIndexParameters>(json, Options)
        ?? throw new JsonException("The parameters document is null.");
}
