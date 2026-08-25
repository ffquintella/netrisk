using DAL.Entities;
using DAL.Enums;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Builds the canonical <see cref="NotificationMessage"/> for each catalog event and hands it to the
/// dispatcher (Track 4 milestone 4.1.3).
///
/// The deep-link base URL comes from configuration (<c>app:baseUrl</c>) with a per-channel override
/// applied by the providers, so a server that has not been told its own public URL produces
/// notifications without a button rather than notifications with a broken one.
/// </summary>
public class NotificationEventPublisher(
    ILogger logger,
    INotificationDispatcher dispatcher,
    Microsoft.Extensions.Configuration.IConfiguration configuration)
    : INotificationEventPublisher
{
    private string? BaseUrl => configuration["app:baseUrl"]?.TrimEnd('/');

    private string? Link(string path) => BaseUrl == null ? null : $"{BaseUrl}{path}";

    public Task RiskCreatedAsync(Risk risk, double? score = null) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskCreated,
            Severity = SeverityFromScore(score),
            Title = $"New risk: {risk.Subject}",
            Body = Shorten(risk.Notes, 500),
            Fields =
            [
                new NotificationField("Risk", $"#{risk.Id}"),
                new NotificationField("Reference", risk.ReferenceId),
                new NotificationField("Status", risk.Status),
                new NotificationField("Score", Format(score)),
                new NotificationField("Severity", LabelOf(SeverityFromScore(score)))
            ],
            Link = Link($"/risks/{risk.Id}"),
            SubjectType = "risk",
            SubjectId = risk.Id,
            EntityId = risk.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskSeverityChangedAsync(Risk risk, double? previousScore, double? newScore) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskSeverityChanged,
            Severity = SeverityFromScore(newScore),
            Title = $"Risk severity changed: {risk.Subject}",
            Body = $"The risk score moved from {Format(previousScore)} to {Format(newScore)}.",
            Fields =
            [
                new NotificationField("Risk", $"#{risk.Id}"),
                new NotificationField("Previous", Format(previousScore)),
                new NotificationField("Current", Format(newScore)),
                new NotificationField("Severity", LabelOf(SeverityFromScore(newScore)))
            ],
            Link = Link($"/risks/{risk.Id}"),
            SubjectType = "risk",
            SubjectId = risk.Id,
            EntityId = risk.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task VulnerabilityImportedAsync(ScanImport import) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.VulnerabilityImported,
            // The import's own severity is the worst thing it brought in, which is what a subscriber
            // filtering on "Critical only" means by it.
            Severity = HighestSeverityIn(import.NewBySeverity),
            Title = $"Scan import finished: {import.Importer}",
            Body = $"{import.NewCount} new, {import.UpdatedCount} updated, {import.ClosedCount} closed, "
                   + $"{import.DuplicateCount} duplicate.",
            Fields =
            [
                new NotificationField("Import", $"#{import.Id}"),
                new NotificationField("Importer", import.Importer),
                new NotificationField("File", import.FileName ?? "—"),
                new NotificationField("New", import.NewCount.ToString()),
                new NotificationField("New by severity", import.NewBySeverity ?? "—"),
                new NotificationField("Status", import.Status.ToString())
            ],
            Link = Link($"/vulnerabilities/import-jobs/{import.Id}"),
            SubjectType = "scan_import",
            SubjectId = import.Id,
            EntityId = import.EntityId,
            OccurredAt = import.FinishedAt ?? DateTime.UtcNow
        });

    public Task FindingStatusChangedAsync(Vulnerability finding, FindingStatus? from, FindingStatus to,
        FindingStatusChangeSource source, string? justification = null) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.FindingStatusChanged,
            Severity = SeverityFromFinding(finding),
            Title = $"Finding {(from == null ? "created" : $"{from} → {to}")}: {finding.Title}",
            Body = justification ?? Shorten(finding.Description, 400),
            Fields =
            [
                new NotificationField("Finding", $"#{finding.Id}"),
                new NotificationField("From", from?.ToString() ?? "—"),
                new NotificationField("To", to.ToString()),
                new NotificationField("Changed by", source.ToString()),
                new NotificationField("Severity", finding.Severity ?? "—")
            ],
            Link = Link($"/vulnerabilities/{finding.Id}"),
            SubjectType = "finding",
            SubjectId = finding.Id,
            EntityId = finding.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task SlaAsync(int findingId, string title, string? severityLabel, int? severity,
        DateTime dueDate, int thresholdDays, int daysOverdue, int? entityId) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = thresholdDays == 0
                ? NotificationEventType.SlaBreached
                : NotificationEventType.SlaApproaching,
            Severity = severity,
            Title = thresholdDays == 0
                ? $"SLA breached: {title}"
                : $"SLA due in {thresholdDays} day(s): {title}",
            Body = thresholdDays == 0
                ? $"This finding passed its remediation deadline of {dueDate:yyyy-MM-dd} "
                  + $"{daysOverdue} day(s) ago."
                : $"This finding is due for remediation on {dueDate:yyyy-MM-dd}.",
            Fields =
            [
                new NotificationField("Finding", $"#{findingId}"),
                new NotificationField("Severity", severityLabel ?? LabelOf(severity)),
                new NotificationField("Due", dueDate.ToString("yyyy-MM-dd")),
                new NotificationField(thresholdDays == 0 ? "Days overdue" : "Days remaining",
                    (thresholdDays == 0 ? daysOverdue : thresholdDays).ToString())
            ],
            Link = Link($"/vulnerabilities/{findingId}"),
            SubjectType = "finding",
            SubjectId = findingId,
            EntityId = entityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task IncidentCreatedAsync(Incident incident) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.IncidentCreated,
            Severity = null,
            Title = $"Incident opened: {incident.Name}",
            Body = Shorten(incident.Description, 500),
            Fields =
            [
                new NotificationField("Incident", $"#{incident.Id}"),
                new NotificationField("Status", incident.Status.ToString()),
                new NotificationField("Reported", incident.CreationDate.ToString("yyyy-MM-dd HH:mm"))
            ],
            Link = Link($"/incidents/{incident.Id}"),
            SubjectType = "incident",
            SubjectId = incident.Id,
            EntityId = incident.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task IrpTaskAssignedAsync(IncidentResponsePlanTask task, string? assigneeName) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.IrpTaskAssigned,
            Severity = null,
            Title = $"IRP task assigned: {task.Name}",
            Body = Shorten(task.Description, 400),
            Fields =
            [
                new NotificationField("Task", $"#{task.Id}"),
                new NotificationField("Plan", task.PlanId.ToString()),
                new NotificationField("Assignee", assigneeName ?? "—"),
                new NotificationField("Status", task.Status.ToString())
            ],
            Link = Link($"/incident-response-plans/{task.PlanId}"),
            SubjectType = "irp_task",
            SubjectId = task.Id,
            EntityId = null,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskAcceptanceExpiringAsync(RiskAcceptance acceptance, int daysUntilExpiry,
        int coveredFindings) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskAcceptanceExpiring,
            Severity = null,
            Title = $"Risk acceptance expiring in {daysUntilExpiry} day(s): {acceptance.Name}",
            Body = $"{coveredFindings} finding(s) will be reactivated when this acceptance lapses on "
                   + $"{acceptance.ExpiresAt:yyyy-MM-dd}.",
            Fields =
            [
                new NotificationField("Acceptance", $"#{acceptance.Id}"),
                new NotificationField("Expires", acceptance.ExpiresAt.ToString("yyyy-MM-dd")),
                new NotificationField("Findings covered", coveredFindings.ToString()),
                new NotificationField("Authorized by", acceptance.AuthorizingManagerId.ToString())
            ],
            Link = Link($"/risk-acceptances/{acceptance.Id}"),
            SubjectType = "risk_acceptance",
            SubjectId = acceptance.Id,
            EntityId = acceptance.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task IssueSyncAppliedAsync(Vulnerability finding, string connectionName, string issueKey,
        string externalStatus, IssueSyncAction action) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.IssueSyncApplied,
            Severity = SeverityFromFinding(finding),
            Title = $"{connectionName} {issueKey} → {action}: {finding.Title}",
            Body = $"The linked issue moved to '{externalStatus}', which this connection maps to {action}.",
            Fields =
            [
                new NotificationField("Finding", $"#{finding.Id}"),
                new NotificationField("Tracker", connectionName),
                new NotificationField("Issue", issueKey),
                new NotificationField("External status", externalStatus),
                new NotificationField("Action", action.ToString())
            ],
            Link = Link($"/vulnerabilities/{finding.Id}"),
            SubjectType = "finding",
            SubjectId = finding.Id,
            EntityId = finding.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    // --- helpers ----------------------------------------------------------------------------

    private async Task SafeDispatch(NotificationMessage message)
    {
        try
        {
            await dispatcher.DispatchAsync(message);
        }
        catch (Exception ex)
        {
            // The dispatcher already swallows delivery failures; this catches the case where it
            // cannot even reach the database, so raising an event is never a way to fail a write.
            logger.Error(ex, "Could not publish the {Event} notification: {Message}",
                message.EventType, ex.Message);
        }
    }

    /// <summary>
    /// Maps a finding's severity text onto the 1–4 scale. The importers already normalize severity
    /// into this vocabulary, so a string comparison is enough and does not need the CVSS score.
    /// </summary>
    internal static int? SeverityFromFinding(Vulnerability finding) =>
        (finding.Severity ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => null
        };

    /// <summary>
    /// A risk's 0–10 score mapped onto the 1–4 notification scale, using the same cut points the
    /// register's colour bands use so a "Critical risk" notification means what the UI calls critical.
    /// </summary>
    internal static int? SeverityFromScore(double? score) => score switch
    {
        null => null,
        >= 8 => 4,
        >= 6 => 3,
        >= 3 => 2,
        _ => 1
    };

    /// <summary>
    /// The worst severity named in a <c>new_by_severity</c> summary such as
    /// <c>critical=2;high=7;medium=31</c>. Parsed rather than assumed, because an import with no
    /// criticals should not notify a critical-only subscription.
    /// </summary>
    internal static int? HighestSeverityIn(string? newBySeverity)
    {
        if (string.IsNullOrWhiteSpace(newBySeverity)) return null;

        var lowered = newBySeverity.ToLowerInvariant();

        foreach (var (name, level) in new[] { ("critical", 4), ("high", 3), ("medium", 2), ("low", 1) })
        {
            var index = lowered.IndexOf(name, StringComparison.Ordinal);
            if (index < 0) continue;

            // "critical=0" is not a critical import, so the count is read rather than the presence
            // of the word.
            var rest = lowered[(index + name.Length)..].TrimStart('=', ':', ' ');
            var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());

            if (digits.Length == 0 || (int.TryParse(digits, out var count) && count > 0)) return level;
        }

        return null;
    }

    private static string LabelOf(int? severity) => severity switch
    {
        4 => "Critical",
        3 => "High",
        2 => "Medium",
        1 => "Low",
        _ => "Info"
    };

    private static string Format(double? score) => score?.ToString("0.0") ?? "—";

    private static string Shorten(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = text.Trim();
        return clean.Length <= max ? clean : clean[..(max - 1)] + "…";
    }
}
