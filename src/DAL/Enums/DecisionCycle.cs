namespace DAL.Enums;

/// <summary>
/// Where an archive stands (Stage 9.9, S50 §4.1). Stored as <c>risk_archives.status</c> (int, CHECK 1–2).
///
/// <see cref="Archived"/> is the record's own state. Whether the archive is <em>live</em> is computed on read: the risk
/// must still be closed by the closure the archive made (S50 D1) — a risk reopened through
/// <c>DELETE /Risks/{id}/Closure</c> leaves an <see cref="Archived"/> row that no longer governs anything.
/// </summary>
public enum RiskArchiveStatus
{
    /// <summary>The risk was archived with a justification and reopening conditions.</summary>
    Archived = 1,

    /// <summary>The archive was reopened — by a condition, at its quarterly review or by hand.</summary>
    Reopened = 2
}

/// <summary>What reopened an archive (S50 §4.1). Stored as <c>risk_archives.reopen_origin</c> (CHECK 1–3).</summary>
public enum RiskArchiveReopenOrigin
{
    /// <summary>A reassessment event of a type the archive watches reached the risk (S50 §4.3) — fires once.</summary>
    Condition = 1,

    /// <summary>The quarterly review decided to reopen it.</summary>
    QuarterlyReview = 2,

    /// <summary>A person reopened it, with a reason.</summary>
    Manual = 3
}

/// <summary>The outcome of an archive's quarterly review (S50 §4.2). Stored as <c>risk_archive_reviews.outcome</c>.</summary>
public enum RiskArchiveReviewOutcome
{
    /// <summary>Still not worth treating: the archive stands and the next review is a quarter away.</summary>
    KeepArchived = 1,

    /// <summary>No longer: the archive is reopened.</summary>
    Reopen = 2
}

/// <summary>What a risk committee is asked to decide (S50 §4.6). Stored as <c>risk_committee_decisions.kind</c>.</summary>
public enum RiskCommitteeDecisionKind
{
    /// <summary>Accept the residual risk — a new acceptance.</summary>
    Accept = 1,

    /// <summary>Renew a live or lapsed acceptance — a new acceptance chained to the previous one.</summary>
    Renew = 2
}

/// <summary>Where a committee decision stands (S50 §4.6). Stored as <c>risk_committee_decisions.status</c>.</summary>
public enum RiskCommitteeDecisionStatus
{
    /// <summary>Voting.</summary>
    Open = 1,

    /// <summary>The required approvals were reached; the acceptance was created in the same write.</summary>
    Approved = 2,

    /// <summary>The required approvals can no longer be reached by the members who may still vote.</summary>
    Rejected = 3,

    /// <summary>Withdrawn by whoever submitted it, or by an administrator, with a reason.</summary>
    Withdrawn = 4
}

/// <summary>A member's vote (S50 §4.6). Stored as <c>risk_committee_votes.choice</c>; a vote is final.</summary>
public enum RiskCommitteeVoteChoice
{
    Approve = 1,
    Reject = 2,
    Abstain = 3
}
