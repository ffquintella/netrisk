using DAL.Enums;

namespace Model.DecisionCycle;

/// <summary>
/// The bounds every input of Stage 9.9 is validated against (S50 §4) — one place, so the services, the pure rules and the
/// tests agree.
/// </summary>
public static class DecisionCycleLimits
{
    public const int MaxJustificationLength = 4000;
    public const int MaxConditionDescriptionLength = 1000;
    public const int MaxReviewNoteLength = 2000;
    public const int MaxReopenReasonLength = 2000;

    /// <summary>The archive's review cadence: quarterly, as the methodology's Phase 4 names it.</summary>
    public const int ReviewIntervalMonths = 3;

    public const int MaxBacktestNoteLength = 2000;

    /// <summary>The risks one incident may be matched to.</summary>
    public const int MaxBacktestRisks = 50;

    /// <summary>The longest period one backtesting report covers, and the default (the last year).</summary>
    public const int MaxReportDays = 3660;
    public const int DefaultReportDays = 365;

    /// <summary>The incidents one report lists, oldest dropped first; the counts always cover the whole period.</summary>
    public const int MaxReportItems = 1000;

    public const int MaxCommitteeNameLength = 200;
    public const int MaxMandateLength = 2000;

    /// <summary>A committee decides with at least two approvals — one would be an individual approval.</summary>
    public const int MinRequiredApprovals = 2;
    public const int MaxRequiredApprovals = 50;

    public const int MaxDecisionNameLength = 255;
    public const int MaxDecisionJustificationLength = 20000;
    public const int MaxMinutesReferenceLength = 500;
    public const int MaxVoteCommentLength = 2000;
    public const int MaxWithdrawalReasonLength = 1000;

    /// <summary>The decisions a list returns at most.</summary>
    public const int MaxListLimit = 500;
}

/// <summary>
/// The third line of the methodology's Phase 0 — internal audit (S50 §4.7). A role that reads and never writes: holding
/// <see cref="PermissionKey"/> makes every write of the API refuse the caller, whatever else the caller holds —
/// administrator included — like the Track 8 segregation of duties refuses administrators.
/// </summary>
public static class ThirdLineAssurance
{
    /// <summary>The permission that marks the third line. A restriction, not a grant.</summary>
    public const string PermissionKey = "third_line_assurance";

    /// <summary>The role <c>Data/98.sql</c> seeds with it and with the read permissions below.</summary>
    public const string RoleName = "ThirdLineAuditor";

    /// <summary>The 403 a write by the third line answers with.</summary>
    public const string ReadOnlyRule = "third_line_read_only";

    /// <summary>The 422 when the third line is named as an approver, reviewer or committee member.</summary>
    public const string CannotApproveRule = "third_line_cannot_approve";

    /// <summary>
    /// The read permissions the seeded role carries beside <see cref="PermissionKey"/>: the register, governance,
    /// compliance, assessments, reports, vulnerabilities, hosts and incidents. Several of them also gate writes; the
    /// read-only guard is what keeps those writes closed (S50 D10).
    /// </summary>
    public static readonly IReadOnlyList<string> ReadPermissions =
    [
        "riskmanagement", "governance", "compliance", "assessments", "reports", "vulnerabilities", "hosts",
        "incident_management"
    ];

    /// <summary>
    /// Whether a bulk grant — "every permission" for a new administrator, "select all" in the user editor — may include
    /// <paramref name="key"/>: every permission but the marker. The marker restricts; swept up with everything else it
    /// would make an administrator read-only (S50 R10). It is granted deliberately, through the role, or not at all.
    /// </summary>
    public static bool IsBulkGrantable(string? key) => !string.Equals(key, PermissionKey, StringComparison.Ordinal);

    /// <summary>The permissions a bulk grant gives: all of <paramref name="permissions"/> but the marker.</summary>
    public static List<DAL.Entities.Permission> BulkGrantable(IEnumerable<DAL.Entities.Permission> permissions) =>
        permissions.Where(p => IsBulkGrantable(p.Key)).ToList();
}

// --- archive (T195) --------------------------------------------------------------------------------------------

/// <summary>Whether an archive governs its risk (S50 D1), computed on read.</summary>
public enum RiskArchiveState
{
    /// <summary>Archived, and the risk is closed by the archive's own closure: the conditions and the review apply.</summary>
    Live = 1,

    /// <summary>Reopened through the archive — by a condition, the quarterly review or a person.</summary>
    Reopened = 2,

    /// <summary>
    /// Archived, but the risk was reopened (or closed again) outside the archive — through
    /// <c>DELETE /Risks/{id}/Closure</c>. The record stays; it no longer governs the risk.
    /// </summary>
    Superseded = 3
}

/// <summary>Archive a risk (S50 §4.1).</summary>
public class RiskArchiveRequest
{
    public string? Justification { get; set; }

    /// <summary>The <c>close_reason</c> of the closure the archive makes.</summary>
    public int? CloseReason { get; set; }

    /// <summary>At least one: the Phase 7 triggers whose event reopens the archive.</summary>
    public List<RiskArchiveConditionRequest>? Conditions { get; set; }
}

public class RiskArchiveConditionRequest
{
    public ReassessmentTriggerType? TriggerType { get; set; }

    public string? Description { get; set; }
}

public class RiskArchiveReopenRequest
{
    public string? Reason { get; set; }
}

public class RiskArchiveReviewRequest
{
    public RiskArchiveReviewOutcome? Outcome { get; set; }

    public string? Note { get; set; }
}

public class RiskArchiveConditionDto
{
    public ReassessmentTriggerType TriggerType { get; set; }

    public string? Description { get; set; }
}

public class RiskArchiveReviewDto
{
    public int Id { get; set; }

    public RiskArchiveReviewOutcome Outcome { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime ReviewedAt { get; set; }

    public int? ReviewedById { get; set; }

    public DateTime? NextReviewDueAt { get; set; }
}

public class RiskArchiveDto
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;

    public RiskArchiveStatus Status { get; set; }

    public RiskArchiveState State { get; set; }

    public string Justification { get; set; } = string.Empty;

    public string PreviousStatus { get; set; } = string.Empty;

    public DateTime ArchivedAt { get; set; }

    public int? ArchivedById { get; set; }

    public DateTime NextReviewDueAt { get; set; }

    /// <summary>Live and past its quarterly review date.</summary>
    public bool ReviewOverdue { get; set; }

    public DateTime? LastReviewedAt { get; set; }

    public DateTime? ReopenedAt { get; set; }

    public RiskArchiveReopenOrigin? ReopenOrigin { get; set; }

    public int? ReopenedById { get; set; }

    public string? ReopenReason { get; set; }

    public int? ReopenEventId { get; set; }

    public List<RiskArchiveConditionDto> Conditions { get; set; } = [];

    public List<RiskArchiveReviewDto> Reviews { get; set; } = [];
}

/// <summary>What one pass of the quarterly-review notice did (the daily job, S50 §4.2).</summary>
public class RiskArchiveReviewSweepSummary
{
    public int Live { get; set; }

    public int Due { get; set; }

    /// <summary>Due archives announced in this pass — each due date is announced once.</summary>
    public int Notified { get; set; }
}

// --- backtesting (T196) ----------------------------------------------------------------------------------------

/// <summary>
/// How an incident or near miss compares with the register (S50 §4.4), computed from the dates — never declared.
/// </summary>
public enum BacktestOutcome
{
    /// <summary>Nobody has assessed it yet: neither foreseen nor unforeseen.</summary>
    NotAssessed = 0,

    /// <summary>Assessed: no registered risk describes it.</summary>
    NotForeseen = 1,

    /// <summary>
    /// The risks matched to it were all registered at or after the occurrence — written after the fact. Counted as not
    /// foreseen (S50 D6).
    /// </summary>
    RegisteredAfterOccurrence = 2,

    /// <summary>A risk registered before the occurrence describes it, and the register was not discarding it then.</summary>
    ForeseenTreated = 3,

    /// <summary>
    /// Every risk registered before the occurrence that describes it had been cut — archived, closed, accepted or
    /// decided "monitor/accept" or "archive" — when it happened: a false negative of the cut (S50 §4.4).
    /// </summary>
    ForeseenDismissed = 4
}

/// <summary>Match an incident to the registered risks that describe it, or record that none does (S50 §4.4).</summary>
public class BacktestAssessmentRequest
{
    public List<int>? RiskIds { get; set; }

    /// <summary>Required, and true, when <see cref="RiskIds"/> is empty: an empty list is never taken as an answer by accident.</summary>
    public bool NoCorrespondingScenario { get; set; }

    public string? Note { get; set; }
}

public class BacktestRiskDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public DateTime RegisteredAt { get; set; }

    /// <summary>Registered strictly before the incident occurred — the only way a risk counts as foreseeing it.</summary>
    public bool RegisteredBeforeOccurrence { get; set; }

    /// <summary>Cut by the register when the incident occurred.</summary>
    public bool DismissedAtOccurrence { get; set; }

    /// <summary>Why it reads as cut: "archived", "closed", "accepted", "decided monitor/accept", "decided archive".</summary>
    public List<string> DismissalReasons { get; set; } = [];
}

public class BacktestIncidentDto
{
    public int IncidentId { get; set; }

    public string IncidentName { get; set; } = string.Empty;

    public IncidentKind Kind { get; set; }

    public int? EntityId { get; set; }

    /// <summary>The earliest of the incident's start, report and creation dates.</summary>
    public DateTime OccurredAt { get; set; }

    public BacktestOutcome Outcome { get; set; }

    public DateTime? AssessedAt { get; set; }

    public int? AssessedById { get; set; }

    public string? Note { get; set; }

    /// <summary>The matched risks the caller may see.</summary>
    public List<BacktestRiskDto> Risks { get; set; } = [];

    /// <summary>Matched risks outside the caller's scope — counted in the outcome, not disclosed (S50 D7).</summary>
    public int HiddenRiskCount { get; set; }
}

public class BacktestReportDto
{
    public DateTime From { get; set; }

    public DateTime To { get; set; }

    public int? EntityId { get; set; }

    public int Incidents { get; set; }

    public int NearMisses { get; set; }

    public int Assessed { get; set; }

    public int NotAssessed { get; set; }

    public int NotForeseen { get; set; }

    public int RegisteredAfterOccurrence { get; set; }

    public int ForeseenTreated { get; set; }

    public int ForeseenDismissed { get; set; }

    /// <summary>(not foreseen + registered after) ÷ assessed; null when nothing is assessed.</summary>
    public double? UnforeseenRate { get; set; }

    /// <summary>Foreseen but dismissed ÷ foreseen; null when nothing was foreseen.</summary>
    public double? FalseNegativeRate { get; set; }

    /// <summary>The share of the period's incidents assessed at all; null when there are none.</summary>
    public double? AssessedShare { get; set; }

    public bool Truncated { get; set; }

    public List<BacktestIncidentDto> Items { get; set; } = [];
}

// --- committee (T197) ------------------------------------------------------------------------------------------

public class RiskCommitteeRequest
{
    public string? Name { get; set; }

    public string? Mandate { get; set; }

    /// <summary>Null: the organization's committee, which may decide any risk.</summary>
    public int? EntityId { get; set; }

    public int? RequiredApprovals { get; set; }
}

public class RiskCommitteeMemberDto
{
    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; }

    public int? AddedById { get; set; }
}

public class RiskCommitteeDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Mandate { get; set; }

    public int? EntityId { get; set; }

    public int RequiredApprovals { get; set; }

    public DateTime? RetiredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public List<RiskCommitteeMemberDto> Members { get; set; } = [];
}

/// <summary>Submit a risk acceptance (or a renewal) to a committee (S50 §4.6).</summary>
public class RiskCommitteeDecisionRequest
{
    public int? RiskId { get; set; }

    public RiskCommitteeDecisionKind? Kind { get; set; }

    /// <summary>For a renewal: the acceptance it renews.</summary>
    public int? RenewsAcceptanceId { get; set; }

    public string? Name { get; set; }

    public string? BusinessJustification { get; set; }

    public string? CompensatingControls { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? MinutesReference { get; set; }
}

public class RiskCommitteeVoteRequest
{
    public RiskCommitteeVoteChoice? Choice { get; set; }

    public string? Comment { get; set; }
}

public class RiskCommitteeWithdrawRequest
{
    public string? Reason { get; set; }
}

public class RiskCommitteeVoteDto
{
    public int? VoterId { get; set; }

    public string? VoterName { get; set; }

    public RiskCommitteeVoteChoice Choice { get; set; }

    public string? Comment { get; set; }

    public DateTime CastAt { get; set; }
}

public class RiskCommitteeDecisionDto
{
    public int Id { get; set; }

    public int CommitteeId { get; set; }

    public string CommitteeName { get; set; } = string.Empty;

    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;

    public RiskCommitteeDecisionKind Kind { get; set; }

    public int? RenewsAcceptanceId { get; set; }

    public RiskCommitteeDecisionStatus Status { get; set; }

    public int RequiredApprovals { get; set; }

    public string? Name { get; set; }

    public string BusinessJustification { get; set; } = string.Empty;

    public string? CompensatingControls { get; set; }

    public DateTime ExpiresAt { get; set; }

    public string? MinutesReference { get; set; }

    public int? OpenedById { get; set; }

    public DateTime OpenedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public string? WithdrawalReason { get; set; }

    public int? AcceptanceId { get; set; }

    public int Approvals { get; set; }

    public int Rejections { get; set; }

    public int Abstentions { get; set; }

    /// <summary>Members who may vote on this decision: not the risk's submitter, owner or manager, not the third line.</summary>
    public int EligibleVoters { get; set; }

    public List<RiskCommitteeVoteDto> Votes { get; set; } = [];
}
