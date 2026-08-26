using DAL.Enums;

namespace Model.Notifications;

/// <summary>
/// The event catalog the admin UI renders as rows of its events × channels matrix
/// (Track 4 milestone 4.1.3).
///
/// Lives in <c>Model</c> so the API, the desktop client and the tests all read the same list. A
/// client that hard-coded its own copy would show a checkbox for an event the server never raises,
/// which is worse than showing nothing.
/// </summary>
public static class NotificationCatalog
{
    /// <summary>One catalog row: the event, a label, and whether severity filtering applies to it.</summary>
    public record EventDescriptor(
        NotificationEventType EventType,
        string Name,
        string Description,
        bool SupportsSeverityFilter,
        bool DigestRecommended);

    public static readonly IReadOnlyList<EventDescriptor> Events =
    [
        new(NotificationEventType.RiskCreated, "risk.created",
            "A risk was recorded.", true, false),
        new(NotificationEventType.RiskSeverityChanged, "risk.severity_changed",
            "A risk's severity band moved.", true, false),
        new(NotificationEventType.VulnerabilityImported, "vulnerability.imported",
            "A scanner import completed.", true, true),
        new(NotificationEventType.FindingStatusChanged, "finding.status_changed",
            "A finding moved through the triage lifecycle.", true, true),
        new(NotificationEventType.SlaApproaching, "sla.approaching",
            "A finding is nearing its remediation deadline.", true, true),
        new(NotificationEventType.SlaBreached, "sla.breached",
            "A finding passed its remediation deadline.", true, false),
        new(NotificationEventType.IncidentCreated, "incident.created",
            "An incident was opened.", false, false),
        new(NotificationEventType.IrpTaskAssigned, "irp.task_assigned",
            "An incident-response task was assigned.", false, true),
        new(NotificationEventType.RiskAcceptanceExpiring, "riskacceptance.expiring",
            "A risk acceptance is approaching expiry.", false, true),
        new(NotificationEventType.IssueSyncApplied, "issuesync.applied",
            "An external issue tracker changed a finding.", false, true),

        // Track 8. The review and campaign events are digest-recommended because they are produced
        // by a daily sweep: a subscriber to "overdue reviews" wants one message listing them, not
        // forty messages saying the same thing about forty risks.
        new(NotificationEventType.RiskReviewOverdue, "risk.review_overdue",
            "A risk's management review is overdue, or it has never been reviewed.", true, true),
        new(NotificationEventType.RiskAcceptanceExpired, "riskacceptance.expired",
            "A risk acceptance lapsed and the risk needs re-triage or a renewal.", true, false),
        new(NotificationEventType.MitigationTaskDue, "mitigationtask.due",
            "A treatment task is due or overdue.", false, true),
        new(NotificationEventType.RiskReviewCampaignAssigned, "riskreview.campaign_assigned",
            "A periodic business risk review was assigned to a reviewer.", false, false),
        new(NotificationEventType.RiskReviewCampaignOverdue, "riskreview.campaign_overdue",
            "A periodic business risk review passed its due date.", false, false),
        new(NotificationEventType.RiskEscalated, "risk.escalated",
            "A business reviewer escalated a risk to a named senior approver.", true, false)
    ];

    /// <summary>The wire name for an event type, or its enum name when the catalog does not list it.</summary>
    public static string NameOf(NotificationEventType eventType) =>
        Events.FirstOrDefault(e => e.EventType == eventType)?.Name ?? eventType.ToString();

    /// <summary>Resolves a wire name back to the enum. Null when unknown — the caller rejects it.</summary>
    public static NotificationEventType? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var descriptor = Events.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.EventType.ToString(), name, StringComparison.OrdinalIgnoreCase));

        return descriptor?.EventType;
    }
}
