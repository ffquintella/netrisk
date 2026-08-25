using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Notifications;

/// <summary>
/// Channel, subscription and delivery-log administration
/// (Track 4 milestones 4.1.2 and 4.1.3).
/// </summary>
public class NotificationSubscriptionsService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    INotificationChannelRegistry registry)
    : ServiceBase(logger, dalService), INotificationSubscriptionsService
{
    /// <summary>Deliveries kept before the cleanup job removes them. Ninety days covers a quarter's audit.</summary>
    public const int DefaultDeliveryRetentionDays = 90;

    public async Task<List<NotificationChannel>> GetChannelsAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var channels = await db.NotificationChannels
            .Where(c => includeDisabled || c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        foreach (var channel in channels) Redact(channel);

        return channels;
    }

    public async Task<NotificationChannel> GetChannelAsync(int id)
    {
        await using var db = DalService.GetContext();

        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new DataNotFoundException("notification_channels", id.ToString(),
                          new Exception($"Notification channel {id} was not found."));

        Redact(channel);
        return channel;
    }

    public async Task<NotificationChannel> CreateChannelAsync(NotificationChannel channel, int? userId)
    {
        if (channel == null) throw new InvalidParameterException(nameof(channel), "A channel is required.");
        Validate(channel);

        await using var db = DalService.GetContext();

        if (await db.NotificationChannels.AnyAsync(c => c.Name == channel.Name))
            throw new InvalidParameterException(nameof(channel.Name),
                $"A notification channel named '{channel.Name}' already exists.");

        await ValidateFallbackAsync(db, channel.FallbackChannelId, null);

        var stored = new NotificationChannel
        {
            Name = channel.Name.Trim(),
            Kind = channel.Kind,
            ConfigurationJson = ProtectConfiguration(channel.ConfigurationJson, null),
            SecretsEncrypted = true,
            Enabled = channel.Enabled,
            FallbackChannelId = channel.FallbackChannelId,
            CreatedAt = DateTime.UtcNow,
            CreatedById = userId
        };

        db.NotificationChannels.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Notification channel {Name} ({Kind}) created by user {User}",
            stored.Name, stored.Kind, userId);

        Redact(stored);
        return stored;
    }

    public async Task<NotificationChannel> UpdateChannelAsync(NotificationChannel channel, int? userId)
    {
        if (channel == null) throw new InvalidParameterException(nameof(channel), "A channel is required.");
        Validate(channel);

        await using var db = DalService.GetContext();

        var stored = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == channel.Id)
                     ?? throw new DataNotFoundException("notification_channels", channel.Id.ToString(),
                         new Exception($"Notification channel {channel.Id} was not found."));

        if (await db.NotificationChannels.AnyAsync(c => c.Name == channel.Name && c.Id != channel.Id))
            throw new InvalidParameterException(nameof(channel.Name),
                $"A notification channel named '{channel.Name}' already exists.");

        await ValidateFallbackAsync(db, channel.FallbackChannelId, channel.Id);

        stored.Name = channel.Name.Trim();
        stored.Kind = channel.Kind;
        stored.ConfigurationJson = ProtectConfiguration(channel.ConfigurationJson, stored.ConfigurationJson);
        stored.SecretsEncrypted = true;
        stored.Enabled = channel.Enabled;
        stored.FallbackChannelId = channel.FallbackChannelId;
        stored.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        Logger.Information("Notification channel {Id} updated by user {User}", stored.Id, userId);

        Redact(stored);
        return stored;
    }

    public async Task DeleteChannelAsync(int id)
    {
        await using var db = DalService.GetContext();

        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new DataNotFoundException("notification_channels", id.ToString(),
                          new Exception($"Notification channel {id} was not found."));

        // Refused rather than silently orphaning: a channel that other channels fall back to is
        // load-bearing, and the operator has to decide what replaces it.
        var dependents = await db.NotificationChannels.CountAsync(c => c.FallbackChannelId == id);
        if (dependents > 0)
            throw new InvalidParameterException(nameof(id),
                $"{dependents} channel(s) fall back to this one. Change their fallback first.");

        db.NotificationChannels.Remove(channel);
        await db.SaveChangesAsync();

        Logger.Information("Notification channel {Id} ({Name}) deleted", id, channel.Name);
    }

    public async Task<List<NotificationSubscription>> GetSubscriptionsAsync()
    {
        await using var db = DalService.GetContext();

        return await db.NotificationSubscriptions
            .OrderBy(s => s.EventType).ThenBy(s => s.ChannelId)
            .ToListAsync();
    }

    public async Task<List<NotificationSubscription>> MatchAsync(NotificationEventType eventType,
        int? severity, int? entityId)
    {
        await using var db = DalService.GetContext();

        var candidates = await db.NotificationSubscriptions
            .Include(s => s.Channel)
            .Where(s => s.EventType == eventType && s.Enabled)
            .ToListAsync();

        return candidates.Where(s => Matches(s, severity, entityId)).ToList();
    }

    /// <summary>
    /// The matching rule, in one place so the dispatcher and the admin preview agree.
    ///
    /// An event with no severity passes a severity filter rather than failing it: an incident has no
    /// severity band, and a subscription that asks for "Critical and above" should still hear about
    /// incidents rather than silently dropping them. The alternative — treating null as below the
    /// threshold — is how a subscriber discovers months later that they never got incident alerts.
    /// </summary>
    internal static bool Matches(NotificationSubscription subscription, int? severity, int? entityId)
    {
        if (subscription.Channel is { Enabled: false }) return false;

        if (subscription.MinSeverity != null && severity != null && severity < subscription.MinSeverity)
            return false;

        // An entity-scoped subscription wants only that entity. An event with no entity is global and
        // reaches only the unscoped subscriptions.
        if (subscription.EntityId != null && subscription.EntityId != entityId) return false;

        return true;
    }

    public async Task<NotificationSubscription> CreateSubscriptionAsync(NotificationSubscription subscription)
    {
        if (subscription == null)
            throw new InvalidParameterException(nameof(subscription), "A subscription is required.");

        ValidateSubscription(subscription);

        await using var db = DalService.GetContext();

        if (!await db.NotificationChannels.AnyAsync(c => c.Id == subscription.ChannelId))
            throw new InvalidParameterException(nameof(subscription.ChannelId),
                $"Notification channel {subscription.ChannelId} was not found.");

        var stored = new NotificationSubscription
        {
            EventType = subscription.EventType,
            ChannelId = subscription.ChannelId,
            MinSeverity = subscription.MinSeverity,
            EntityId = subscription.EntityId,
            Enabled = subscription.Enabled,
            DigestWindowMinutes = subscription.DigestWindowMinutes,
            CreatedAt = DateTime.UtcNow
        };

        db.NotificationSubscriptions.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Subscription created: {Event} -> channel {Channel}",
            stored.EventType, stored.ChannelId);

        return stored;
    }

    public async Task<NotificationSubscription> UpdateSubscriptionAsync(NotificationSubscription subscription)
    {
        if (subscription == null)
            throw new InvalidParameterException(nameof(subscription), "A subscription is required.");

        ValidateSubscription(subscription);

        await using var db = DalService.GetContext();

        var stored = await db.NotificationSubscriptions.FirstOrDefaultAsync(s => s.Id == subscription.Id)
                     ?? throw new DataNotFoundException("notification_subscriptions", subscription.Id.ToString(),
                         new Exception($"Subscription {subscription.Id} was not found."));

        if (!await db.NotificationChannels.AnyAsync(c => c.Id == subscription.ChannelId))
            throw new InvalidParameterException(nameof(subscription.ChannelId),
                $"Notification channel {subscription.ChannelId} was not found.");

        stored.EventType = subscription.EventType;
        stored.ChannelId = subscription.ChannelId;
        stored.MinSeverity = subscription.MinSeverity;
        stored.EntityId = subscription.EntityId;
        stored.Enabled = subscription.Enabled;
        stored.DigestWindowMinutes = subscription.DigestWindowMinutes;
        stored.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return stored;
    }

    public async Task DeleteSubscriptionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await db.NotificationSubscriptions.FirstOrDefaultAsync(s => s.Id == id)
                     ?? throw new DataNotFoundException("notification_subscriptions", id.ToString(),
                         new Exception($"Subscription {id} was not found."));

        db.NotificationSubscriptions.Remove(stored);
        await db.SaveChangesAsync();
    }

    public async Task<List<NotificationDelivery>> GetDeliveriesAsync(int limit = 200,
        NotificationDeliveryStatus? status = null, int? subscriptionId = null)
    {
        await using var db = DalService.GetContext();

        var query = db.NotificationDeliveries.AsQueryable();

        if (status != null) query = query.Where(d => d.Status == status);
        if (subscriptionId != null) query = query.Where(d => d.SubscriptionId == subscriptionId);

        return await query
            .OrderByDescending(d => d.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync();
    }

    public async Task<NotificationDelivery> RequeueDeliveryAsync(int id)
    {
        await using var db = DalService.GetContext();

        var delivery = await db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == id)
                       ?? throw new DataNotFoundException("notification_deliveries", id.ToString(),
                           new Exception($"Delivery {id} was not found."));

        if (delivery.Status == NotificationDeliveryStatus.Delivered
            || delivery.Status == NotificationDeliveryStatus.DeliveredByFallback)
            throw new InvalidParameterException(nameof(id),
                "This notification was already delivered; re-sending it would duplicate the alert.");

        // Attempts reset with it: the point of a requeue is that the operator changed something, so
        // the new attempt deserves the full three tries rather than the one it has left.
        delivery.Status = NotificationDeliveryStatus.Pending;
        delivery.Attempts = 0;
        delivery.LastError = null;
        delivery.DigestDueAt = null;

        await db.SaveChangesAsync();

        Logger.Information("Delivery {Id} requeued", id);

        return delivery;
    }

    public async Task<int> PurgeDeliveriesAsync(int olderThanDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, olderThanDays));

        var removed = 0;

        // Batched load-and-remove rather than ExecuteDelete. A set-based delete would be faster, but it
        // is unsupported by EF's in-memory provider, and having the retention job be the one thing that
        // cannot be covered by a service test is a bad trade for a query that runs once a day.
        while (true)
        {
            await using var db = DalService.GetContext();

            var batch = await db.NotificationDeliveries
                .Where(d => d.CreatedAt < cutoff)
                .OrderBy(d => d.Id)
                .Take(500)
                .ToListAsync();

            if (batch.Count == 0) return removed;

            db.NotificationDeliveries.RemoveRange(batch);
            await db.SaveChangesAsync();

            removed += batch.Count;
        }
    }

    // --- helpers ----------------------------------------------------------------------------

    private void Validate(NotificationChannel channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw new InvalidParameterException(nameof(channel.Name), "A notification channel requires a name.");

        if (registry.For(channel.Kind) == null)
            throw new InvalidParameterException(nameof(channel.Kind),
                $"No provider is registered for channel kind {channel.Kind}. Available: "
                + string.Join(", ", registry.All.Select(c => c.Kind)));

        var configuration = ChannelConfiguration.Parse(channel.ConfigurationJson);

        // Validated per kind here rather than inside the provider so a channel cannot be saved in a
        // state whose only symptom is a permanently failing delivery.
        switch (channel.Kind)
        {
            case NotificationChannelKind.Email
                when EmailNotificationChannel.ParseRecipients(configuration.Recipients).Count == 0:
                throw new InvalidParameterException(nameof(channel.ConfigurationJson),
                    "An email channel requires at least one recipient.");

            case NotificationChannelKind.Slack or NotificationChannelKind.Teams or NotificationChannelKind.Webhook
                when string.IsNullOrWhiteSpace(configuration.WebhookUrl):
                throw new InvalidParameterException(nameof(channel.ConfigurationJson),
                    $"A {channel.Kind} channel requires a webhook URL.");
        }

        if (configuration.WebhookUrl != null
            && !ChannelConfiguration.RedactedPlaceholder.Equals(configuration.WebhookUrl)
            && !Uri.TryCreate(configuration.WebhookUrl, UriKind.Absolute, out var uri))
            throw new InvalidParameterException(nameof(channel.ConfigurationJson),
                "The webhook URL is not a valid absolute URL.");
        else if (configuration.WebhookUrl != null
                 && !ChannelConfiguration.RedactedPlaceholder.Equals(configuration.WebhookUrl)
                 && Uri.TryCreate(configuration.WebhookUrl, UriKind.Absolute, out var parsed)
                 && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
            throw new InvalidParameterException(nameof(channel.ConfigurationJson),
                "The webhook URL must be http or https.");
    }

    private static void ValidateSubscription(NotificationSubscription subscription)
    {
        if (!Enum.IsDefined(subscription.EventType))
            throw new InvalidParameterException(nameof(subscription.EventType),
                $"Unknown event type. Available: "
                + string.Join(", ", NotificationCatalog.Events.Select(e => e.Name)));

        if (subscription.MinSeverity is < 1 or > 4)
            throw new InvalidParameterException(nameof(subscription.MinSeverity),
                "Minimum severity must be between 1 (Low) and 4 (Critical), or unset for any.");

        if (subscription.DigestWindowMinutes is < 0 or > 1440)
            throw new InvalidParameterException(nameof(subscription.DigestWindowMinutes),
                "A digest window must be between 0 (immediate) and 1440 minutes.");
    }

    /// <summary>
    /// Walks the fallback chain looking for a cycle. A cycle is not a resilience configuration: the
    /// dispatcher would follow it until it ran out of channels, and every delivery would attempt the
    /// same failing channel repeatedly.
    /// </summary>
    private static async Task ValidateFallbackAsync(DAL.Context.AuditableContext db, int? fallbackId, int? selfId)
    {
        if (fallbackId == null) return;

        if (fallbackId == selfId)
            throw new InvalidParameterException(nameof(NotificationChannel.FallbackChannelId),
                "A channel cannot fall back to itself.");

        var chain = await db.NotificationChannels
            .Select(c => new { c.Id, c.FallbackChannelId })
            .ToDictionaryAsync(c => c.Id, c => c.FallbackChannelId);

        if (!chain.ContainsKey(fallbackId.Value))
            throw new InvalidParameterException(nameof(NotificationChannel.FallbackChannelId),
                $"Notification channel {fallbackId} was not found.");

        var seen = new HashSet<int>();
        if (selfId != null) seen.Add(selfId.Value);

        var cursor = fallbackId;
        while (cursor != null)
        {
            if (!seen.Add(cursor.Value))
                throw new InvalidParameterException(nameof(NotificationChannel.FallbackChannelId),
                    "That fallback would create a loop in the fallback chain.");

            cursor = chain.GetValueOrDefault(cursor.Value);
        }
    }

    /// <summary>
    /// Encrypts the secrets in an incoming configuration, carrying forward any field the client sent
    /// back as the redaction placeholder. Without that carry-forward, saving the form after only
    /// changing the recipients would overwrite the webhook URL with bullet characters.
    /// </summary>
    private string ProtectConfiguration(string? incoming, string? existing)
    {
        var config = ChannelConfiguration.Parse(incoming);
        var current = ChannelConfiguration.Parse(existing);

        config.WebhookUrl = Merge(config.WebhookUrl, current.WebhookUrl);
        config.SigningSecret = Merge(config.SigningSecret, current.SigningSecret);

        if (config.Headers != null)
        {
            var currentHeaders = current.Headers ?? new Dictionary<string, string>();
            config.Headers = config.Headers.ToDictionary(
                h => h.Key,
                h => Merge(h.Value, currentHeaders.GetValueOrDefault(h.Key)) ?? string.Empty);
        }

        return config.ToJson();
    }

    private string? Merge(string? incoming, string? stored)
    {
        if (incoming == null) return null;
        if (ChannelConfiguration.RedactedPlaceholder.Equals(incoming, StringComparison.Ordinal)) return stored;
        return protector.Protect(incoming);
    }

    /// <summary>
    /// Replaces the secret fields with the placeholder before the row leaves the service. Applied to
    /// the entity in place, so there is no path by which a caller receives the ciphertext either —
    /// a ciphertext handed to a client is one offline dictionary attack from being a plaintext token.
    /// </summary>
    private static void Redact(NotificationChannel channel)
    {
        var config = ChannelConfiguration.Parse(channel.ConfigurationJson);
        channel.ConfigurationJson = config.Redacted().ToJson();
    }
}
