using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// "When <see cref="EventType"/> happens, and it is at least <see cref="MinSeverity"/>, tell
/// <see cref="Channel"/>" (Track 4 milestone 4.1.3).
///
/// One row per cell of the events × channels matrix the admin UI renders, which is why the filters
/// live here and not on the channel: the same Slack channel legitimately wants every Critical risk
/// and only a daily digest of imports.
/// </summary>
public class NotificationSubscription
{
    public int Id { get; set; }

    public NotificationEventType EventType { get; set; }

    public int ChannelId { get; set; }

    /// <summary>
    /// Minimum severity that passes the filter, on the 1–4 normalized scale (1 Low … 4 Critical).
    /// Null means "any severity", which is the right default for events that have none.
    /// </summary>
    public int? MinSeverity { get; set; }

    /// <summary>Restricts the subscription to one business entity. Null means every entity.</summary>
    public int? EntityId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Batch window in minutes. Null or 0 sends immediately; a positive value collects matching
    /// events and sends one summary, which is what keeps a 3000-finding import from being 3000
    /// Slack messages (and hitting the platform's ~1 msg/s rate limit for the next hour).
    /// </summary>
    public int? DigestWindowMinutes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual NotificationChannel? Channel { get; set; }

    public virtual Entity? Entity { get; set; }
}
