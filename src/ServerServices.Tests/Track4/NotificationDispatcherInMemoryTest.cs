using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Notifications;
using ServerServices.Interfaces;
using ServerServices.Notifications;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Dispatch, retry, fallback, digesting and the delivery log
/// (Track 4 milestones 4.1.1 and 4.1.3).
///
/// The acceptance criterion — "new Critical risk → Slack fires within seconds" — is what the immediate
/// path here covers. The rest are the properties that stop the feature from being worse than nothing:
/// a notification failure never fails the domain operation, a retry does not fall back on the first
/// blip, a digest sends once rather than once per event, and a provider error never puts a credential
/// in the delivery log.
/// </summary>
[TestSubject(typeof(NotificationDispatcher))]
public class NotificationDispatcherInMemoryTest : InMemoryServiceTestBase
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly INotificationSubscriptionsService _subscriptions;

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    public NotificationDispatcherInMemoryTest()
    {
        _dispatcher = GetService<INotificationDispatcher>();
        _subscriptions = GetService<INotificationSubscriptionsService>();
    }

    private static NotificationMessage Message(int? severity = 4,
        NotificationEventType eventType = NotificationEventType.RiskCreated) => new()
    {
        EventType = eventType,
        Severity = severity,
        Title = "New Critical risk: unauthenticated admin API",
        Body = "Recorded by the register.",
        Link = "https://netrisk.acme.com/risks/9",
        SubjectType = "risk",
        SubjectId = 9,
        OccurredAt = Now
    };

    private async Task<NotificationChannel> SlackAsync(string name = "SOC Slack", bool enabled = true)
    {
        var channel = await _subscriptions.CreateChannelAsync(new NotificationChannel
        {
            Name = name,
            Kind = NotificationChannelKind.Slack,
            Enabled = enabled,
            ConfigurationJson = new ChannelConfiguration
            {
                WebhookUrl = "https://hooks.slack.com/services/T/B/x"
            }.ToJson()
        }, 1);

        return channel;
    }

    private async Task<NotificationChannel> EmailAsync(string name = "Fallback mail")
    {
        return await _subscriptions.CreateChannelAsync(new NotificationChannel
        {
            Name = name,
            Kind = NotificationChannelKind.Email,
            Enabled = true,
            ConfigurationJson = new ChannelConfiguration { Recipients = "soc@acme.com" }.ToJson()
        }, 1);
    }

    private Task SubscribeAsync(int channelId, int? minSeverity = null, int? digestMinutes = null,
        NotificationEventType eventType = NotificationEventType.RiskCreated) =>
        _subscriptions.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = eventType,
            ChannelId = channelId,
            MinSeverity = minSeverity,
            DigestWindowMinutes = digestMinutes,
            Enabled = true
        });

    // --- immediate delivery -----------------------------------------------------------------

    [Fact]
    public async Task ACriticalRiskReachesSlackImmediately()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id, minSeverity: 4);

        var created = await _dispatcher.DispatchAsync(Message());

        var delivery = Assert.Single(created);

        Assert.Single(FakeOutboundHttpClient.Requests);
        Assert.Contains("hooks.slack.com", FakeOutboundHttpClient.Requests[0].Url);

        // Not queued for the sweep: the acceptance criterion is "within seconds".
        await using var db = OpenContext();
        var stored = db.NotificationDeliveries.Single(d => d.Id == delivery.Id);

        Assert.Equal(NotificationDeliveryStatus.Delivered, stored.Status);
        Assert.NotNull(stored.DeliveredAt);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task AnEventNoSubscriptionWantsCreatesNoDelivery()
    {
        await SlackAsync();

        Assert.Empty(await _dispatcher.DispatchAsync(Message()));
        Assert.Empty(FakeOutboundHttpClient.Requests);
    }

    [Fact]
    public async Task AnEventBelowTheSeverityFilterIsNotDelivered()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id, minSeverity: 4);

        Assert.Empty(await _dispatcher.DispatchAsync(Message(severity: 2)));
    }

    [Fact]
    public async Task OneEventReachesEverySubscribedChannel()
    {
        var slack = await SlackAsync();
        var mail = await EmailAsync();

        await SubscribeAsync(slack.Id);
        await SubscribeAsync(mail.Id);

        Assert.Equal(2, (await _dispatcher.DispatchAsync(Message())).Count);
    }

    [Fact]
    public async Task ADispatchFailureNeverThrows()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id);

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        // A notification is a side effect. A Slack outage that rolled back the creation of a Critical
        // risk would be a far worse bug than the missing alert.
        var created = await _dispatcher.DispatchAsync(Message());

        Assert.Single(created);

        await using var db = OpenContext();
        Assert.Equal(NotificationDeliveryStatus.Retrying,
            db.NotificationDeliveries.Single().Status);
    }

    // --- retry and backoff ------------------------------------------------------------------

    [Fact]
    public async Task ARetryableFailureIsRetriedRatherThanFallingBackImmediately()
    {
        var mail = await EmailAsync();
        var slack = await SlackAsync();

        slack.FallbackChannelId = mail.Id;
        await _subscriptions.UpdateChannelAsync(slack, 1);

        await SubscribeAsync(slack.Id);

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 429 };

        await _dispatcher.DispatchAsync(Message());

        await using var db = OpenContext();
        var delivery = db.NotificationDeliveries.Single();

        // Falling back on the first transient blip would double-notify on every Slack hiccup.
        Assert.Equal(NotificationDeliveryStatus.Retrying, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
    }

    [Fact]
    public async Task BackoffHoldsARetryUntilItsWindowElapses()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id);

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        await _dispatcher.DispatchAsync(Message());

        var attemptsBefore = FakeOutboundHttpClient.Requests.Count;

        // A sweep a second later must not burn the remaining attempts.
        var immediate = await _dispatcher.ProcessPendingAsync(DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(0, immediate.Retried);
        Assert.Equal(attemptsBefore, FakeOutboundHttpClient.Requests.Count);

        var later = await _dispatcher.ProcessPendingAsync(DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(1, later.Retried);
    }

    [Fact]
    public async Task APermanentFailureIsNotRetried()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id);

        // A 403 is a configuration problem; retrying it three times only delays the operator finding out.
        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 403 };

        await _dispatcher.DispatchAsync(Message());

        await using var db = OpenContext();
        Assert.Equal(NotificationDeliveryStatus.Failed, db.NotificationDeliveries.Single().Status);
    }

    // --- fallback ---------------------------------------------------------------------------

    [Fact]
    public async Task TheFallbackChannelDeliversOnceThePrimaryIsPermanentlyBroken()
    {
        var mail = await EmailAsync();
        var slack = await SlackAsync();

        slack.FallbackChannelId = mail.Id;
        await _subscriptions.UpdateChannelAsync(slack, 1);

        await SubscribeAsync(slack.Id);

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse
        {
            StatusCode = 404, Body = "invalid_token"
        };

        await _dispatcher.DispatchAsync(Message());

        await using var db = OpenContext();
        var delivery = db.NotificationDeliveries.Single();

        Assert.Equal(NotificationDeliveryStatus.DeliveredByFallback, delivery.Status);
        Assert.Equal(mail.Id, delivery.ChannelId);
        // The primary's error is kept: "delivered by fallback" without the reason hides that the Slack
        // webhook has been broken for a week.
        Assert.Contains("invalid_token", delivery.LastError!);
    }

    [Fact]
    public async Task ADisabledFallbackIsSkippedAndTheDeliveryFails()
    {
        var mail = await EmailAsync();
        var slack = await SlackAsync();

        slack.FallbackChannelId = mail.Id;
        await _subscriptions.UpdateChannelAsync(slack, 1);

        mail.Enabled = false;
        await _subscriptions.UpdateChannelAsync(mail, 1);

        await SubscribeAsync(slack.Id);

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 404 };

        await _dispatcher.DispatchAsync(Message());

        await using var db = OpenContext();
        Assert.Equal(NotificationDeliveryStatus.Failed, db.NotificationDeliveries.Single().Status);
    }

    // --- digesting --------------------------------------------------------------------------

    [Fact]
    public async Task ADigestingSubscriptionQueuesRatherThanSendingImmediately()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id, digestMinutes: 30,
            eventType: NotificationEventType.VulnerabilityImported);

        var created = await _dispatcher.DispatchAsync(
            Message(eventType: NotificationEventType.VulnerabilityImported));

        Assert.Empty(FakeOutboundHttpClient.Requests);
        Assert.NotNull(Assert.Single(created).DigestDueAt);
    }

    [Fact]
    public async Task ClosingADigestWindowSendsOneMessageForEveryQueuedEvent()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id, digestMinutes: 30,
            eventType: NotificationEventType.VulnerabilityImported);

        for (var index = 0; index < 5; index++)
            await _dispatcher.DispatchAsync(
                Message(severity: index == 0 ? 4 : 2, eventType: NotificationEventType.VulnerabilityImported));

        Assert.Empty(FakeOutboundHttpClient.Requests);

        var swept = await _dispatcher.ProcessPendingAsync(DateTime.UtcNow.AddHours(1));

        Assert.Equal(1, swept.DigestsSent);
        // Five events, one message — which is the whole point of the window.
        Assert.Single(FakeOutboundHttpClient.Requests);

        await using var db = OpenContext();
        var deliveries = db.NotificationDeliveries.ToList();

        Assert.Single(deliveries, d => d.Status == NotificationDeliveryStatus.Delivered);
        // The rest are closed as Batched rather than left pending, or the next sweep would send each
        // of them again.
        Assert.Equal(4, deliveries.Count(d => d.Status == NotificationDeliveryStatus.Batched));
    }

    [Fact]
    public async Task AFailedDigestLeavesTheRestQueuedSoTheRetryRebuildsIt()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id, digestMinutes: 30,
            eventType: NotificationEventType.VulnerabilityImported);

        for (var index = 0; index < 4; index++)
            await _dispatcher.DispatchAsync(Message(eventType: NotificationEventType.VulnerabilityImported));

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        Assert.Equal(0, (await _dispatcher.ProcessPendingAsync(DateTime.UtcNow.AddHours(1))).DigestsSent);

        await using var db = OpenContext();

        // Closing them as Batched on a failure would mean the retry sent a digest of one and silently
        // dropped the other three events.
        Assert.Equal(0, db.NotificationDeliveries.Count(d => d.Status == NotificationDeliveryStatus.Batched));
        Assert.Equal(3, db.NotificationDeliveries.Count(d => d.Status == NotificationDeliveryStatus.Pending));
    }

    [Fact]
    public void ADigestTakesTheHighestSeverityOfWhatItSummarises()
    {
        var digest = NotificationDispatcher.BuildDigest(
        [
            Message(severity: 2),
            Message(severity: 4),
            Message(severity: 1)
        ]);

        // A digest that reads as Medium while containing a Critical is worse than no digest.
        Assert.Equal(4, digest.Severity);
        Assert.Equal(3, digest.AggregatedCount);
        Assert.Contains("By severity", digest.Fields.Select(f => f.Label));
    }

    [Fact]
    public void ADigestOfOneKeepsThatEventsOwnFieldsAndLink()
    {
        var single = Message();

        var digest = NotificationDispatcher.BuildDigest([single]);

        Assert.Equal(1, digest.AggregatedCount);
        Assert.Equal(single.Title, digest.Title);
        Assert.Equal(single.Link, digest.Link);
    }

    // --- delivery log -----------------------------------------------------------------------

    [Fact]
    public async Task TheDeliveryLogRecordsThePayloadSoARetryDoesNotRebuildIt()
    {
        var channel = await SlackAsync();
        await SubscribeAsync(channel.Id);

        await _dispatcher.DispatchAsync(Message());

        await using var db = OpenContext();
        var delivery = db.NotificationDeliveries.Single();

        using var payload = JsonDocument.Parse(delivery.PayloadJson!);

        Assert.Equal("risk", payload.RootElement.GetProperty("SubjectType").GetString());
        Assert.Equal(9, payload.RootElement.GetProperty("SubjectId").GetInt32());
        Assert.Equal("risk", delivery.SubjectType);
        Assert.Equal(4, delivery.Severity);
    }

    [Theory]
    [InlineData("Slack rejected https://hooks.slack.com/services/T00/B00/abcdef",
        "https://hooks.slack.com/[redacted]")]
    [InlineData("401 Unauthorized: bearer abcdef0123456789", "bearer [redacted]")]
    [InlineData("token=abcdef0123456789", "token [redacted]")]
    public void ProviderErrorsAreRedactedBeforeTheyReachTheLog(string error, string expected)
    {
        // Provider error bodies have been known to echo the credential back, and the delivery log is
        // readable by anyone who can administer notifications.
        Assert.Contains(expected, NotificationDispatcher.Redact(error));
    }

    [Fact]
    public async Task TestingAChannelDoesNotWriteToTheDeliveryLog()
    {
        var channel = await SlackAsync();

        var result = await _dispatcher.TestChannelAsync(channel.Id);

        Assert.True(result.Success);

        // A test is a diagnostic; filling the delivery log with tests makes the log less useful.
        await using var db = OpenContext();
        Assert.Empty(db.NotificationDeliveries);
    }

    [Fact]
    public async Task TestingAnUnknownChannelIsNotFound()
    {
        await Assert.ThrowsAsync<Model.Exceptions.DataNotFoundException>(
            () => _dispatcher.TestChannelAsync(404));
    }
}
