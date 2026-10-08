namespace Model.Monitoring;

/// <summary>
/// The state of a key risk indicator, computed from its latest valid reading (Stage 9.8, S49 §4.4). Computed enums
/// start at 1 (S43 D17).
/// </summary>
public enum KriState
{
    /// <summary>No valid reading at all. Not assessable — never within tolerance.</summary>
    NoReading = 1,

    /// <summary>
    /// The latest valid reading is older than the KRI's maximum reading age, whatever its value. Not within tolerance —
    /// "false comfort is the characteristic defect of an indicator panel" (S27, Stage 9.8).
    /// </summary>
    Stale = 2,

    /// <summary>The latest reading is current and on the good side of the warning (or there is no warning).</summary>
    WithinTolerance = 3,

    /// <summary>The latest reading is current, past the warning threshold and not beyond the tolerance.</summary>
    Warning = 4,

    /// <summary>The latest reading is current and strictly beyond the tolerance.</summary>
    Breached = 5,

    /// <summary>The KRI is retired: not evaluated, gates nothing.</summary>
    Retired = 6
}

/// <summary>The outcome of Gate B by indicator (S49 §4.8), mirroring Gate B on the tail.</summary>
public enum IndicatorAppetiteState
{
    /// <summary>No active KRI is linked to the risk: no indicator gates it.</summary>
    NotConfigured = 1,

    /// <summary>A linked KRI has no reading or is stale, and none is beyond its tolerance. Never read as within.</summary>
    NotAssessable = 2,

    /// <summary>Every linked KRI is current and within its tolerance (or in warning).</summary>
    WithinTolerance = 3,

    /// <summary>A linked KRI is beyond its tolerance — or stale with its last reading beyond it (S49 D4): treat or escalate.</summary>
    ExceedsTolerance = 4
}

/// <summary>Why Gate B by indicator could not be assessed (S49 §4.8).</summary>
public enum IndicatorNotAssessableReason
{
    /// <summary>A linked KRI has never had a valid reading.</summary>
    NoReading = 1,

    /// <summary>A linked KRI's latest reading is older than its maximum reading age.</summary>
    Stale = 2
}

/// <summary>Where a reassessment trigger stands on its risk (S49 §4.6). Computed on read.</summary>
public enum ReassessmentTriggerState
{
    /// <summary>No management review of the risk since the trigger was raised.</summary>
    Pending = 1,

    /// <summary>A management review of the risk was submitted at or after the trigger was raised.</summary>
    Answered = 2,

    /// <summary>The risk was closed without that review.</summary>
    RiskClosed = 3
}

/// <summary>Whether a methodology metric can be computed today (S49 §3.3, §4.9).</summary>
public enum MetricAvailability
{
    /// <summary>Computed in full from data that exists.</summary>
    Available = 1,

    /// <summary>Computed for part of what the methodology asks; the detail says which part is missing.</summary>
    Partial = 2,

    /// <summary>Not computable — its source does not exist yet (the stage that delivers it is named), or it failed.</summary>
    NotAvailable = 3
}

/// <summary>The ten metrics of MIGR-TI/IA Phase 7, in the methodology's order (S49 §3.3).</summary>
public enum MethodologyMetric
{
    /// <summary>M1 — coverage of critical processes and discovered assets.</summary>
    CriticalProcessCoverage = 1,

    /// <summary>M2 — % of risks with an owner and evidence.</summary>
    OwnerAndEvidence = 2,

    /// <summary>M3 — mean time from discovery to decision.</summary>
    DiscoveryToDecision = 3,

    /// <summary>M4 — aggregate exposure above appetite (E[L] and P95).</summary>
    AggregateExposure = 4,

    /// <summary>M5 — control effectiveness and residual by domain.</summary>
    ControlEffectiveness = 5,

    /// <summary>M6 — time to remediate KEV items.</summary>
    KevRemediation = 6,

    /// <summary>M7 — restoration tested against the declared RTO/RPO.</summary>
    RestorationVerification = 7,

    /// <summary>M8 — concentration in third parties.</summary>
    ThirdPartyConcentration = 8,

    /// <summary>M9 — reopened risks, unforeseen incidents and false negatives.</summary>
    ReopenedAndUnforeseen = 9,

    /// <summary>M10 — AI: precision, recall, calibration, drift, human override rate.</summary>
    ArtificialIntelligence = 10
}
