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
    JsmSlaBreached = 17
}
