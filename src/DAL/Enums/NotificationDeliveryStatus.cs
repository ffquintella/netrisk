namespace DAL.Enums;

/// <summary>
/// Lifecycle of one attempt to deliver one notification (Track 4 milestone 4.1.3), persisted in
/// <c>notification_deliveries.status</c>.
///
/// The delivery log is the observability feature: "did the Slack alert actually go out" is the
/// question every operator asks first, and it cannot be answered from the absence of a message.
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Queued, not yet attempted.</summary>
    Pending = 1,

    /// <summary>Delivered by the subscription's primary channel.</summary>
    Delivered = 2,

    /// <summary>The primary channel failed and a channel further down the fallback chain delivered it.</summary>
    DeliveredByFallback = 3,

    /// <summary>Attempted, failed, and eligible for another attempt.</summary>
    Retrying = 4,

    /// <summary>Out of attempts. The last error is on the row.</summary>
    Failed = 5,

    /// <summary>Held for a digest window and superseded by the digest that went out.</summary>
    Batched = 6
}
