using DAL.Entities;
using DAL.Enums;
using Model.Notifications;

namespace ServerServices.Interfaces;

/// <summary>
/// What the domain calls to raise a notification event (Track 4 milestone 4.1.3).
///
/// Typed methods rather than a single <c>Publish(NotificationMessage)</c>: the mapping from a domain
/// object to a title, a severity and a field grid is a decision that belongs in one place, and if
/// each call site built its own message the same event would read differently depending on which code
/// path raised it. It also means a caller cannot forget the deep link.
///
/// Every method swallows its own failures. A notification is a side effect; a broken Slack webhook
/// must never fail the operation that raised it.
/// </summary>
public interface INotificationEventPublisher
{
    /// <summary>
    /// A risk was recorded (<c>risk.created</c>). <paramref name="score"/> is passed in rather than
    /// read off the risk because <c>risk_scoring</c> is a separate row that the creating service has
    /// in hand and the notification layer would otherwise have to go back to the database for.
    /// </summary>
    Task RiskCreatedAsync(Risk risk, double? score = null);

    /// <summary>A risk's severity band moved (<c>risk.severity_changed</c>).</summary>
    Task RiskSeverityChangedAsync(Risk risk, double? previousScore, double? newScore);

    /// <summary>A scanner import finished (<c>vulnerability.imported</c>). Digest-friendly.</summary>
    Task VulnerabilityImportedAsync(ScanImport import);

    /// <summary>A finding moved through the triage lifecycle (<c>finding.status_changed</c>).</summary>
    Task FindingStatusChangedAsync(Vulnerability finding, FindingStatus? from, FindingStatus to,
        FindingStatusChangeSource source, string? justification = null);

    /// <summary>
    /// A finding is approaching or past its remediation deadline (<c>sla.approaching</c> /
    /// <c>sla.breached</c>). <paramref name="thresholdDays"/> of 0 means the deadline has passed.
    /// </summary>
    Task SlaAsync(int findingId, string title, string? severityLabel, int? severity, DateTime dueDate,
        int thresholdDays, int daysOverdue, int? entityId);

    /// <summary>An incident was opened (<c>incident.created</c>).</summary>
    Task IncidentCreatedAsync(Incident incident);

    /// <summary>An incident-response task was assigned (<c>irp.task_assigned</c>).</summary>
    Task IrpTaskAssignedAsync(IncidentResponsePlanTask task, string? assigneeName);

    /// <summary>A risk acceptance is nearing expiry (<c>riskacceptance.expiring</c>).</summary>
    Task RiskAcceptanceExpiringAsync(RiskAcceptance acceptance, int daysUntilExpiry, int coveredFindings);

    /// <summary>An external issue tracker changed a finding (<c>issuesync.applied</c>).</summary>
    Task IssueSyncAppliedAsync(Vulnerability finding, string connectionName, string issueKey,
        string externalStatus, IssueSyncAction action);

    // --- Track 8 (Risk Governance) --------------------------------------------------------------

    /// <summary>
    /// A risk's review is overdue or has never happened (<c>risk.review_overdue</c>, 8.5.1).
    /// </summary>
    /// <param name="daysOverdue">Days past the cadence, or 0 for a risk never reviewed at all.</param>
    Task RiskReviewOverdueAsync(Risk risk, double? score, int daysOverdue, DateTime? lastReviewed,
        int cadenceDays);

    /// <summary>A risk acceptance lapsed (<c>riskacceptance.expired</c>, 8.1.3).</summary>
    Task RiskAcceptanceExpiredAsync(RiskAcceptance acceptance, Risk? risk);

    /// <summary>A treatment task is due or overdue (<c>mitigationtask.due</c>, 8.5.3).</summary>
    Task MitigationTaskDueAsync(MitigationTask task, int riskId, int daysUntilDue);

    /// <summary>
    /// A business review campaign was assigned (<c>riskreview.campaign_assigned</c>, 8.6.3). The
    /// deep link is the point of this one: the reviewer should land on the campaign, not on a login
    /// screen and a hunt.
    /// </summary>
    Task RiskReviewCampaignAssignedAsync(RiskReviewCampaign campaign, int reviewerUserId, int itemCount);

    /// <summary>A campaign passed its due date (<c>riskreview.campaign_overdue</c>, 8.6.3).</summary>
    Task RiskReviewCampaignOverdueAsync(RiskReviewCampaign campaign, int pendingItems);

    /// <summary>A reviewer escalated a risk (<c>risk.escalated</c>, 8.6.4).</summary>
    Task RiskEscalatedAsync(Risk risk, double? score, int escalatedToUserId, string? note);

    // --- Track 4.6 (Jira Service Management) ----------------------------------------------------

    /// <summary>
    /// A mirrored Jira Service Management request breached an SLA metric
    /// (<c>jsm.sla_breached</c>, 4.6).
    ///
    /// Raised once per (request, metric, cycle) — a cycle that breached last week must not re-announce
    /// itself on every sync, because a channel that repeats itself gets muted and a muted channel is
    /// worse than none. The deep link is the Jira portal URL rather than a NetRisk route: the person
    /// who has to act on a service-desk breach acts on it in the service desk.
    /// </summary>
    Task JsmSlaBreachedAsync(string issueKey, string? summary, string metricName, string? requestUrl,
        string? reporter, long? remainingMs);
}
