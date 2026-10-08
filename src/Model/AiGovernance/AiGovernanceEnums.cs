namespace Model.AiGovernance;

/// <summary>
/// The state of one metric of a model, for the model's current version (Stage 9.12, S53 §4.4), computed on read and never
/// stored. <b>Not evaluated is explicit</b>: a metric with no live reading for the current version has no value — never 0,
/// never a pass (T215). Computed enums start at 1 (S43 D17).
/// </summary>
public enum AiMetricState
{
    /// <summary>No live reading of this metric for the model's current version.</summary>
    NotEvaluated = 1,

    /// <summary>The latest live reading for the current version is within the model's maximum evaluation age.</summary>
    Evaluated = 2,

    /// <summary>The latest live reading for the current version is older than the model's maximum evaluation age.</summary>
    Stale = 3
}

/// <summary>
/// Where the evaluation of a model's current version stands, over the metrics its risk tier and oversight require (S53
/// §4.3). Computed on read. A model with no recorded evaluation is <see cref="NotEvaluated"/> — never evaluated by omission
/// (T215): the required set is never empty, because drift is required of every model.
/// </summary>
public enum AiModelEvaluationState
{
    /// <summary>No required metric has a live reading for the current version.</summary>
    NotEvaluated = 1,

    /// <summary>Some required metric has a reading and some has none.</summary>
    Incomplete = 2,

    /// <summary>Every required metric has a reading, and some is older than the maximum evaluation age.</summary>
    Stale = 3,

    /// <summary>Every required metric has a current reading for the current version.</summary>
    Evaluated = 4
}

/// <summary>
/// What the inventory record of a model is missing, or does not hold (S53 §4.7), computed on read and never stored. A
/// finding is a signal: nothing is refused because of one. Absent is a finding of its own, never read as compliant (S53 D5).
/// A retired model has none.
/// </summary>
public enum AiModelFindingCode
{
    /// <summary>No accountable owner — the methodology's "human roles, never AI" needs a person.</summary>
    OwnerMissing = 1,

    /// <summary>The risk tier is not declared; the model is held to what a high-tier model reports.</summary>
    RiskTierUndeclared = 2,

    /// <summary>Whether people review its outputs is not declared.</summary>
    HumanOversightUndeclared = 3,

    /// <summary>A high-tier (or undeclared) model whose outputs nobody reviews — MIGR-TI/IA allows AI with a human in the loop.</summary>
    HumanOversightAbsent = 4,

    /// <summary>A vendor model whose vendor is not a registered third party.</summary>
    VendorUnregistered = 5,

    /// <summary>The data the model uses was never declared — not even as none.</summary>
    DataUndeclared = 6,

    /// <summary>A data record the model uses has no LGPD catalogue (Stage 9.11).</summary>
    DataNotCatalogued = 7,

    /// <summary>A model in use with no risk of the register linked to it.</summary>
    NoRiskRegistered = 8,

    /// <summary>A model in use with no required metric evaluated for its current version (T215).</summary>
    NotEvaluated = 9,

    /// <summary>A model in use with some required metric not evaluated for its current version.</summary>
    EvaluationIncomplete = 10,

    /// <summary>A model in use with some required metric's latest reading older than the maximum evaluation age.</summary>
    EvaluationStale = 11
}
