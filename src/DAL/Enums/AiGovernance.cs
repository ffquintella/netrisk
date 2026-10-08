namespace DAL.Enums;

/// <summary>
/// What kind of model an inventoried AI component is (Stage 9.12, S53 §4.1). Stored as <c>ai_models.kind</c> (int, CHECK
/// 1–7). Informative: the metrics a model is expected to report follow its declared risk tier, not its kind (S53 D6).
/// </summary>
public enum AiModelKind
{
    /// <summary>Classifies or scores cases (triage, fraud, plagiarism, admission scoring).</summary>
    Classification = 1,

    /// <summary>Predicts a quantity or a trend (demand, dropout, KRI forecasting).</summary>
    Forecasting = 2,

    /// <summary>Generates text, code or images (an LLM, a chatbot, a summariser).</summary>
    Generative = 3,

    /// <summary>Recommends or ranks items for a person.</summary>
    Recommendation = 4,

    /// <summary>Flags anomalies in events or behaviour.</summary>
    AnomalyDetection = 5,

    /// <summary>Recognises a person from a biometric trait (face, voice, fingerprint).</summary>
    Biometric = 6,

    Other = 7
}

/// <summary>Where the model comes from (S53 §4.1). Stored as <c>ai_models.source</c> (int, CHECK 1–3).</summary>
public enum AiModelSource
{
    /// <summary>Built and trained by the organization.</summary>
    InHouse = 1,

    /// <summary>Supplied by a vendor — the vendor is a registered third party (Stage 9.10), or the model has a finding.</summary>
    Vendor = 2,

    /// <summary>Open source or open weights, run by the organization.</summary>
    OpenSource = 3
}

/// <summary>
/// Where an inventoried model stands (S53 §4.1). Stored as <c>ai_models.status</c> (int, CHECK 1–4).
///
/// <see cref="Pilot"/> and <see cref="Production"/> are "in use": the evaluation and register findings apply to them. A
/// model that is not <see cref="Retired"/> derives flag 11 on the risks linked to it (S53 §4.6). A model is never deleted —
/// it is retired with a reason, and the inventory keeps it as evidence (S53 D9).
/// </summary>
public enum AiModelStatus
{
    /// <summary>Under evaluation before any use — the governance instrument comes before the use (S27).</summary>
    Proposed = 1,

    /// <summary>In limited use.</summary>
    Pilot = 2,

    /// <summary>In use.</summary>
    Production = 3,

    /// <summary>Out of use; frozen, kept as evidence.</summary>
    Retired = 4
}

/// <summary>
/// The risk tier the organization declares for a model — the "evaluation proportional to the risk" of MIGR-TI/IA Phase 6
/// (S53 §4.3, D6). Stored as <c>ai_models.risk_tier</c> (int, CHECK 1–3); NULL is "not declared", a finding, and is read
/// as <see cref="High"/> for what the model must report — absent is never lenient (S53 D5).
/// </summary>
public enum AiModelRiskTier
{
    /// <summary>No effect on a person's rights or on a material decision.</summary>
    Minimal = 1,

    /// <summary>Supports a decision a person makes, with limited effect.</summary>
    Limited = 2,

    /// <summary>Affects people's rights, access, assessment or safety, or a material decision of the organization.</summary>
    High = 3
}

/// <summary>
/// How people oversee the model's outputs (S53 §4.1). Stored as <c>ai_models.human_oversight</c> (int, CHECK 1–3); NULL is
/// "not declared", a finding. The human override rate measures this oversight, so it is expected whenever a person reviews
/// outputs — or when nobody declared whether one does (S53 §4.3).
/// </summary>
public enum AiHumanOversight
{
    /// <summary>A person reviews every output before it takes effect.</summary>
    EveryOutput = 1,

    /// <summary>A person reviews a sample of the outputs.</summary>
    Sampled = 2,

    /// <summary>Nobody reviews the outputs: the model acts on its own.</summary>
    NoReview = 3
}

/// <summary>
/// How a model uses a data record of the entity map (S53 §4.2). Stored as <c>ai_model_data_links.data_usage</c> (int, CHECK
/// 1–5). The record is the <c>organizationData</c> node the Stage 9.11 catalogue is keyed by, so its catalogue is read
/// through the node — no column is added to any link (S53 D4).
/// </summary>
public enum AiModelDataUsage
{
    Training = 1,
    FineTuning = 2,
    Evaluation = 3,

    /// <summary>Read by the model when it runs (inference input, retrieval corpus).</summary>
    Input = 4,

    /// <summary>Produced by the model.</summary>
    Output = 5
}

/// <summary>
/// The model metrics of MIGR-TI/IA Phase 6 and Phase 7 (S53 §4.4, T214). Stored as <c>ai_model_metric_readings.metric</c>
/// (int, CHECK 1–6).
///
/// "Precisão" reads as accuracy in T214 and as precision in the methodology panel's M10; both are recorded, because
/// precision pairs with recall and accuracy does not replace it (S53 D7). Every value is a fraction in [0, 1] except drift,
/// a non-negative statistic (PSI, KL divergence…) the reading's method names.
/// </summary>
public enum AiModelMetric
{
    Accuracy = 1,
    Precision = 2,
    Recall = 3,

    /// <summary>A calibration error (ECE, Brier score) — lower is better.</summary>
    Calibration = 4,

    /// <summary>A drift statistic over a window — lower is better.</summary>
    Drift = 5,

    /// <summary>
    /// The share of reviewed outputs a person overrode — computed by the server from the overrides recorded with author and
    /// reason, never typed (S53 D8).
    /// </summary>
    HumanOverrideRate = 6
}
