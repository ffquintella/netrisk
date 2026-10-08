using DAL.Enums;
using Model.TailRisk;

namespace Model.Governance;

/// <summary>
/// The request to accept a risk (Track 8 milestone 8.1.2).
///
/// Justification and expiry are not optional and are not defaulted. An acceptance without a reason
/// is not evidence of a decision, and one without an expiry is the failure the whole milestone
/// exists to prevent: "accepted" quietly becoming "forgotten".
/// </summary>
public class RiskAcceptanceRequest
{
    public string? Name { get; set; }

    public string? BusinessJustification { get; set; }

    /// <summary>The manager authorizing. Defaults to the caller when omitted.</summary>
    public int? AuthorizingManagerId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? CompensatingControls { get; set; }

    /// <summary>
    /// A stated, audited override of the segregation-of-duties rule. Ignored unless the
    /// <c>risk_workflow_segregation_break_glass</c> setting is on; when used, the reason is written
    /// to the management review the acceptance produces.
    /// </summary>
    public string? SegregationOverrideReason { get; set; }
}

/// <summary>Why an acceptance is being withdrawn. Mandatory — revoking is as consequential as accepting.</summary>
public class RiskAcceptanceRevocation
{
    public string? Reason { get; set; }
}

/// <summary>
/// The appetite in force for a risk, and what it implies for a decision about that risk
/// (Track 8 milestone 8.3.3).
/// </summary>
public class AppetiteEvaluation
{
    /// <summary>Whether an appetite is configured at all. When false nothing is gated, and the
    /// admin screen says so rather than implying a control that is not there.</summary>
    public bool AppetiteConfigured { get; set; }

    public int? AppetiteId { get; set; }

    /// <summary>The entity whose appetite applied, or null when the global default did.</summary>
    public int? EntityId { get; set; }

    public double? MaxAcceptableResidual { get; set; }

    public double? DualApprovalThreshold { get; set; }

    public double? ResidualScore { get; set; }

    /// <summary>Residual above the ceiling: the risk cannot be accepted at all.</summary>
    public bool ExceedsCeiling { get; set; }

    /// <summary>Residual above the escalation threshold: a second, distinct top-band approver is required.</summary>
    public bool RequiresDualApproval { get; set; }

    /// <summary>A sentence the GUI and the portal can show verbatim.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Gate B on the tail (Stage 9.7, S48 §4.7): the appetite's monetary tolerances against the risk's E[L], P95 and
    /// CVaR95. Additive — every field above keeps its meaning, and the ordinal ceiling is still checked first.
    /// </summary>
    public TailAppetiteEvaluation Tail { get; set; } = new();

    /// <summary>
    /// Gate B by indicator (Stage 9.8, S49 §4.8): the KRIs linked to the risk against their tolerances. Additive and
    /// checked last, after the ceiling and the tail. Independent of <see cref="AppetiteConfigured"/>: a KRI carries its
    /// own tolerance (S49 D5), so a risk with no appetite row is still gated by its indicators.
    /// </summary>
    public Model.Monitoring.IndicatorAppetiteEvaluation Indicators { get; set; } = new();
}

/// <summary>
/// How many open risks sit above the appetite in force, for one entity or for the organization
/// (Track 8 milestone 8.3.3). A list rather than a map keyed on a nullable entity id: "the global
/// bucket" is a real row here, and the dashboards render it beside the named ones.
/// </summary>
public class AppetiteBreachCount
{
    public int? EntityId { get; set; }

    public string? EntityName { get; set; }

    public int Count { get; set; }
}

/// <summary>Create/update payload for a mitigation task line item (Track 8 milestone 8.5.3).</summary>
public class MitigationTaskRequest
{
    public int Id { get; set; }

    public int MitigationId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public int? OwnerId { get; set; }

    public DateTime? DueDate { get; set; }

    public MitigationTaskStatus? Status { get; set; }

    /// <summary>
    /// What has to be true for the task to count as done (Stage 9.6, S47 §4.8), ≤ 4 000. On update, null leaves
    /// the stored value as it is and blank clears it — a client that does not know the field cannot erase it.
    /// </summary>
    public string? AcceptanceCriterion { get; set; }

    /// <summary>
    /// The evidence the task was done (S47 §4.8), ≤ 4 000. Moving a task to Completed requires it, sent now or
    /// already stored; on update null leaves it as it is, and it cannot be cleared from a completed task. Who
    /// recorded it and when are stamped by the server.
    /// </summary>
    public string? CompletionEvidence { get; set; }
}

/// <summary>The edits applied while promoting a pending risk into the register (8.5.2).</summary>
public class PendingRiskPromotion
{
    public string? Subject { get; set; }

    public string? Notes { get; set; }

    public int? CategoryId { get; set; }

    public int? SourceId { get; set; }

    public int? OwnerId { get; set; }

    public int? ManagerId { get; set; }

    public int? EntityId { get; set; }

    public int? Likelihood { get; set; }

    public int? Impact { get; set; }

    // --- Stage 9.2 (S42 §6): the structured scenario the promoted risk starts with --------------

    public string? ScenarioCause { get; set; }

    public string? ScenarioVulnerability { get; set; }

    public string? ScenarioCentralEvent { get; set; }

    public string? ScenarioConsequences { get; set; }

    /// <summary>
    /// The promoted risk's evidence confidence. When omitted the risk is promoted as
    /// <see cref="EvidenceConfidence.Hypothesis"/>: promotion moves a hypothesis into the register, it
    /// does not by itself produce evidence (S42 §11, D5).
    /// </summary>
    public EvidenceConfidence? EvidenceConfidence { get; set; }
}

/// <summary>
/// A standalone hypothesis — a pending risk registered directly rather than raised by an assessment
/// answer (Stage 9.2, T152, S42 §6). It enters the same triage queue and is promoted or dismissed the
/// same way.
/// </summary>
public class HypothesisRequest
{
    /// <summary>Required; what the hypothesis is about.</summary>
    public string? Subject { get; set; }

    /// <summary>Why someone suspects it — stored as the pending risk's comment.</summary>
    public string? Description { get; set; }

    public int? OwnerId { get; set; }

    public string? AffectedAssets { get; set; }

    /// <summary>
    /// The business entity the hypothesis is filed under (S42 §5.4). Must be within the caller's
    /// scope. Omitted: a caller scoped to one entity gets that one, a caller scoped to several must
    /// name it, and an unrestricted caller files an organization-wide hypothesis.
    /// </summary>
    public int? EntityId { get; set; }
}

/// <summary>
/// One row of the assessment intake queue, rendered for a triage screen (Track 8 milestone 8.5.2).
///
/// A projection rather than the entity because <c>pending_risks.subject</c> is a BLOB — legacy
/// schema this track does not retype — and every consumer would otherwise have to know to decode it.
/// </summary>
public class PendingRiskListing
{
    public int Id { get; set; }

    /// <summary>NULL for a standalone hypothesis (Stage 9.2, S42 §6).</summary>
    public int? AssessmentId { get; set; }

    /// <summary>NULL for a standalone hypothesis.</summary>
    public int? AssessmentAnswerId { get; set; }

    /// <summary>Assessment-raised or standalone (S42 §6).</summary>
    public PendingRiskOrigin Origin { get; set; } = PendingRiskOrigin.Assessment;

    /// <summary>The author of a standalone hypothesis; NULL for an assessment-raised row.</summary>
    public int? SubmittedById { get; set; }

    /// <summary>The business entity the row is scoped to; NULL is organization-wide (S42 §4.2).</summary>
    public int? EntityId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public float Score { get; set; }

    public int? OwnerId { get; set; }

    public string? AffectedAssets { get; set; }

    public string? Comment { get; set; }

    public DateTime SubmissionDate { get; set; }

    public PendingRiskStatus Status { get; set; }

    public int? PromotedRiskId { get; set; }

    public string? DismissalReason { get; set; }
}

/// <summary>Why a pending risk is being dropped. Required: a queue drained without reasons is a queue deleted.</summary>
public class PendingRiskDismissal
{
    public string? Reason { get; set; }
}

/// <summary>One reviewer's decision on one campaign item (Track 8 milestone 8.6.4).</summary>
public class CampaignDecisionRequest
{
    public RiskReviewDecision Decision { get; set; }

    public string? Notes { get; set; }

    /// <summary>Populated when <see cref="Decision"/> is <c>Accepted</c>.</summary>
    public RiskAcceptanceRequest? Acceptance { get; set; }

    /// <summary>Populated when <see cref="Decision"/> is <c>MitigationRequested</c>.</summary>
    public List<MitigationTaskRequest>? Tasks { get; set; }

    /// <summary>Populated when <see cref="Decision"/> is <c>Escalated</c>.</summary>
    public int? EscalateToUserId { get; set; }
}

/// <summary>A drag-to-rank reordering: campaign item ids in the reviewer's priority order.</summary>
public class CampaignRankingRequest
{
    public List<int> OrderedItemIds { get; set; } = [];
}

/// <summary>Inherent and residual side by side, with the delta the GUI and reports show (8.2.2).</summary>
public class RiskScorePair
{
    public int RiskId { get; set; }

    public float Inherent { get; set; }

    public float? Residual { get; set; }

    /// <summary>Inherent minus residual — how much the treatment is claimed to have bought.</summary>
    public float? Delta => Residual is null ? null : Inherent - Residual.Value;

    public double? ContributingScore { get; set; }
}

/// <summary>
/// One risk in a review campaign, with everything a business reviewer needs to decide it
/// (Track 8 milestone 8.6.4).
///
/// Assembled server-side and returned as a campaign sub-resource on purpose. The portal's audience
/// holds <c>business_risk_review</c> and deliberately <em>not</em> <c>riskmanagement</c>, so it cannot
/// read <c>/Risks/{id}/Appetite</c> or <c>/Risks/Scores</c> — and it should not be able to: those are
/// register-wide reads. Gathering the same information behind the campaign's own permission keeps the
/// reviewer scoped to the campaign they were appointed to, and turns what would be one request per
/// risk plus three into a single call.
/// </summary>
public class CampaignReviewItem
{
    public int ItemId { get; set; }

    public int RiskId { get; set; }

    public int? Rank { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string ReferenceId { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;

    public float? Inherent { get; set; }

    public float? Residual { get; set; }

    public RiskReviewDecision Decision { get; set; }

    public string? DecisionNotes { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>What the appetite in force says about accepting this risk, and why.</summary>
    public AppetiteEvaluation? Appetite { get; set; }

    /// <summary>The expiry of the acceptance already in force, if any.</summary>
    public DateTime? AcceptedUntil { get; set; }

    /// <summary>The treatment tasks already on the risk, so a reviewer does not ask twice.</summary>
    public List<CampaignReviewTask> Tasks { get; set; } = [];
}

/// <summary>A treatment task as the portal shows it. A projection, not the entity: a business
/// reviewer has no use for the audit columns and no business seeing them.</summary>
public class CampaignReviewTask
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public MitigationTaskStatus Status { get; set; }

    public DateTime? DueDate { get; set; }
}

/// <summary>
/// A stored risk whose state the 8.3.1 machine would not have allowed it to reach.
///
/// Reported, never auto-mutated. A legacy status is evidence of how the risk actually got where it
/// is, and silently rewriting it would destroy the only record of that — which is the opposite of
/// what a governance track is for.
/// </summary>
public class WorkflowViolation
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>Campaign completion statistics per entity (Track 8 milestone 8.6.5).</summary>
public class CampaignStatistics
{
    public int CampaignId { get; set; }

    public int EntityId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public int TotalItems { get; set; }

    public int DecidedItems { get; set; }

    public int Accepted { get; set; }

    public int MitigationRequested { get; set; }

    public int Escalated { get; set; }

    public RiskReviewCampaignStatus Status { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>Mean days from campaign creation to a decision, over the decided items.</summary>
    public double? AverageDaysToDecide { get; set; }
}

/// <summary>
/// A risk whose management review is overdue, with everything the notification needs
/// (Track 8 milestone 8.5.1).
///
/// Resolved in the service rather than in the job: the cadence comes from two lookup tables and a
/// setting, and a job that assembled it itself would be a second implementation of the rule that
/// <c>GetRiskReviewLevelAsync</c> already owns.
/// </summary>
public class OverdueReview
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string ReferenceId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int? OwnerId { get; set; }

    public int? ManagerId { get; set; }

    public int? EntityId { get; set; }

    public double? Score { get; set; }

    /// <summary>Null when the risk has never been reviewed — the group that is easiest to miss,
    /// because a query written around <c>next_review</c> skips it entirely.</summary>
    public DateTime? LastReviewedAt { get; set; }

    public int CadenceDays { get; set; }

    public int DaysOverdue { get; set; }
}

/// <summary>The FAIR-lite inputs a caller supplies for a quantitatively scored risk (8.7.2).</summary>
public class QuantitativeRiskInput
{
    public double LossEventFrequencyMin { get; set; }

    public double LossEventFrequencyMostLikely { get; set; }

    public double LossEventFrequencyMax { get; set; }

    public double LossMagnitudeMin { get; set; }

    public double LossMagnitudeMostLikely { get; set; }

    public double LossMagnitudeMax { get; set; }

    /// <summary>Iterations; the service clamps it to a sane range.</summary>
    public int? Iterations { get; set; }

    /// <summary>Seed, so a stated number can be reproduced exactly. Defaults to a fixed value.</summary>
    public int? Seed { get; set; }
}

/// <summary>The simulation result, before and after the mitigation's claimed effectiveness (8.7.2).</summary>
public class QuantitativeRiskResult
{
    public int RiskId { get; set; }

    public double InherentP10 { get; set; }

    public double InherentP50 { get; set; }

    public double InherentP90 { get; set; }

    public double InherentMean { get; set; }

    public double? ResidualP10 { get; set; }

    public double? ResidualP50 { get; set; }

    public double? ResidualP90 { get; set; }

    /// <summary>The mean of the residual run — Gate C's E[L] after (Stage 9.6, S47 §4.4); null without a residual run.</summary>
    public double? ResidualMean { get; set; }

    /// <summary>Stage 9.7 (S48 §4.2): E[L], P95 and CVaR95 with confidence intervals of the inherent run; null before
    /// schema 96 until the analysis is recomputed.</summary>
    public TailStatisticsDto? InherentTail { get; set; }

    /// <summary>The same for the residual run; null without one.</summary>
    public TailStatisticsDto? ResidualTail { get; set; }

    public List<LossExceedancePointDto> LossExceedanceCurve { get; set; } = [];

    /// <summary>The band the median annualized loss maps into, so lists and heatmaps keep working.</summary>
    public string MappedRiskLevel { get; set; } = string.Empty;

    /// <summary>The 0–10 score the monetary median maps to, so a quantitative risk sorts beside a matrix one.</summary>
    public float MappedScore { get; set; }

    public int Seed { get; set; }

    public int Iterations { get; set; }
}

public class LossExceedancePointDto
{
    public double Loss { get; set; }

    public double Probability { get; set; }
}

// --- The auditor evidence pack (Track 8 milestones 8.4.2 and 8.6.5) ----------------------------
//
// A flat list of field changes is a change log, not evidence. What an auditor asks for is the
// decisions — who accepted what, until when, on whose authority, and what the business reviewers
// said — with the field-level trail underneath as corroboration. So the pack carries four sections
// over one entity and one period, assembled once and rendered by both the CSV and the PDF path.
//
// These are DTOs rather than the DAL entities because Model sits below DAL and cannot see them, and
// because the pack has to name people rather than user ids: an evidence file whose actor column
// holds "412" is not evidence anybody can read.

public class GovernanceEvidencePack
{
    public int? EntityId { get; set; }

    /// <summary>Resolved entity name, or a marker when the export covers the whole register.</summary>
    public string EntityName { get; set; } = "";

    public DateTime FromUtc { get; set; }

    public DateTime ToUtc { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    /// <summary>Who asked for the pack. An evidence export is itself an auditable act.</summary>
    public string RequestedBy { get; set; } = "";

    /// <summary>True when the row limit cut the change list short, so the reader is told.</summary>
    public bool ChangesTruncated { get; set; }

    public List<EvidenceAcceptance> Acceptances { get; set; } = [];

    public List<EvidenceReview> Reviews { get; set; } = [];

    public List<EvidenceCampaignDecision> CampaignDecisions { get; set; } = [];

    public List<EvidenceChange> Changes { get; set; } = [];
}

/// <summary>A formal acceptance that was active at any point in the period (8.1).</summary>
public class EvidenceAcceptance
{
    public int Id { get; set; }
    public int? RiskId { get; set; }
    public string RiskSubject { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string AuthorizingManager { get; set; } = "";
    public string? RequestedBy { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public string? BusinessJustification { get; set; }
    public string? CompensatingControls { get; set; }
    public double? ResidualScoreSnapshot { get; set; }

    /// <summary>Set when the acceptance came from a business reviewer's decision (8.6.4).</summary>
    public bool FromCampaign { get; set; }
}

/// <summary>A management review recorded in the period, with its counter-signature (8.3.4).</summary>
public class EvidenceReview
{
    public int Id { get; set; }
    public int RiskId { get; set; }
    public string RiskSubject { get; set; } = "";
    public DateTime SubmissionDate { get; set; }
    public string Reviewer { get; set; } = "";
    public string Comments { get; set; } = "";
    public bool RequiresCountersignature { get; set; }
    public string? SecondReviewer { get; set; }
    public DateTime? SecondReviewAt { get; set; }

    /// <summary>Present only when somebody broke the segregation rule on purpose (8.3.2).</summary>
    public string? SegregationOverrideReason { get; set; }
}

/// <summary>One business reviewer's decision inside a campaign (8.6.4, folded in per 8.6.5).</summary>
public class EvidenceCampaignDecision
{
    public int CampaignId { get; set; }
    public string CampaignName { get; set; } = "";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime DueDate { get; set; }
    public string CampaignStatus { get; set; } = "";
    public int RiskId { get; set; }
    public string RiskSubject { get; set; } = "";
    public int? Rank { get; set; }
    public string Decision { get; set; } = "";
    public string? DecisionNotes { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? EscalatedTo { get; set; }
    public int? RiskAcceptanceId { get; set; }
}

/// <summary>One field-level change from the interceptor's trail (8.4.1).</summary>
public class EvidenceChange
{
    public DateTime OccurredAt { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string? Field { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public int? UserId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? CorrelationId { get; set; }
}
