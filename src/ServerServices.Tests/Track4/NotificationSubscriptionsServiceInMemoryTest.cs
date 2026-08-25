using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Notifications;
using ServerServices.Interfaces;
using ServerServices.Notifications;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Channel and subscription administration and the delivery log
/// (Track 4 milestones 4.1.2 and 4.1.3).
///
/// The properties worth holding: a stored credential is never handed back to a client, a form that
/// round-trips the redaction placeholder does not overwrite the stored token with bullet characters,
/// and a fallback chain cannot contain a loop — which would otherwise be an infinite retry dressed up
/// as resilience.
/// </summary>
[TestSubject(typeof(NotificationSubscriptionsService))]
public class NotificationSubscriptionsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly INotificationSubscriptionsService _svc;
    private readonly ISecretProtector _protector;

    public NotificationSubscriptionsServiceInMemoryTest()
    {
        _svc = GetService<INotificationSubscriptionsService>();
        _protector = GetService<ISecretProtector>();
    }

    private static NotificationChannel SlackChannel(string name = "SOC Slack",
        string url = "https://hooks.slack.com/services/T/B/x") => new()
    {
        Name = name,
        Kind = NotificationChannelKind.Slack,
        Enabled = true,
        ConfigurationJson = new ChannelConfiguration { WebhookUrl = url }.ToJson()
    };

    private static NotificationChannel EmailChannel(string name = "SOC mail") => new()
    {
        Name = name,
        Kind = NotificationChannelKind.Email,
        Enabled = true,
        ConfigurationJson = new ChannelConfiguration { Recipients = "soc@acme.com" }.ToJson()
    };

    // --- channels ---------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAChannelEncryptsItsSecretsAndReturnsThemRedacted()
    {
        var created = await _svc.CreateChannelAsync(SlackChannel(), userId: 1);

        // What the caller receives.
        var returned = ChannelConfiguration.Parse(created.ConfigurationJson);
        Assert.Equal(ChannelConfiguration.RedactedPlaceholder, returned.WebhookUrl);

        // What is on disk: neither the plaintext URL nor something a client was handed.
        await using var db = OpenContext();
        var stored = db.NotificationChannels.Single();
        var storedConfig = ChannelConfiguration.Parse(stored.ConfigurationJson);

        Assert.NotNull(storedConfig.WebhookUrl);
        Assert.DoesNotContain("hooks.slack.com", stored.ConfigurationJson);
        Assert.True(_protector.LooksProtected(storedConfig.WebhookUrl));
        Assert.Equal("https://hooks.slack.com/services/T/B/x", _protector.Unprotect(storedConfig.WebhookUrl));
    }

    [Fact]
    public async Task SavingTheFormBackWithTheRedactedPlaceholderKeepsTheStoredSecret()
    {
        var created = await _svc.CreateChannelAsync(SlackChannel(), 1);

        // Exactly what the admin form sends after changing only the name.
        created.Name = "SOC Slack (renamed)";

        await _svc.UpdateChannelAsync(created, 1);

        await using var db = OpenContext();
        var stored = ChannelConfiguration.Parse(db.NotificationChannels.Single().ConfigurationJson);

        Assert.Equal("https://hooks.slack.com/services/T/B/x", _protector.Unprotect(stored.WebhookUrl));
    }

    [Fact]
    public async Task ANewSecretReplacesTheStoredOne()
    {
        var created = await _svc.CreateChannelAsync(SlackChannel(), 1);

        created.ConfigurationJson = new ChannelConfiguration
        {
            WebhookUrl = "https://hooks.slack.com/services/T/B/replaced"
        }.ToJson();

        await _svc.UpdateChannelAsync(created, 1);

        await using var db = OpenContext();
        var stored = ChannelConfiguration.Parse(db.NotificationChannels.Single().ConfigurationJson);

        Assert.Equal("https://hooks.slack.com/services/T/B/replaced", _protector.Unprotect(stored.WebhookUrl));
    }

    [Fact]
    public async Task DuplicateChannelNamesAreRefused()
    {
        await _svc.CreateChannelAsync(SlackChannel(), 1);

        // Two channels called "SOC Slack" make it impossible to say which one is misconfigured.
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateChannelAsync(SlackChannel(), 1));
    }

    [Theory]
    [InlineData(NotificationChannelKind.Slack)]
    [InlineData(NotificationChannelKind.Teams)]
    [InlineData(NotificationChannelKind.Webhook)]
    public async Task AWebhookChannelWithoutAUrlIsRefused(NotificationChannelKind kind)
    {
        var channel = new NotificationChannel { Name = "x", Kind = kind, ConfigurationJson = "{}" };

        // Refused at save rather than at send: the alternative is a channel whose only symptom is a
        // permanently failing delivery.
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.CreateChannelAsync(channel, 1));

        Assert.Contains("webhook URL", thrown.Message);
    }

    [Fact]
    public async Task AnEmailChannelWithoutRecipientsIsRefused()
    {
        var channel = new NotificationChannel
        {
            Name = "x", Kind = NotificationChannelKind.Email, ConfigurationJson = "{}"
        };

        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.CreateChannelAsync(channel, 1));
    }

    [Fact]
    public async Task ANonHttpWebhookUrlIsRefused()
    {
        var channel = SlackChannel(url: "file:///etc/passwd");

        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.CreateChannelAsync(channel, 1));
    }

    [Fact]
    public async Task AChannelCannotFallBackToItself()
    {
        var created = await _svc.CreateChannelAsync(SlackChannel(), 1);

        created.FallbackChannelId = created.Id;

        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.UpdateChannelAsync(created, 1));
    }

    [Fact]
    public async Task AFallbackChainMayNotLoop()
    {
        var slack = await _svc.CreateChannelAsync(SlackChannel(), 1);
        var mail = await _svc.CreateChannelAsync(EmailChannel(), 1);

        slack.FallbackChannelId = mail.Id;
        await _svc.UpdateChannelAsync(slack, 1);

        mail.FallbackChannelId = slack.Id;

        // A loop is not a resilience configuration: the dispatcher would follow it until it ran out of
        // channels and retry the same failing one on the way round.
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.UpdateChannelAsync(mail, 1));

        Assert.Contains("loop", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AChannelOtherChannelsFallBackToCannotBeDeleted()
    {
        var mail = await _svc.CreateChannelAsync(EmailChannel(), 1);
        var slack = await _svc.CreateChannelAsync(SlackChannel(), 1);

        slack.FallbackChannelId = mail.Id;
        await _svc.UpdateChannelAsync(slack, 1);

        // Refused loudly rather than silently orphaning the fallback.
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.DeleteChannelAsync(mail.Id));

        Assert.Contains("fall back", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownChannelIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetChannelAsync(404));
    }

    // --- subscriptions ----------------------------------------------------------------------

    [Fact]
    public async Task ASubscriptionRequiresAnExistingChannel()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateSubscriptionAsync(new NotificationSubscription
            {
                EventType = NotificationEventType.RiskCreated,
                ChannelId = 999
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task AnOutOfRangeSeverityFilterIsRefused(int severity)
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateSubscriptionAsync(new NotificationSubscription
            {
                EventType = NotificationEventType.RiskCreated,
                ChannelId = channel.Id,
                MinSeverity = severity
            }));
    }

    [Fact]
    public async Task AnAbsurdDigestWindowIsRefused()
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateSubscriptionAsync(new NotificationSubscription
            {
                EventType = NotificationEventType.VulnerabilityImported,
                ChannelId = channel.Id,
                DigestWindowMinutes = 5000
            }));
    }

    [Fact]
    public async Task MatchingHonoursSeverityEntityAndTheEnabledFlags()
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await _svc.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = NotificationEventType.RiskCreated, ChannelId = channel.Id, MinSeverity = 3
        });

        Assert.Single(await _svc.MatchAsync(NotificationEventType.RiskCreated, 4, null));
        Assert.Empty(await _svc.MatchAsync(NotificationEventType.RiskCreated, 2, null));
        Assert.Empty(await _svc.MatchAsync(NotificationEventType.IncidentCreated, 4, null));
    }

    [Fact]
    public async Task AnEventWithNoSeverityStillReachesASeverityFilteredSubscription()
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await _svc.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = NotificationEventType.IncidentCreated, ChannelId = channel.Id, MinSeverity = 4
        });

        // An incident has no severity band. Treating null as "below the threshold" is how a subscriber
        // discovers months later that they never got incident alerts.
        Assert.Single(await _svc.MatchAsync(NotificationEventType.IncidentCreated, null, null));
    }

    [Fact]
    public async Task AnEntityScopedSubscriptionOnlyHearsAboutItsOwnEntity()
    {
        Seed(ctx => ctx.Entities.Add(new Entity
        {
            Id = 7, DefinitionName = "acme", DefinitionVersion = "1", Status = "active"
        }));

        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await _svc.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = NotificationEventType.RiskCreated, ChannelId = channel.Id, EntityId = 7
        });

        Assert.Single(await _svc.MatchAsync(NotificationEventType.RiskCreated, 4, 7));
        Assert.Empty(await _svc.MatchAsync(NotificationEventType.RiskCreated, 4, 8));
        Assert.Empty(await _svc.MatchAsync(NotificationEventType.RiskCreated, 4, null));
    }

    [Fact]
    public async Task ASubscriptionOnADisabledChannelDoesNotMatch()
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        await _svc.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = NotificationEventType.RiskCreated, ChannelId = channel.Id
        });

        channel.Enabled = false;
        await _svc.UpdateChannelAsync(channel, 1);

        Assert.Empty(await _svc.MatchAsync(NotificationEventType.RiskCreated, 4, null));
    }

    [Fact]
    public async Task DeletingASubscriptionRemovesIt()
    {
        var channel = await _svc.CreateChannelAsync(SlackChannel(), 1);

        var subscription = await _svc.CreateSubscriptionAsync(new NotificationSubscription
        {
            EventType = NotificationEventType.RiskCreated, ChannelId = channel.Id
        });

        await _svc.DeleteSubscriptionAsync(subscription.Id);

        Assert.Empty(await _svc.GetSubscriptionsAsync());
    }

    // --- delivery log -----------------------------------------------------------------------

    [Fact]
    public async Task ADeliveredNotificationCannotBeRequeued()
    {
        Seed(ctx => ctx.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = 1,
            EventType = NotificationEventType.SlaBreached,
            Status = NotificationDeliveryStatus.Delivered,
            CreatedAt = DateTime.UtcNow
        }));

        // Re-sending a delivered notification would duplicate the alert.
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.RequeueDeliveryAsync(1));
    }

    [Fact]
    public async Task RequeuingAFailedDeliveryResetsItsAttempts()
    {
        Seed(ctx => ctx.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = 1,
            EventType = NotificationEventType.SlaBreached,
            Status = NotificationDeliveryStatus.Failed,
            Attempts = 3,
            LastError = "404",
            CreatedAt = DateTime.UtcNow
        }));

        var requeued = await _svc.RequeueDeliveryAsync(1);

        // The point of a requeue is that the operator changed something, so the attempt deserves the
        // full three tries rather than the none it had left.
        Assert.Equal(NotificationDeliveryStatus.Pending, requeued.Status);
        Assert.Equal(0, requeued.Attempts);
        Assert.Null(requeued.LastError);
    }

    [Fact]
    public async Task TheDeliveryLogFiltersByStatus()
    {
        Seed(ctx =>
        {
            ctx.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = 1, EventType = NotificationEventType.SlaBreached,
                Status = NotificationDeliveryStatus.Delivered, CreatedAt = DateTime.UtcNow
            });
            ctx.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = 2, EventType = NotificationEventType.SlaBreached,
                Status = NotificationDeliveryStatus.Failed, CreatedAt = DateTime.UtcNow
            });
        });

        var failed = await _svc.GetDeliveriesAsync(status: NotificationDeliveryStatus.Failed);

        // "Failed" is what an operator opens the log for.
        Assert.Equal(2, Assert.Single(failed).Id);
    }

    [Fact]
    public async Task PurgingRemovesOnlyRowsOlderThanTheRetentionWindow()
    {
        Seed(ctx =>
        {
            ctx.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = 1, EventType = NotificationEventType.SlaBreached,
                Status = NotificationDeliveryStatus.Delivered,
                CreatedAt = DateTime.UtcNow.AddDays(-120)
            });
            ctx.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = 2, EventType = NotificationEventType.SlaBreached,
                Status = NotificationDeliveryStatus.Delivered,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        });

        var purged = await _svc.PurgeDeliveriesAsync(90);

        Assert.Equal(1, purged);
        Assert.Equal(2, Assert.Single(await _svc.GetDeliveriesAsync()).Id);
    }
}
