using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The delivery log (Track 4 milestone 4.1.3): one row per notification per subscription, carrying
/// what was sent, how many attempts it took, and the last error.
///
/// Persisted rather than logged to a file because the operator question — "the SLA breach fired, did
/// the team hear about it?" — has to be answerable from the admin UI, and because the retry job
/// needs a durable work queue. A failed send that leaves no row is indistinguishable from an event
/// that never happened.
/// </summary>
public class NotificationDelivery
{
    public int Id { get; set; }

    /// <summary>Null when the subscription was deleted after the attempt; the log outlives it.</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>The channel actually used. Differs from the subscription's when a fallback delivered it.</summary>
    public int? ChannelId { get; set; }

    public NotificationEventType EventType { get; set; }

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;

    /// <summary>Attempts made so far. The dispatcher gives up at three.</summary>
    public int Attempts { get; set; }

    /// <summary>Rendered title, kept for the log view so a row means something without the payload.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// The channel-agnostic message as JSON, so a retry does not have to reconstruct it from a
    /// domain object that may have changed in the meantime.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Last error text, truncated. Redacted of tokens before it is written: a 401 response body from
    /// a webhook has been known to echo the credential back.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>Severity of the event, for filtering the log.</summary>
    public int? Severity { get; set; }

    /// <summary>Kind and id of the record this notification is about, for the deep link.</summary>
    public string? SubjectType { get; set; }

    public int? SubjectId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// When a digesting subscription's window closes. The retry job ignores rows whose window has
    /// not elapsed, which is how batching is implemented without a second queue.
    /// </summary>
    public DateTime? DigestDueAt { get; set; }

    public virtual NotificationSubscription? Subscription { get; set; }

    public virtual NotificationChannel? Channel { get; set; }
}
