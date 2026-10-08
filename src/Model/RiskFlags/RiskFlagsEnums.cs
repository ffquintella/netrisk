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

    /// <summary>
    /// The classification of the data the risk is linked to: the security classification level marked sensitive (Stage
    /// 9.5), or the LGPD data catalogue's marking (Stage 9.11, S52 §4.8).
    /// </summary>
    DataClassification = 4,

    /// <summary>
    /// The tail statistics of the risk's inherent Monte Carlo run (Stage 9.7, S48 §4.8): a low annual probability of
    /// loss and a catastrophic mean loss of a loss year.
    /// </summary>
    TailStatistics = 5,

    /// <summary>
    /// The AI model inventory (Stage 9.12, S53 §4.6): the risk is linked to an inventoried model that is not retired — an AI
    /// component's risk in the same register.
    /// </summary>
    AiModelInventory = 6
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
