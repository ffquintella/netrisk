using System.Diagnostics;
using System.Text.Json;
using DAL.Enums;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Slack incoming-webhook provider, rendering Block Kit (Track 4 milestone 4.1.2).
///
/// Block Kit rather than the legacy <c>attachments</c> array: attachments are deprecated, and the
/// field grid and the "Open in NetRisk" button — the two things that make an alert actionable — are
/// only properly supported by blocks. A colour-coded attachment is still emitted alongside, because
/// that is the only way to get a severity stripe down the left of the message.
///
/// Rate limiting is Slack's documented ~1 message per second per channel, answered with HTTP 429 and
/// a <c>Retry-After</c> header. That is surfaced as a retryable result with the provider's own
/// back-off rather than a fixed one, because guessing shorter than Slack asked for is what turns a
/// brief limit into a sustained one.
/// </summary>
public class SlackNotificationChannel(ILogger logger, IOutboundHttpClient http) : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.Slack;

    public string Name => "Slack";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message,
        ChannelConfiguration configuration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return DeliveryResult.Permanent("This Slack channel has no incoming-webhook URL configured.");

        var payload = BuildPayload(message, configuration);

        logger.Debug("Posting a {Event} notification to Slack", message.EventType);

        var response = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "POST",
            Url = configuration.WebhookUrl,
            Body = payload
        }, ct);

        if (response.IsSuccess) return DeliveryResult.Delivered(response.StatusCode);

        // Slack answers a webhook failure with a short text body ("invalid_payload", "channel_not_found")
        // rather than JSON, and that text is the whole diagnosis, so it is kept.
        var detail = Describe(response);

        return response.IsRetryable
            ? DeliveryResult.Retry(detail, response.StatusCode, response.RetryAfter)
            : DeliveryResult.Permanent(detail, response.StatusCode);
    }

    public async Task<ChannelTestResult> TestAsync(ChannelConfiguration configuration,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return ChannelTestResult.Fail("No incoming-webhook URL is configured.");

        var stopwatch = Stopwatch.StartNew();

        var result = await SendAsync(TestMessage(), configuration, ct);

        stopwatch.Stop();

        if (!result.Success)
            return ChannelTestResult.Fail(result.Error ?? "The test message was rejected.",
                stopwatch.ElapsedMilliseconds);

        // Said explicitly because a modern Slack webhook ignores the channel override, and an
        // operator who set one and saw a success would otherwise assume it was honoured.
        var note = string.IsNullOrWhiteSpace(configuration.Channel)
            ? "Test message delivered."
            : "Test message delivered. Note: modern Slack webhooks post to the channel chosen when the "
              + "webhook was created; the configured channel override is ignored.";

        return ChannelTestResult.Ok(note, stopwatch.ElapsedMilliseconds);
    }

    internal static NotificationMessage TestMessage() => new()
    {
        EventType = NotificationEventType.RiskCreated,
        Severity = 2,
        Title = "NetRisk test notification",
        Body = "If you can read this, NetRisk can post to this channel.",
        Fields = [new NotificationField("Source", "Connection test")],
        OccurredAt = DateTime.UtcNow
    };

    /// <summary>
    /// The Block Kit payload. Built with a writer rather than an anonymous-object graph because the
    /// block list is conditional (no button without a link, no field section without fields) and a
    /// nested ternary soup of anonymous types is unreadable and untestable.
    /// </summary>
    internal static string BuildPayload(NotificationMessage message, ChannelConfiguration configuration)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();

            // The notification/preview text. Slack requires it, screen readers use it, and it is parsed
            // as mrkdwn — so it is escaped like the other mrkdwn fields rather than left raw.
            json.WriteString("text", Escape(NotificationRendering.TitleWithCount(message)));

            if (!string.IsNullOrWhiteSpace(configuration.Channel))
                json.WriteString("channel", configuration.Channel);

            // Everything visual goes inside one colour-striped attachment; the stripe is the only
            // severity affordance Slack offers.
            json.WriteStartArray("attachments");
            json.WriteStartObject();
            json.WriteString("color", NotificationRendering.ColourFor(message.Severity));
            json.WriteStartArray("blocks");

            json.WriteStartObject();
            json.WriteString("type", "header");
            json.WriteStartObject("text");
            json.WriteString("type", "plain_text");
            // Slack truncates a header at 150 characters and rejects a longer one outright.
            json.WriteString("text", Truncate($"[{message.SeverityLabel}] {NotificationRendering.TitleWithCount(message)}", 150));
            json.WriteEndObject();
            json.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(message.Body))
            {
                json.WriteStartObject();
                json.WriteString("type", "section");
                json.WriteStartObject("text");
                json.WriteString("type", "mrkdwn");
                json.WriteString("text", Truncate(Escape(message.Body), 3000));
                json.WriteEndObject();
                json.WriteEndObject();
            }

            // Slack allows at most ten fields in a section, so the list is chunked rather than
            // truncated: dropping the tenth field silently is how the asset name disappears.
            foreach (var chunk in message.Fields.Chunk(10))
            {
                json.WriteStartObject();
                json.WriteString("type", "section");
                json.WriteStartArray("fields");
                foreach (var field in chunk)
                {
                    json.WriteStartObject();
                    json.WriteString("type", "mrkdwn");
                    json.WriteString("text", Truncate($"*{Escape(field.Label)}*\n{Escape(field.Value)}", 2000));
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }

            if (!string.IsNullOrWhiteSpace(message.Link))
            {
                json.WriteStartObject();
                json.WriteString("type", "actions");
                json.WriteStartArray("elements");
                json.WriteStartObject();
                json.WriteString("type", "button");
                json.WriteStartObject("text");
                json.WriteString("type", "plain_text");
                json.WriteString("text", "Open in NetRisk");
                json.WriteEndObject();
                json.WriteString("url", message.Link);
                json.WriteString("style", message.Severity >= 3 ? "danger" : "primary");
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteStartObject();
            json.WriteString("type", "context");
            json.WriteStartArray("elements");
            json.WriteStartObject();
            json.WriteString("type", "mrkdwn");
            json.WriteString("text",
                $"{NotificationCatalog.NameOf(message.EventType)} · {message.OccurredAt:yyyy-MM-dd HH:mm} UTC");
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Slack's mrkdwn requires only these three to be entity-escaped. Escaping more (as HTML would)
    /// makes an asterisk in a finding title show up as <c>&amp;ast;</c>.
    /// </summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Describe(OutboundHttpResponse response)
    {
        if (response.StatusCode == 0)
            return $"Slack could not be reached: {response.TransportError}";

        var body = string.IsNullOrWhiteSpace(response.Body) ? "" : $" — {response.Body.Trim()}";
        return $"Slack rejected the message with HTTP {response.StatusCode}{body}";
    }
}
