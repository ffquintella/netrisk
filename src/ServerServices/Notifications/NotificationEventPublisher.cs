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

    // --- Track 8 (Risk Governance) --------------------------------------------------------------

    public Task RiskReviewOverdueAsync(Risk risk, double? score, int daysOverdue,
        DateTime? lastReviewed, int cadenceDays) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskReviewOverdue,
            Severity = SeverityFromScore(score),
            Title = daysOverdue > 0
                ? $"Risk review {daysOverdue} day(s) overdue: {risk.Subject}"
                : $"Risk has never been reviewed: {risk.Subject}",
            Body = lastReviewed == null
                ? "This risk has no management review on record. Its severity band sets a review "
                  + $"cadence of {cadenceDays} day(s)."
                : $"Last reviewed on {lastReviewed:yyyy-MM-dd}. The cadence for this severity band is "
                  + $"{cadenceDays} day(s).",
            Fields =
            [
                new NotificationField("Risk", $"#{risk.Id}"),
                new NotificationField("Reference", risk.ReferenceId),
                new NotificationField("Status", risk.Status),
                new NotificationField("Score", Format(score)),
                new NotificationField("Last reviewed", lastReviewed?.ToString("yyyy-MM-dd") ?? "never"),
                new NotificationField("Days overdue", daysOverdue.ToString())
            ],
            Link = Link($"/risks/{risk.Id}"),
            SubjectType = "risk",
            SubjectId = risk.Id,
            EntityId = risk.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskAcceptanceExpiredAsync(RiskAcceptance acceptance, Risk? risk) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskAcceptanceExpired,
            Severity = SeverityFromScore(acceptance.ResidualScoreSnapshot),
            Title = $"Risk acceptance expired: {acceptance.Name}",
            Body = $"The acceptance lapsed on {acceptance.ExpiresAt:yyyy-MM-dd}. "
                   + (risk == null
                       ? "Whatever it covered needs re-triage or a renewed acceptance."
                       : $"Risk '{risk.Subject}' is flagged for review again."),
            Fields =
            [
                new NotificationField("Acceptance", $"#{acceptance.Id}"),
                new NotificationField("Risk", risk == null ? "—" : $"#{risk.Id}"),
                new NotificationField("Expired", acceptance.ExpiresAt.ToString("yyyy-MM-dd")),
                new NotificationField("Residual at acceptance", Format(acceptance.ResidualScoreSnapshot)),
                new NotificationField("Authorized by", acceptance.AuthorizingManagerId.ToString())
            ],
            Link = risk == null ? null : Link($"/risks/{risk.Id}"),
            SubjectType = "risk_acceptance",
            SubjectId = acceptance.Id,
            EntityId = acceptance.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task MitigationTaskDueAsync(MitigationTask task, int riskId, int daysUntilDue) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.MitigationTaskDue,
            // Not severity-filtered in the catalog, so this only colours the message.
            Severity = daysUntilDue < 0 ? 3 : 2,
            Title = daysUntilDue < 0
                ? $"Treatment task {-daysUntilDue} day(s) overdue: {task.Title}"
                : $"Treatment task due in {daysUntilDue} day(s): {task.Title}",
            Body = Shorten(task.Description, 500),
            Fields =
            [
                new NotificationField("Task", $"#{task.Id}"),
                new NotificationField("Risk", $"#{riskId}"),
                new NotificationField("Owner", task.OwnerId?.ToString() ?? "unassigned"),
                new NotificationField("Due", task.DueDate?.ToString("yyyy-MM-dd") ?? "—"),
                new NotificationField("Status", task.Status.ToString())
            ],
            Link = Link($"/risks/{riskId}"),
            SubjectType = "mitigation_task",
            SubjectId = task.Id,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskReviewCampaignAssignedAsync(RiskReviewCampaign campaign, int reviewerUserId,
        int itemCount) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskReviewCampaignAssigned,
            Severity = null,
            Title = $"Risk review assigned: {campaign.Name}",
            Body = $"{itemCount} risk(s) to review by {campaign.DueDate:yyyy-MM-dd}.",
            Fields =
            [
                new NotificationField("Campaign", $"#{campaign.Id}"),
                new NotificationField("Period",
                    $"{campaign.PeriodStart:yyyy-MM-dd} to {campaign.PeriodEnd:yyyy-MM-dd}"),
                new NotificationField("Due", campaign.DueDate.ToString("yyyy-MM-dd")),
                new NotificationField("Risks", itemCount.ToString()),
                new NotificationField("Reviewer", reviewerUserId.ToString())
            ],
            // The portal's own route, not the desktop app's: this notification goes to a business
            // reviewer who does not have the desktop client installed, and a link they cannot open is
            // worse than no link.
            Link = Link($"/portal/campaigns/{campaign.Id}"),
            SubjectType = "risk_review_campaign",
            SubjectId = campaign.Id,
            EntityId = campaign.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskReviewCampaignOverdueAsync(RiskReviewCampaign campaign, int pendingItems) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskReviewCampaignOverdue,
            Severity = 3,
            Title = $"Risk review overdue: {campaign.Name}",
            Body = $"The review was due on {campaign.DueDate:yyyy-MM-dd} and {pendingItems} risk(s) "
                   + "still have no decision.",
            Fields =
            [
                new NotificationField("Campaign", $"#{campaign.Id}"),
                new NotificationField("Due", campaign.DueDate.ToString("yyyy-MM-dd")),
                new NotificationField("Undecided", pendingItems.ToString())
            ],
            Link = Link($"/portal/campaigns/{campaign.Id}"),
            SubjectType = "risk_review_campaign",
            SubjectId = campaign.Id,
            EntityId = campaign.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task RiskEscalatedAsync(Risk risk, double? score, int escalatedToUserId, string? note) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.RiskEscalated,
            Severity = SeverityFromScore(score),
            Title = $"Risk escalated for decision: {risk.Subject}",
            Body = string.IsNullOrWhiteSpace(note)
                ? "A business reviewer escalated this risk rather than deciding it."
                : Shorten(note, 500),
            Fields =
            [
                new NotificationField("Risk", $"#{risk.Id}"),
                new NotificationField("Score", Format(score)),
                new NotificationField("Escalated to", escalatedToUserId.ToString())
            ],
            Link = Link($"/risks/{risk.Id}"),
            SubjectType = "risk",
            SubjectId = risk.Id,
            EntityId = risk.EntityId,
            OccurredAt = DateTime.UtcNow
        });

    public Task JsmSlaBreachedAsync(string issueKey, string? summary, string metricName,
        string? requestUrl, string? reporter, long? remainingMs) =>
        SafeDispatch(new NotificationMessage
        {
            EventType = NotificationEventType.JsmSlaBreached,
            // Fixed at 3 rather than derived: an SLA goal is the customer's own definition of urgent,
            // and NetRisk has no severity scale to map a service-desk metric onto without inventing
            // one.
            Severity = 3,
            Title = $"Jira SLA breached on {issueKey}: {metricName}",
            Body = string.IsNullOrWhiteSpace(summary)
                ? $"The '{metricName}' goal was passed."
                : $"The '{metricName}' goal was passed on: {Shorten(summary, 500)}",
            Fields =
            [
                new NotificationField("Request", issueKey),
                new NotificationField("Metric", metricName),
                new NotificationField("Overdue by", Overdue(remainingMs)),
                new NotificationField("Reporter", reporter ?? "—")
            ],
            // The Jira portal URL, not a NetRisk route: this is somebody else's ticket and it gets
            // resolved in their tool. Falls back to no link rather than to a NetRisk page that would
            // show nothing useful.
            Link = requestUrl,
            SubjectType = "jira_service_request",
            SubjectId = 0,
            OccurredAt = DateTime.UtcNow
        });

    /// <summary>
    /// How far past the goal, from Jira's remaining time — which goes negative once the goal is
    /// passed, so the sign is flipped here rather than shown to a reader as "-2h".
    /// </summary>
    private static string Overdue(long? remainingMs)
    {
        if (remainingMs == null) return "unknown";

        var overdue = TimeSpan.FromMilliseconds(Math.Abs(remainingMs.Value));

        return overdue.TotalHours >= 1
            ? $"{(int)overdue.TotalHours}h {overdue.Minutes}m"
            : $"{overdue.Minutes}m";
    }

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
