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
}
