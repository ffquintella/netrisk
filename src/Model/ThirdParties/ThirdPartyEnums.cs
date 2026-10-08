namespace Model.ThirdParties;

/// <summary>
/// What a HECVAT assessment says, computed on read from its answers (Stage 9.10, S51 §4.5, T202, T205). Computed enums
/// start at 1 (S43 D17). Only <see cref="Conforming"/> is a pass; every other state is not, and in particular a
/// questionnaire with any question unanswered is <see cref="Incomplete"/> whatever the answered part scores (S51 D7).
/// </summary>
public enum HecvatState
{
    /// <summary>
    /// Fewer questions answered (Yes, No or N/A) than the vendor was asked, or a question left blank. No score is
    /// reported — a score over the answered part would read as a pass of the whole.
    /// </summary>
    Incomplete = 1,

    /// <summary>Complete, scored at or above the pass threshold, and no critical question answered against the preference.</summary>
    Conforming = 2,

    /// <summary>Complete, and scored below the threshold or a critical question answered against the preference.</summary>
    NonConforming = 3,

    /// <summary>Complete, but past its validity date — never a pass, whatever it scored.</summary>
    Expired = 4,

    /// <summary>Complete, but nothing in it is scored (every scored question N/A, or none has a preferred answer).</summary>
    NotScorable = 5,

    /// <summary>Voided with a reason; kept as evidence, read as nothing.</summary>
    Voided = 6,

    /// <summary>The third party has no live assessment at all.</summary>
    NotAssessed = 7
}

/// <summary>
/// A gap in what the register knows about a third party (S51 §4.7), computed on read and never stored. A finding is a
/// signal for whoever owns the relationship — nothing is refused because of one.
/// </summary>
public enum ThirdPartyFindingCode
{
    HecvatMissing = 1,
    HecvatIncomplete = 2,
    HecvatNonConforming = 3,
    HecvatExpired = 4,
    HecvatNotScorable = 5,

    /// <summary>Whether it processes personal data is not declared.</summary>
    PersonalDataUndeclared = 6,

    /// <summary>It processes personal data and its sub-processor list was never declared — not even as empty.</summary>
    SubprocessorsUndeclared = 7,

    /// <summary>It processes personal data, or a data record, and no data location is declared.</summary>
    DataLocationMissing = 8,

    RightToAuditUndeclared = 9,

    /// <summary>The contract does not grant the right to audit.</summary>
    RightToAuditMissing = 10,

    ExitPlanMissing = 11,

    /// <summary>It supports a critical process and its exit plan was never exercised.</summary>
    ExitPlanUntested = 12,

    DataPortabilityMissing = 13,

    /// <summary>What it supplies requires an RTO and the contract commits to none.</summary>
    RtoNotContracted = 14,

    /// <summary>The contracted RTO is looser than what it supplies requires.</summary>
    RtoExceedsRequirement = 15,

    RpoNotContracted = 16,
    RpoExceedsRequirement = 17,

    /// <summary>No contracted deadline to fix a vulnerability of medium severity or higher.</summary>
    VulnerabilityFixUndeclared = 18,

    /// <summary>The contracted deadline is longer than the 30 days FGV's NRM §5.2 allows for medium severity or higher.</summary>
    VulnerabilityFixTooSlow = 19,

    /// <summary>Still active, with a contract that has ended.</summary>
    ContractExpired = 20,

    SlaUndeclared = 21
}

/// <summary>The three concentrations the methodology names (S51 §4.8, T203): supplier, cloud and identity.</summary>
public enum ConcentrationDimension
{
    /// <summary>Every active or exiting third party.</summary>
    Supplier = 1,

    /// <summary>The third parties marked as cloud providers.</summary>
    Cloud = 2,

    /// <summary>The third parties marked as identity providers.</summary>
    Identity = 3
}
