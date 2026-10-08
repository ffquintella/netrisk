using Model.DecisionCycle;

namespace ServerServices.Interfaces;

/// <summary>
/// Stage 9.9 (S50 §4.1–§4.3) — the Phase 4 "archive" decision: a risk closed with a justification, the Phase 7 triggers
/// that reopen it, and a quarterly review.
///
/// Errors: <c>InvalidParameterException</c> (400, naming the field), <c>DataNotFoundException</c> (404 — also for a risk
/// outside the caller's scope), <c>RuleBrokenException</c> (422: <c>risk_archive_risk_closed</c>,
/// <c>risk_archive_not_live</c>, <c>risk_archive_condition_met</c>, <c>gate_a_non_discretionary</c>,
/// <c>segregation_of_duties</c>, <c>third_line_cannot_approve</c>), <c>InvalidStateTransitionException</c> (422 — the
/// Track 8 state machine's rule for closing).
/// </summary>
public interface IRiskArchiveService
{
    /// <summary>The archives the caller can see: live ones, and ended ones too when asked; only those due when asked.</summary>
    Task<List<RiskArchiveDto>> GetArchivesAsync(bool dueOnly, bool includeEnded);

    /// <summary>Every archive of one risk, newest first.</summary>
    Task<List<RiskArchiveDto>> GetRiskArchivesAsync(int riskId);

    /// <summary>
    /// Archives an open risk: closes it through its own closure, records the Phase 4 "archive" decision, the conditions
    /// that reopen it and the first review date, a quarter away. Checked like a closure (Gate A, the state machine) and
    /// like a decision (segregation of duties, not the third line).
    /// </summary>
    Task<RiskArchiveDto> ArchiveAsync(int riskId, RiskArchiveRequest request, int actingUserId);

    /// <summary>Reopens the risk's live archive by hand, with a reason; the risk returns to its status before archiving.</summary>
    Task<RiskArchiveDto> ReopenAsync(int riskId, RiskArchiveReopenRequest request, int actingUserId);

    /// <summary>The quarterly review of the risk's live archive: keep it (next review a quarter away) or reopen it.</summary>
    Task<RiskArchiveDto> ReviewAsync(int riskId, RiskArchiveReviewRequest request, int actingUserId);

    /// <summary>
    /// The daily sweep (S50 §4.2): announces each live archive whose quarterly review is due — once per due date — through
    /// <c>risk.archive_review_due</c>. Runs unscoped; never throws for one archive.
    /// </summary>
    Task<RiskArchiveReviewSweepSummary> NotifyDueReviewsAsync();
}
