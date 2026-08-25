using System.Text.Json;
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
/// Subscription matching, retry, fallback, digesting and the delivery log
/// (Track 4 milestones 4.1.1 and 4.1.3).
///
/// The dispatcher never throws for a delivery problem. A notification is a side effect of a domain
/// operation, and a Slack outage that rolls back the creation of a Critical risk would be a far worse
/// bug than the missing alert.
/// </summary>
public class NotificationDispatcher(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    INotificationChannelRegistry registry,
    INotificationSubscriptionsService subscriptions)
    : ServiceBase(logger, dalService), INotificationDispatcher
{
    /// <summary>Attempts before a delivery is marked failed. Three is the spec's number.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Base of the exponential backoff. Attempt 1 waits a minute, attempt 2 four minutes — enough for
    /// a rate limit to clear or a relay to come back, and short enough that an SLA breach alert is
    /// not hours late.
    /// </summary>
    private static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<NotificationDelivery>> DispatchAsync(NotificationMessage message,
        CancellationToken ct = default)
    {
        var created = new List<NotificationDelivery>();

        try
        {
            var matched = await subscriptions.MatchAsync(message.EventType, message.Severity, message.EntityId);

            if (matched.Count == 0)
            {
                Logger.Debug("No subscription matched {Event} at severity {Severity}",
                    message.EventType, message.Severity);
                return created;
            }

            var now = DateTime.UtcNow;

            await using (var db = DalService.GetContext())
            {
                foreach (var subscription in matched)
                {
                    var delivery = new NotificationDelivery
                    {
                        SubscriptionId = subscription.Id,
                        ChannelId = subscription.ChannelId,
                        EventType = message.EventType,
                        Status = NotificationDeliveryStatus.Pending,
                        Title = Truncate(message.Title, 512),
                        PayloadJson = JsonSerializer.Serialize(message, PayloadOptions),
                        Severity = message.Severity,
                        SubjectType = message.SubjectType,
                        SubjectId = message.SubjectId,
                        CreatedAt = now,
                        // A digesting subscription queues the row and waits; the sweep closes the
                        // window. Doing it here rather than in the job keeps "when is this due" in
                        // one place.
                        DigestDueAt = subscription.DigestWindowMinutes is > 0
                            ? now.AddMinutes(subscription.DigestWindowMinutes.Value)
                            : null
                    };

                    db.NotificationDeliveries.Add(delivery);
                    created.Add(delivery);
                }

                await db.SaveChangesAsync(ct);
            }

            // Immediate deliveries are attempted inline so "New Critical risk → Slack fires within
            // seconds" holds without waiting for the next sweep.
            foreach (var delivery in created.Where(d => d.DigestDueAt == null))
                await AttemptAsync(delivery.Id, message, ct);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not dispatch a {Event} notification: {Message}",
                message.EventType, ex.Message);
        }

        return created;
    }

    public async Task<DispatchSweepResult> ProcessPendingAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var result = new DispatchSweepResult();

        List<NotificationDelivery> due;

        await using (var db = DalService.GetContext())
        {
            due = await db.NotificationDeliveries
                .Where(d => d.Status == NotificationDeliveryStatus.Pending
                            || d.Status == NotificationDeliveryStatus.Retrying)
                .OrderBy(d => d.Id)
                .Take(500)
                .ToListAsync(ct);
        }

        // Digest rows whose window has closed are collapsed per (subscription) into one message; the
        // rest are individual attempts whose backoff has elapsed.
        var digestGroups = due
            .Where(d => d.DigestDueAt != null && d.DigestDueAt <= nowUtc)
            .GroupBy(d => d.SubscriptionId)
            .ToList();

        foreach (var group in digestGroups)
        {
            var sent = await SendDigestAsync(group.ToList(), ct);
            if (sent) result.DigestsSent++;
            else result.Failed++;
        }

        foreach (var delivery in due.Where(d => d.DigestDueAt == null && IsBackoffElapsed(d, nowUtc)))
        {
            var message = Deserialize(delivery);
            if (message == null)
            {
                await MarkFailedAsync(delivery.Id, "The stored notification payload could not be read.", ct);
                result.Failed++;
                continue;
            }

            var outcome = await AttemptAsync(delivery.Id, message, ct);

            switch (outcome)
            {
                case AttemptOutcome.Delivered: result.Delivered++; break;
                case AttemptOutcome.DeliveredByFallback: result.DeliveredByFallback++; break;
                case AttemptOutcome.Retrying: result.Retried++; break;
                default: result.Failed++; break;
            }
        }

        if (result.Delivered + result.Failed + result.Retried + result.DigestsSent > 0)
            Logger.Information(
                "Notification sweep: {Delivered} delivered, {Fallback} by fallback, {Retried} retrying, "
                + "{Failed} failed, {Digests} digest(s)",
                result.Delivered, result.DeliveredByFallback, result.Retried, result.Failed, result.DigestsSent);

        return result;
    }

    public async Task<ChannelTestResult> TestChannelAsync(int channelId, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();

        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == channelId, ct)
                      ?? throw new DataNotFoundException("notification_channels", channelId.ToString(),
                          new Exception($"Notification channel {channelId} was not found."));

        var provider = registry.For(channel.Kind);
        if (provider == null)
            return ChannelTestResult.Fail($"No provider is registered for channel kind {channel.Kind}.");

        try
        {
            return await provider.TestAsync(Decrypt(channel), ct);
        }
        catch (SecretProtectionException ex)
        {
            return ChannelTestResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Test of notification channel {Id} threw: {Message}", channelId, ex.Message);
            return ChannelTestResult.Fail($"The test failed: {ex.Message}");
        }
    }

    // --- delivery ---------------------------------------------------------------------------

    internal enum AttemptOutcome
    {
        Delivered,
        DeliveredByFallback,
        Retrying,
        Failed
    }

    /// <summary>
    /// One attempt at one delivery, including the walk down the fallback chain once the primary is
    /// out of attempts.
    /// </summary>
    private async Task<AttemptOutcome> AttemptAsync(int deliveryId, NotificationMessage message,
        CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        var delivery = await db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery == null) return AttemptOutcome.Failed;

        var channel = delivery.ChannelId == null
            ? null
            : await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == delivery.ChannelId, ct);

        delivery.Attempts++;
        delivery.LastAttemptAt = DateTime.UtcNow;

        var result = await SendThroughAsync(channel, message, ct);

        if (result.Success)
        {
            delivery.Status = NotificationDeliveryStatus.Delivered;
            delivery.DeliveredAt = DateTime.UtcNow;
            delivery.LastError = null;
            await db.SaveChangesAsync(ct);
            return AttemptOutcome.Delivered;
        }

        delivery.LastError = Truncate(Redact(result.Error), 2000);

        // Out of attempts, or a failure retrying cannot fix: try the fallback chain before giving up.
        // Doing it here rather than on the first failure is deliberate — falling back immediately
        // would double-notify on every transient Slack blip.
        if (!result.Retryable || delivery.Attempts >= MaxAttempts)
        {
            var fallback = await ResolveFallbackAsync(db, channel, ct);

            if (fallback != null)
            {
                var fallbackResult = await SendThroughAsync(fallback, message, ct);

                if (fallbackResult.Success)
                {
                    delivery.Status = NotificationDeliveryStatus.DeliveredByFallback;
                    delivery.ChannelId = fallback.Id;
                    delivery.DeliveredAt = DateTime.UtcNow;
                    // The primary's error is kept: "delivered by fallback" without the reason hides
                    // that the Slack webhook has been broken for a week.
                    await db.SaveChangesAsync(ct);

                    Logger.Warning(
                        "Notification {Id} was delivered by fallback channel {Fallback} after {Channel} failed: {Error}",
                        delivery.Id, fallback.Name, channel?.Name, delivery.LastError);

                    return AttemptOutcome.DeliveredByFallback;
                }

                delivery.LastError = Truncate(
                    $"{delivery.LastError} | fallback {fallback.Name}: {Redact(fallbackResult.Error)}", 2000);
            }

            delivery.Status = NotificationDeliveryStatus.Failed;
            await db.SaveChangesAsync(ct);

            Logger.Warning("Notification {Id} failed permanently after {Attempts} attempt(s): {Error}",
                delivery.Id, delivery.Attempts, delivery.LastError);

            return AttemptOutcome.Failed;
        }

        delivery.Status = NotificationDeliveryStatus.Retrying;
        await db.SaveChangesAsync(ct);

        return AttemptOutcome.Retrying;
    }

    /// <summary>
    /// Sends one message through one channel, translating a missing channel or provider into a
    /// permanent failure rather than an exception.
    /// </summary>
    private async Task<DeliveryResult> SendThroughAsync(NotificationChannel? channel,
        NotificationMessage message, CancellationToken ct)
    {
        if (channel == null) return DeliveryResult.Permanent("The channel no longer exists.");
        if (!channel.Enabled) return DeliveryResult.Permanent($"Channel '{channel.Name}' is disabled.");

        var provider = registry.For(channel.Kind);
        if (provider == null)
            return DeliveryResult.Permanent($"No provider is registered for channel kind {channel.Kind}.");

        try
        {
            return await provider.SendAsync(message, Decrypt(channel), ct);
        }
        catch (SecretProtectionException ex)
        {
            // Not retryable: re-entering the credential is the only fix, and retrying hides that.
            return DeliveryResult.Permanent(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Provider {Provider} threw sending notification: {Message}",
                provider.Name, ex.Message);
            return DeliveryResult.Retry($"The {provider.Name} provider failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One digest send for a group of queued deliveries that share a subscription.
    ///
    /// The digest carries the first message's shape with the others' titles folded into its body and
    /// <c>AggregatedCount</c> set, so every provider renders "and 41 more" in its own idiom without
    /// knowing what a digest is.
    /// </summary>
    private async Task<bool> SendDigestAsync(List<NotificationDelivery> group, CancellationToken ct)
    {
        var messages = group.Select(Deserialize).Where(m => m != null).Select(m => m!).ToList();

        if (messages.Count == 0)
        {
            foreach (var delivery in group)
                await MarkFailedAsync(delivery.Id, "The stored notification payload could not be read.", ct);
            return false;
        }

        var digest = BuildDigest(messages);
        var lead = group.First();

        var outcome = await AttemptAsync(lead.Id, digest, ct);

        var sent = outcome is AttemptOutcome.Delivered or AttemptOutcome.DeliveredByFallback;

        // Only once the digest actually went out are the rest closed as Batched: they were represented
        // by it, and leaving them queued would send each of them again on the next sweep. On a failure
        // they stay pending, so the retry rebuilds the whole digest instead of re-sending a digest of
        // one and silently dropping the other four events.
        if (!sent) return false;

        await using var db = DalService.GetContext();

        foreach (var delivery in group.Skip(1))
        {
            var stored = await db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == delivery.Id, ct);
            if (stored == null) continue;
            stored.Status = NotificationDeliveryStatus.Batched;
            stored.DeliveredAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Collapses several messages into one. The highest severity wins the header, because a digest
    /// that reads as Medium when it contains a Critical is worse than no digest.
    /// </summary>
    internal static NotificationMessage BuildDigest(List<NotificationMessage> messages)
    {
        var first = messages[0];
        var highest = messages.Max(m => m.Severity ?? 0);

        var digest = new NotificationMessage
        {
            EventType = first.EventType,
            Severity = highest == 0 ? null : highest,
            Title = messages.Count == 1
                ? first.Title
                : $"{messages.Count} × {NotificationCatalog.NameOf(first.EventType)}",
            Body = messages.Count == 1
                ? first.Body
                : string.Join("\n", messages.Take(20).Select(m => $"• {m.Title}"))
                  + (messages.Count > 20 ? $"\n… and {messages.Count - 20} more." : ""),
            Link = messages.Count == 1 ? first.Link : null,
            SubjectType = messages.Count == 1 ? first.SubjectType : null,
            SubjectId = messages.Count == 1 ? first.SubjectId : null,
            EntityId = first.EntityId,
            OccurredAt = messages.Min(m => m.OccurredAt),
            AggregatedCount = messages.Count
        };

        if (messages.Count == 1)
        {
            digest.Fields = first.Fields;
        }
        else
        {
            var bySeverity = messages
                .GroupBy(m => m.SeverityLabel)
                .OrderByDescending(g => g.Max(m => m.Severity ?? 0))
                .Select(g => $"{g.Key}: {g.Count()}");

            digest.Fields =
            [
                new NotificationField("Events", messages.Count.ToString()),
                new NotificationField("By severity", string.Join(", ", bySeverity)),
                new NotificationField("Window",
                    $"{messages.Min(m => m.OccurredAt):HH:mm}–{messages.Max(m => m.OccurredAt):HH:mm} UTC")
            ];
        }

        return digest;
    }

    private async Task MarkFailedAsync(int deliveryId, string error, CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        var delivery = await db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery == null) return;

        delivery.Status = NotificationDeliveryStatus.Failed;
        delivery.LastError = Truncate(error, 2000);
        delivery.LastAttemptAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Walks to the first enabled channel down the fallback chain, guarding against a cycle that
    /// slipped past validation (a row edited directly in the database, for instance).
    /// </summary>
    private static async Task<NotificationChannel?> ResolveFallbackAsync(DAL.Context.AuditableContext db,
        NotificationChannel? channel, CancellationToken ct)
    {
        var seen = new HashSet<int>();
        var cursor = channel;

        while (cursor?.FallbackChannelId != null && seen.Add(cursor.Id))
        {
            var next = await db.NotificationChannels
                .FirstOrDefaultAsync(c => c.Id == cursor.FallbackChannelId, ct);

            if (next == null) return null;
            if (next.Enabled) return next;

            cursor = next;
        }

        return null;
    }

    private static bool IsBackoffElapsed(NotificationDelivery delivery, DateTime nowUtc)
    {
        if (delivery.Attempts == 0 || delivery.LastAttemptAt == null) return true;

        // 1 min, 4 min, 9 min: quadratic rather than doubling, so the third attempt is still inside
        // the same ten minutes rather than an hour later.
        var wait = BackoffBase * (delivery.Attempts * delivery.Attempts);
        return delivery.LastAttemptAt.Value + wait <= nowUtc;
    }

    private NotificationMessage? Deserialize(NotificationDelivery delivery)
    {
        if (string.IsNullOrWhiteSpace(delivery.PayloadJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<NotificationMessage>(delivery.PayloadJson, PayloadOptions);
        }
        catch (JsonException ex)
        {
            Logger.Warning("Delivery {Id} has an unreadable payload: {Message}", delivery.Id, ex.Message);
            return null;
        }
    }

    private ChannelConfiguration Decrypt(NotificationChannel channel)
    {
        var config = ChannelConfiguration.Parse(channel.ConfigurationJson);

        config.WebhookUrl = protector.Unprotect(config.WebhookUrl);
        config.SigningSecret = protector.Unprotect(config.SigningSecret);

        if (config.Headers != null)
            config.Headers = config.Headers.ToDictionary(h => h.Key,
                h => protector.Unprotect(h.Value) ?? string.Empty);

        return config;
    }

    /// <summary>
    /// Strips anything token-shaped out of a provider error before it is written to the delivery log.
    /// Provider error bodies have been known to echo the credential back, and the delivery log is
    /// readable by anyone who can administer notifications.
    /// </summary>
    internal static string? Redact(string? error)
    {
        if (string.IsNullOrEmpty(error)) return error;

        var redacted = System.Text.RegularExpressions.Regex.Replace(error,
            @"https://hooks\.slack\.com/\S+", "https://hooks.slack.com/[redacted]");

        redacted = System.Text.RegularExpressions.Regex.Replace(redacted,
            @"(?i)\b(bearer|token|apikey|api_key|secret)\s*[:=]?\s*[A-Za-z0-9._\-]{8,}", "$1 [redacted]");

        return redacted;
    }

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}
