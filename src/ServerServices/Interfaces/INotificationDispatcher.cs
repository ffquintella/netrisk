using DAL.Entities;
using DAL.Enums;
using Model.Notifications;

namespace ServerServices.Interfaces;

/// <summary>
/// Turns a raised domain event into deliveries (Track 4 milestone 4.1.1/4.1.3).
///
/// The dispatcher owns everything that is not rendering: subscription matching, the digest window,
/// retry with exponential backoff, the ordered fallback chain, and the delivery log. Providers stay
/// stateless because of it, which is what makes "add Discord" a hundred lines rather than a redesign.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Queues <paramref name="message"/> for every subscription that matches, and attempts immediate
    /// delivery for those with no digest window. Returns the delivery rows created.
    ///
    /// Never throws for a delivery failure: a notification that cannot be sent must not fail the
    /// domain operation that raised it. Creating a risk has to succeed even when Slack is down.
    /// </summary>
    Task<List<NotificationDelivery>> DispatchAsync(NotificationMessage message, CancellationToken ct = default);

    /// <summary>
    /// Retries deliveries that are pending or mid-retry and whose backoff has elapsed, and closes
    /// digest windows that are due. This is the background job's entry point.
    /// </summary>
    Task<DispatchSweepResult> ProcessPendingAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Sends the admin UI's test message through one channel, without creating a delivery row — a
    /// test is a diagnostic, and filling the delivery log with tests makes the log less useful.
    /// </summary>
    Task<ChannelTestResult> TestChannelAsync(int channelId, CancellationToken ct = default);
}

/// <summary>What one sweep of the delivery queue did.</summary>
public class DispatchSweepResult
{
    public int Delivered { get; set; }

    public int Retried { get; set; }

    public int Failed { get; set; }

    /// <summary>Deliveries that went out through a fallback channel because the primary kept failing.</summary>
    public int DeliveredByFallback { get; set; }

    /// <summary>Digest windows closed on this sweep, each having sent one summary message.</summary>
    public int DigestsSent { get; set; }
}
