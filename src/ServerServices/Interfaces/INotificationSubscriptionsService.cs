using DAL.Entities;
using DAL.Enums;
using Model.Notifications;

namespace ServerServices.Interfaces;

/// <summary>
/// Administration of channels, subscriptions and the delivery log
/// (Track 4 milestones 4.1.2 and 4.1.3).
///
/// One service for the three because they are one screen and one job: nobody configures a channel
/// without subscribing something to it, and nobody debugs a subscription without reading the
/// delivery log.
/// </summary>
public interface INotificationSubscriptionsService
{
    // --- channels ---------------------------------------------------------------------------

    /// <summary>
    /// Channels with their secrets redacted. There is deliberately no method that returns a channel
    /// with its webhook URL in clear: the only consumer that needs it is the dispatcher, which reads
    /// the row itself.
    /// </summary>
    Task<List<NotificationChannel>> GetChannelsAsync(bool includeDisabled = true);

    Task<NotificationChannel> GetChannelAsync(int id);

    /// <summary>
    /// Creates a channel, encrypting every secret in its configuration.
    ///
    /// Throws <see cref="Model.Exceptions.InvalidParameterException"/> for a duplicate name, an
    /// unknown kind, or a fallback chain that loops.
    /// </summary>
    Task<NotificationChannel> CreateChannelAsync(NotificationChannel channel, int? userId);

    /// <summary>
    /// Updates a channel. A secret left at the redaction placeholder keeps its stored value, which is
    /// what lets the admin form round-trip without the client ever holding the real token.
    /// </summary>
    Task<NotificationChannel> UpdateChannelAsync(NotificationChannel channel, int? userId);

    Task DeleteChannelAsync(int id);

    // --- subscriptions ----------------------------------------------------------------------

    Task<List<NotificationSubscription>> GetSubscriptionsAsync();

    /// <summary>
    /// Every subscription that should hear about <paramref name="eventType"/> at this severity and
    /// entity. The dispatcher's matching rule, exposed so it can be asserted directly.
    /// </summary>
    Task<List<NotificationSubscription>> MatchAsync(NotificationEventType eventType, int? severity, int? entityId);

    Task<NotificationSubscription> CreateSubscriptionAsync(NotificationSubscription subscription);

    Task<NotificationSubscription> UpdateSubscriptionAsync(NotificationSubscription subscription);

    Task DeleteSubscriptionAsync(int id);

    // --- delivery log -----------------------------------------------------------------------

    /// <summary>
    /// Recent deliveries, newest first, optionally filtered to one status. The observability half of
    /// the milestone: "did it go out" is answered here.
    /// </summary>
    Task<List<NotificationDelivery>> GetDeliveriesAsync(int limit = 200,
        NotificationDeliveryStatus? status = null, int? subscriptionId = null);

    /// <summary>
    /// Resets a failed delivery to pending so the next sweep tries it again — the "resend" button,
    /// which is what an operator wants after fixing a webhook URL.
    /// </summary>
    Task<NotificationDelivery> RequeueDeliveryAsync(int id);

    /// <summary>Deletes delivery rows older than <paramref name="olderThanDays"/>. Called by the cleanup job.</summary>
    Task<int> PurgeDeliveriesAsync(int olderThanDays);
}
