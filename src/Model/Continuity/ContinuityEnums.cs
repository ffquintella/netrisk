namespace Model.Continuity;

/// <summary>The two recovery objectives a restoration test verifies (Stage 9.3, S43 §4.5).</summary>
public enum ContinuityObjective
{
    Rto = 1,
    Rpo = 2
}

/// <summary>
/// The verification state of one declared objective (S43 §4.6). No value 0: a DTO nobody filled in
/// does not read as a valid state (S43 §11, D17).
/// </summary>
public enum ObjectiveVerificationStatus
{
    /// <summary>The objective is not declared. Never RTO 0 and never infinite.</summary>
    Absent = 1,

    /// <summary>Declared, with no valid test behind it. Never "met".</summary>
    Unverified = 2,

    Met = 3,

    NotMet = 4
}

/// <summary>Why an objective is <see cref="ObjectiveVerificationStatus.Unverified"/> or
/// <see cref="ObjectiveVerificationStatus.NotMet"/>.</summary>
public enum VerificationReason
{
    NoTest = 1,
    Stale = 2,
    NotMeasured = 3,
    RestorationFailed = 4,
    Exceeded = 5
}

/// <summary>Where a process's effective criticality comes from (S43 §4.6). Null means it has none.</summary>
public enum CriticalitySource
{
    /// <summary>Derived from the BIA's MTPD.</summary>
    Bia = 1,

    /// <summary>The <c>criticality</c> entity property — "declared, not BIA".</summary>
    Declared = 2
}

/// <summary>One kind of threat to a node's RTO or RPO, its own or inherited from a provider (S43 §4.6).</summary>
public enum ContinuityThreatReason
{
    NotMet = 1,
    Unverified = 2,
    CascadeConflict = 3,
    RequirementWithoutObjective = 4,
    ProviderNotMet = 5,
    ProviderUnverified = 6,
    ProviderExceedsRequirement = 7,
    ProviderObjectiveAbsent = 8
}

/// <summary>
/// The weight class of a threat item: a <see cref="Confirmed"/> threat weighs 1.0, a
/// <see cref="NotVerified"/> one weighs the configured unverified weight (default 0.5; S43 D16).
/// </summary>
public enum ContinuityThreatClass
{
    Confirmed = 1,
    NotVerified = 2
}
