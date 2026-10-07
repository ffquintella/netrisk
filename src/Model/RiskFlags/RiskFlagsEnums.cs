namespace Model.RiskFlags;

/// <summary>Where a flag's derived half comes from in Stage 9.5 (S46 §4.1). Computed enums start at 1 (S43 D17).</summary>
public enum RiskFlagDerivation
{
    /// <summary>Declared by an assessor only — nothing in the system can derive it yet.</summary>
    None = 1,

    /// <summary>The CISA KEV catalogue (Stage 9.4, M42) over the open findings linked to the risk.</summary>
    Kev = 2,

    /// <summary>The business impact analysis (Stage 9.3, M41) over the critical processes the risk reaches.</summary>
    Bia = 3,

    /// <summary>The security classification of the data the risk is linked to.</summary>
    DataClassification = 4
}

/// <summary>The decisions Gate A is evaluated for (S46 §4.7).</summary>
public enum GateAAction
{
    Accept = 1,
    RenewAcceptance = 2,
    Close = 3,
    Delete = 4,

    /// <summary>Recording a Phase 4 decision other than "act immediately".</summary>
    Decide = 5
}

/// <summary>The direction of a risk's score over the trend window (S46 §4.9).</summary>
public enum RiskTrendDirection
{
    Unknown = 1,
    Falling = 2,
    Stable = 3,
    Rising = 4
}

/// <summary>What the next decision on a Top Risks row is (S46 §4.9).</summary>
public enum NextDecisionKind
{
    /// <summary>A decision is on record and nothing is scheduled.</summary>
    None = 1,

    /// <summary>Gate A holds: the decision is due now, and overdue since the onset.</summary>
    GateAEscalation = 2,

    /// <summary>The live acceptance expires.</summary>
    AcceptanceExpiry = 3,

    /// <summary>The next management review the latest review scheduled.</summary>
    ManagementReview = 4,

    /// <summary>The earliest open treatment task falls due.</summary>
    MitigationTaskDue = 5,

    /// <summary>No decision has ever been recorded and nothing is scheduled.</summary>
    DecisionPending = 6
}
