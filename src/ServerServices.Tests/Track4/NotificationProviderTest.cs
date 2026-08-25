using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Notifications;
using Serilog;
using ServerServices.Notifications;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// The four notification providers' rendering and failure classification
/// (Track 4 milestone 4.1.2).
///
/// Rendering is asserted against the payload actually sent, not against an intermediate object: a Slack
/// message that is valid C# and invalid Block Kit is rejected by Slack with a two-word body, and this
/// is the only place that would catch it. The retryable-versus-permanent split is asserted for the same
/// reason — retrying a 403 forever hides a misconfiguration, and giving up on a 429 loses the alert.
/// </summary>
[TestSubject(typeof(SlackNotificationChannel))]
public class NotificationProviderTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static NotificationMessage Message() => new()
    {
        EventType = NotificationEventType.SlaBreached,
        Severity = 4,
        Title = "SQL injection in <billing> & reporting",
        Body = "The finding passed its remediation deadline.",
        Fields =
        [
            new NotificationField("Finding", "#42"),
            new NotificationField("Severity", "Critical"),
            new NotificationField("Asset", "db-prod-01")
        ],
        Link = "https://netrisk.acme.com/vulnerabilities/42",
        SubjectType = "finding",
        SubjectId = 42,
        EntityId = 7,
        OccurredAt = new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc)
    };

    // --- Slack ------------------------------------------------------------------------------

    [Fact]
    public async Task SlackSendsBlockKitWithAHeaderFieldsAndAButton()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new SlackNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(),
            new ChannelConfiguration { WebhookUrl = "https://hooks.slack.com/services/T/B/x" });

        Assert.True(result.Success);

        var request = Assert.Single(http.Requests);
        Assert.Equal("POST", request.Method);

        using var payload = JsonDocument.Parse(request.Body!);
        var attachment = payload.RootElement.GetProperty("attachments")[0];

        // The severity stripe is the only severity affordance Slack offers.
        Assert.Equal("#B4232B", attachment.GetProperty("color").GetString());

        var blocks = attachment.GetProperty("blocks").EnumerateArray().ToList();

        Assert.Equal("header", blocks[0].GetProperty("type").GetString());
        Assert.Contains("Critical", blocks[0].GetProperty("text").GetProperty("text").GetString()!);

        // The field grid and the button are what make an alert actionable.
        Assert.Contains(blocks, b => b.GetProperty("type").GetString() == "section"
                                     && b.TryGetProperty("fields", out _));
        Assert.Contains(blocks, b => b.GetProperty("type").GetString() == "actions");

        // Fallback text for notifications and screen readers; Slack requires it.
        Assert.Contains("SQL injection", payload.RootElement.GetProperty("text").GetString()!);
    }

    [Fact]
    public void SlackEscapesOnlyTheThreeCharactersMrkdwnRequires()
    {
        var message = Message();
        message.Body = "Affects <billing> & reporting: use *bold* carefully.";

        using var payload = JsonDocument.Parse(
            SlackNotificationChannel.BuildPayload(message, new ChannelConfiguration()));

        var section = payload.RootElement.GetProperty("attachments")[0].GetProperty("blocks")
            .EnumerateArray()
            .First(b => b.GetProperty("type").GetString() == "section"
                        && b.TryGetProperty("text", out _));

        var text = section.GetProperty("text").GetProperty("text").GetString()!;

        Assert.Contains("&lt;billing&gt;", text);
        Assert.Contains("&amp;", text);
        // An HTML-style escape would turn an asterisk in a finding title into an entity; mrkdwn needs
        // exactly these three escaped and nothing else.
        Assert.Contains("*bold*", text);
    }

    [Fact]
    public void SlackChunksFieldsIntoSectionsOfTenRatherThanDroppingThem()
    {
        var message = Message();
        message.Fields.Clear();

        for (var index = 0; index < 23; index++)
            message.Fields.Add(new NotificationField($"Field {index}", index.ToString()));

        var payload = SlackNotificationChannel.BuildPayload(message, new ChannelConfiguration());

        using var document = JsonDocument.Parse(payload);

        var fieldSections = document.RootElement.GetProperty("attachments")[0].GetProperty("blocks")
            .EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "section" && b.TryGetProperty("fields", out _))
            .ToList();

        // Slack caps a section at ten fields; dropping the eleventh silently is how the asset name
        // disappears.
        Assert.Equal(3, fieldSections.Count);
        Assert.Equal(23, fieldSections.Sum(s => s.GetProperty("fields").GetArrayLength()));
    }

    [Fact]
    public async Task SlackRateLimitingIsRetryableWithTheProvidersOwnBackoff()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(429, "rate_limited", retryAfter: "30");
        var channel = new SlackNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(),
            new ChannelConfiguration { WebhookUrl = "https://hooks.slack.com/x" });

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        // Honouring Slack's own number matters: guessing shorter is what turns a brief limit into a
        // sustained one.
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Fact]
    public async Task SlackConfigurationErrorsAreNotRetried()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(404, "invalid_token");
        var channel = new SlackNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(),
            new ChannelConfiguration { WebhookUrl = "https://hooks.slack.com/x" });

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        // Slack's short text body is the whole diagnosis, so it is passed through.
        Assert.Contains("invalid_token", result.Error!);
    }

    [Fact]
    public async Task SlackWithNoWebhookUrlFailsPermanentlyWithoutCallingAnything()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new SlackNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(), new ChannelConfiguration());

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task SlackTestSaysTheChannelOverrideIsIgnored()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new SlackNotificationChannel(Log, http);

        var result = await channel.TestAsync(new ChannelConfiguration
        {
            WebhookUrl = "https://hooks.slack.com/x",
            Channel = "#soc"
        });

        Assert.True(result.Success);
        // An operator who set an override and saw a bare success would assume it was honoured.
        Assert.Contains("ignored", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Teams ------------------------------------------------------------------------------

    [Fact]
    public async Task TeamsSendsAnAdaptiveCardEnvelopeNotTheRetiredConnectorFormat()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new TeamsNotificationChannel(Log, http);

        await channel.SendAsync(Message(),
            new ChannelConfiguration { WebhookUrl = "https://acme.logic.azure.com/workflows/x" });

        using var payload = JsonDocument.Parse(Assert.Single(http.Requests).Body!);

        Assert.Equal("message", payload.RootElement.GetProperty("type").GetString());

        var attachment = payload.RootElement.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive",
            attachment.GetProperty("contentType").GetString());

        var content = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", content.GetProperty("type").GetString());

        // MessageCard is the retired O365 connector schema; emitting it would work in some tenants for
        // a while and then stop.
        Assert.DoesNotContain("MessageCard", payload.RootElement.ToString());

        var body = content.GetProperty("body").EnumerateArray().ToList();
        Assert.Contains(body, b => b.GetProperty("type").GetString() == "FactSet");

        Assert.Equal("Action.OpenUrl",
            content.GetProperty("actions")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task TeamsAcceptsThe202AWorkflowWebhookAnswers()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("", 202);
        var channel = new TeamsNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(),
            new ChannelConfiguration { WebhookUrl = "https://acme.logic.azure.com/workflows/x" });

        // A Workflows webhook answers 202, not 200 — an equality check on 200 would report every
        // successful delivery as a failure.
        Assert.True(result.Success);
        Assert.Equal(202, result.StatusCode);
    }

    [Fact]
    public void TeamsUsesTheAdaptiveColourVocabularyNotHex()
    {
        var payload = TeamsNotificationChannel.BuildPayload(Message());

        // Adaptive Cards take colour names; hex is silently ignored.
        Assert.Contains("\"color\":\"attention\"", payload.Replace(" ", ""));
    }

    // --- generic webhook --------------------------------------------------------------------

    [Fact]
    public async Task WebhookSignsTheBodyAndTheTimestamp()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new WebhookNotificationChannel(Log, http);

        await channel.SendAsync(Message(), new ChannelConfiguration
        {
            WebhookUrl = "https://receiver.acme.com/netrisk",
            SigningSecret = "s3cret"
        });

        var request = Assert.Single(http.Requests);

        var signature = request.Headers[WebhookNotificationChannel.SignatureHeader];
        var timestamp = request.Headers[WebhookNotificationChannel.TimestampHeader];

        Assert.StartsWith("sha256=", signature);

        // The receiver's own verification, run against the exact bytes that were sent.
        Assert.True(WebhookNotificationChannel.VerifySignature(timestamp, request.Body!, "s3cret", signature));

        // The timestamp is inside the signed string: signing only the body would let a captured request
        // be replayed forever with no way for the receiver to detect it.
        Assert.False(WebhookNotificationChannel.VerifySignature("0", request.Body!, "s3cret", signature));
        Assert.False(WebhookNotificationChannel.VerifySignature(timestamp, request.Body!, "wrong", signature));
    }

    [Fact]
    public async Task WebhookSendsUnsignedWhenNoSecretIsSetAndSaysSoOnTest()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new WebhookNotificationChannel(Log, http);

        var result = await channel.TestAsync(new ChannelConfiguration
        {
            WebhookUrl = "https://receiver.acme.com/netrisk"
        });

        Assert.True(result.Success);
        Assert.DoesNotContain(WebhookNotificationChannel.SignatureHeader, http.Requests[0].Headers.Keys);
        // Allowed, but a real gap — so the test button says it rather than reporting a bare success.
        Assert.Contains("unsigned", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebhookCarriesCustomHeadersAndTheEventName()
    {
        var http = new FakeOutboundHttpClient();
        var channel = new WebhookNotificationChannel(Log, http);

        await channel.SendAsync(Message(), new ChannelConfiguration
        {
            WebhookUrl = "https://receiver.acme.com/netrisk",
            Headers = new System.Collections.Generic.Dictionary<string, string> { ["X-Api-Key"] = "k" }
        });

        var request = Assert.Single(http.Requests);

        Assert.Equal("k", request.Headers["X-Api-Key"]);
        // Routing without parsing the body is why the event name is a header too.
        Assert.Equal("sla.breached", request.Headers[WebhookNotificationChannel.EventHeader]);
    }

    [Fact]
    public void WebhookPayloadIsTheDocumentedStableShape()
    {
        using var payload = JsonDocument.Parse(WebhookNotificationChannel.BuildPayload(Message()));

        var root = payload.RootElement;

        // These names are the contract for anyone who has written a receiver.
        Assert.Equal(WebhookNotificationChannel.SchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sla.breached", root.GetProperty("event").GetString());
        Assert.Equal(4, root.GetProperty("severity").GetInt32());
        Assert.Equal("finding", root.GetProperty("subject").GetProperty("type").GetString());
        Assert.Equal(42, root.GetProperty("subject").GetProperty("id").GetInt32());
        Assert.Equal(7, root.GetProperty("subject").GetProperty("entityId").GetInt32());

        // Fields as an object, so a receiver reads payload.fields.Severity rather than scanning a list.
        Assert.Equal("Critical", root.GetProperty("fields").GetProperty("Severity").GetString());
    }

    [Fact]
    public async Task WebhookTransportFailureIsRetryable()
    {
        var http = new FakeOutboundHttpClient().EnqueueTransportError("Name or service not known");
        var channel = new WebhookNotificationChannel(Log, http);

        var result = await channel.SendAsync(Message(), new ChannelConfiguration
        {
            WebhookUrl = "https://receiver.acme.com/netrisk"
        });

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Contains("Name or service not known", result.Error!);
    }

    // --- email ------------------------------------------------------------------------------

    [Fact]
    public async Task EmailSendsHtmlAndAPlainTextAlternativeToEveryRecipient()
    {
        var mail = new EmailMock();
        var channel = new EmailNotificationChannel(Log, mail);

        var result = await channel.SendAsync(Message(), new ChannelConfiguration
        {
            Recipients = "a@acme.com, b@acme.com; a@acme.com"
        });

        Assert.True(result.Success);

        // Duplicates collapsed; both separators accepted, because people type both.
        Assert.Equal(2, mail.NotificationSends.Count);

        var sent = mail.NotificationSends[0];
        Assert.Contains("[Critical]", sent.Subject);
        Assert.Contains("<table", sent.Html);
        // A security alert that renders blank in a text-only client is an alert that did not arrive.
        Assert.Contains("Finding: #42", sent.Text!);
    }

    [Fact]
    public void EmailEncodesFindingTextBecauseItIsAttackerInfluenced()
    {
        var html = EmailNotificationChannel.BuildHtmlBody(Message());

        Assert.Contains("&lt;billing&gt;", html);
        Assert.DoesNotContain("<billing>", html);
    }

    [Fact]
    public async Task EmailWithNoRecipientsFailsPermanently()
    {
        var channel = new EmailNotificationChannel(Log, new EmailMock());

        var result = await channel.SendAsync(Message(), new ChannelConfiguration());

        Assert.False(result.Success);
        Assert.False(result.Retryable);
    }

    [Fact]
    public async Task EmailIsRetryableWhenEveryRecipientFailed()
    {
        var mail = new EmailMock { FailSends = true };
        var channel = new EmailNotificationChannel(Log, mail);

        var result = await channel.SendAsync(Message(),
            new ChannelConfiguration { Recipients = "a@acme.com" });

        Assert.False(result.Success);
        // An SMTP failure is nearly always transient — a down relay, a throttle.
        Assert.True(result.Retryable);
    }
}
