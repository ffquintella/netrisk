namespace DAL.Enums;

/// <summary>
/// The domain events a subscription can listen for (Track 4 milestone 4.1.3), persisted in
/// <c>notification_subscriptions.event_type</c> and <c>notification_deliveries.event_type</c>.
///
/// The catalog is closed on purpose. A subscription matrix in the admin UI has to enumerate the
/// events, and a free-text event name would let a subscription be created for an event nothing ever
/// raises — a notification that silently never fires is worse than one that cannot be configured.
/// </summary>
public enum NotificationEventType
{
    /// <summary>A risk was recorded.</summary>
    RiskCreated = 1,

    /// <summary>A risk's severity/score band moved.</summary>
    RiskSeverityChanged = 2,

    /// <summary>A scanner import completed. Digest-friendly: one import can carry thousands of findings.</summary>
    VulnerabilityImported = 3,

    /// <summary>A finding moved through the triage lifecycle (Track 3.2.1).</summary>
    FindingStatusChanged = 4,

    /// <summary>A finding is within its warning window of the remediation deadline.</summary>
    SlaApproaching = 5,

    /// <summary>A finding passed its remediation deadline.</summary>
    SlaBreached = 6,

    /// <summary>An incident was opened.</summary>
    IncidentCreated = 7,

    /// <summary>An incident-response-plan task was assigned to someone.</summary>
    IrpTaskAssigned = 8,

    /// <summary>A formal risk acceptance is approaching its expiry date (Track 3.2.4).</summary>
    RiskAcceptanceExpiring = 9,

    /// <summary>An external issue tracker pushed a change back into NetRisk (Track 4.2.3).</summary>
    IssueSyncApplied = 10,

    // --- Track 8 (Risk Governance) --------------------------------------------------------------
    // The gap these close is that NetRisk's review cadence was pull-only: the machinery to find an
    // overdue review existed, and nothing pushed. DORA Art. 6(5) expects a review at least annually
    // and after major incidents, which is a schedule somebody has to be told about.

    /// <summary>A risk's management review is overdue, or it has never been reviewed (Track 8.5.1).</summary>
    RiskReviewOverdue = 11,

    /// <summary>A risk acceptance lapsed and the risk is back in front of somebody (Track 8.1.3).</summary>
    RiskAcceptanceExpired = 12,

    /// <summary>A treatment task is due or overdue (Track 8.5.3).</summary>
    MitigationTaskDue = 13,

    /// <summary>A business review campaign was assigned to a reviewer (Track 8.6.3).</summary>
    RiskReviewCampaignAssigned = 14,

    /// <summary>A business review campaign passed its due date (Track 8.6.3).</summary>
    RiskReviewCampaignOverdue = 15,

    /// <summary>A business reviewer escalated a risk to a named senior approver (Track 8.6.4).</summary>
    RiskEscalated = 16,

    // --- Track 4.6 (Jira Service Management) ----------------------------------------------------

    /// <summary>
    /// A mirrored Jira Service Management request breached one of its SLA metrics (Track 4.6).
    ///
    /// Distinct from <see cref="SlaBreached"/>, which is NetRisk's own remediation deadline on a
    /// finding. Folding the two together would mean a subscription for "our SLA" also firing for
    /// somebody else's service-desk goal, which are different audiences.
    /// </summary>
    JsmSlaBreached = 17,

    // --- Track 9 Stage 9.5 (the eleven flags and Gate A) ----------------------------------------

    /// <summary>
    /// A risk reached a non-discretionary (Gate A) condition, or somebody recorded "act immediately" on
    /// it (S46 §4.8). Distinct from <see cref="RiskEscalated"/>, which is a business reviewer handing one
    /// risk to a named approver: this one goes to whoever manages risk, and is never digest material.
    /// </summary>
    RiskGateAEscalated = 18,

    // --- Track 9 Stage 9.8 (KRIs and reassessment triggers) -------------------------------------

    /// <summary>
    /// A key risk indicator went beyond its tolerance (S49 §4.10). Raised once per breach episode — a KRI that stays
    /// breached for thirty days is announced once, not thirty times.
    /// </summary>
    KriToleranceBreached = 19,

    /// <summary>
    /// A mandatory reassessment trigger of Phase 7 was raised on a risk (S49 §4.10): a declared event, or a linked KRI
    /// beyond its tolerance. Raised once per event and risk.
    /// </summary>
    RiskReassessmentTriggered = 20,

    // --- Track 9 Stage 9.9 (archival and the risk committee) ------------------------------------

    /// <summary>
    /// An archived risk was reopened by one of its conditions (S50 §4.3). Raised once: the archive is reopened once, and
    /// a further condition reaches an open risk, which the reassessment trigger already announces.
    /// </summary>
    RiskArchiveReopened = 21,

    /// <summary>An archived risk is due for its quarterly review (S50 §4.2). Raised once per due date.</summary>
    RiskArchiveReviewDue = 22,

    /// <summary>A risk acceptance was submitted to a risk committee for its collegiate decision (S50 §4.6).</summary>
    RiskCommitteeDecisionOpened = 23
}
