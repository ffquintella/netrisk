using DAL.Enums;

namespace Model.Notifications;

/// <summary>
/// One notification, expressed in terms no provider owns (Track 4 milestone 4.1.1).
///
/// The whole point of the abstraction: the domain raises this, and each provider renders it its own
/// way — Block Kit for Slack, an Adaptive Card for Teams, HTML for email, documented JSON for a
/// webhook. If the domain produced Slack markdown, adding Teams would mean rewriting every caller.
/// </summary>
public class NotificationMessage
{
    public NotificationEventType EventType { get; set; }

    /// <summary>Normalized severity 1–4 (Low…Critical), or null for events that have none.</summary>
    public int? Severity { get; set; }

    /// <summary>One line. This is what a phone notification shows, so it has to stand alone.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>A short paragraph of context. Plain text — every provider escapes it differently.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Label/value pairs rendered as a field grid (Slack), a FactSet (Teams) or a table (email).
    /// Ordered, because "Severity" first and "Detected" last is the reading order people expect.
    /// </summary>
    public List<NotificationField> Fields { get; set; } = new();

    /// <summary>
    /// Deep link back to the NetRisk record. Every provider turns this into its "Open in NetRisk"
    /// button — an alert you cannot click through from is an alert that gets ignored.
    /// </summary>
    public string? Link { get; set; }

    /// <summary>What the notification is about, for the delivery log and for webhook consumers.</summary>
    public string? SubjectType { get; set; }

    public int? SubjectId { get; set; }

    /// <summary>Entity the subject belongs to, so entity-scoped subscriptions can filter on it.</summary>
    public int? EntityId { get; set; }

    /// <summary>When the underlying event happened, UTC. Not when it was delivered.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// How many underlying events this message summarizes. 1 for an immediate send; more for a
    /// digest, where providers add "and 41 others" rather than pretending it is one event.
    /// </summary>
    public int AggregatedCount { get; set; } = 1;

    /// <summary>Human severity name for rendering. Not persisted — derived from <see cref="Severity"/>.</summary>
    public string SeverityLabel => Severity switch
    {
        4 => "Critical",
        3 => "High",
        2 => "Medium",
        1 => "Low",
        _ => "Info"
    };
}

/// <summary>One label/value pair in a notification's field grid.</summary>
public class NotificationField
{
    public NotificationField()
    {
    }

    public NotificationField(string label, string? value)
    {
        Label = label;
        Value = value ?? string.Empty;
    }

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
