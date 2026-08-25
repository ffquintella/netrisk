using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DAL.Enums;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Microsoft Teams provider, posting an Adaptive Card to a Workflows (Power Automate) webhook
/// (Track 4 milestone 4.1.2).
///
/// Explicitly *not* the retired Office 365 connector format. Microsoft retired O365 connectors and
/// the <c>MessageCard</c> schema they accepted; a Workflows webhook expects an
/// <c>attachments[].content</c> envelope carrying an <c>AdaptiveCard</c>. Emitting the old
/// <c>MessageCard</c> would post successfully into some tenants for a while and then stop, which is
/// the worst failure mode available.
///
/// Teams' documented limit is roughly four requests per second; a 429 is surfaced as retryable with
/// its own <c>Retry-After</c>, exactly as for Slack.
/// </summary>
public class TeamsNotificationChannel(ILogger logger, IOutboundHttpClient http) : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.Teams;

    public string Name => "Microsoft Teams";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message,
        ChannelConfiguration configuration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return DeliveryResult.Permanent("This Teams channel has no Workflows webhook URL configured.");

        logger.Debug("Posting a {Event} notification to a Teams workflow", message.EventType);

        var response = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "POST",
            Url = configuration.WebhookUrl,
            Body = BuildPayload(message)
        }, ct);

        // A Workflows webhook answers 202 Accepted, not 200, so success is the whole 2xx range
        // rather than an equality check — which is what IsSuccess already is.
        if (response.IsSuccess) return DeliveryResult.Delivered(response.StatusCode);

        var detail = Describe(response);

        return response.IsRetryable
            ? DeliveryResult.Retry(detail, response.StatusCode, response.RetryAfter)
            : DeliveryResult.Permanent(detail, response.StatusCode);
    }

    public async Task<ChannelTestResult> TestAsync(ChannelConfiguration configuration,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return ChannelTestResult.Fail("No Workflows webhook URL is configured.");

        var stopwatch = Stopwatch.StartNew();
        var result = await SendAsync(SlackNotificationChannel.TestMessage(), configuration, ct);
        stopwatch.Stop();

        return result.Success
            ? ChannelTestResult.Ok("Test card accepted by the Teams workflow.", stopwatch.ElapsedMilliseconds)
            : ChannelTestResult.Fail(result.Error ?? "The test card was rejected.", stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// The Adaptive Card envelope a Workflows webhook expects: <c>type: message</c> with one
    /// attachment whose <c>contentType</c> is the Adaptive Card media type.
    /// </summary>
    internal static string BuildPayload(NotificationMessage message)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "message");
            json.WriteStartArray("attachments");
            json.WriteStartObject();
            json.WriteString("contentType", "application/vnd.microsoft.card.adaptive");
            json.WriteNull("contentUrl");

            json.WriteStartObject("content");
            json.WriteString("$schema", "http://adaptivecards.io/schemas/adaptive-card.json");
            json.WriteString("type", "AdaptiveCard");
            // 1.4 is the highest version Teams renders reliably across desktop, web and mobile.
            json.WriteString("version", "1.4");

            json.WriteStartArray("body");

            json.WriteStartObject();
            json.WriteString("type", "TextBlock");
            json.WriteString("text", $"[{message.SeverityLabel}] {NotificationRendering.TitleWithCount(message)}");
            json.WriteString("weight", "Bolder");
            json.WriteString("size", "Medium");
            json.WriteString("color", NotificationRendering.AdaptiveColourFor(message.Severity));
            json.WriteBoolean("wrap", true);
            json.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(message.Body))
            {
                json.WriteStartObject();
                json.WriteString("type", "TextBlock");
                json.WriteString("text", message.Body);
                json.WriteBoolean("wrap", true);
                json.WriteEndObject();
            }

            if (message.Fields.Count > 0)
            {
                json.WriteStartObject();
                json.WriteString("type", "FactSet");
                json.WriteStartArray("facts");
                foreach (var field in message.Fields)
                {
                    json.WriteStartObject();
                    json.WriteString("title", field.Label);
                    json.WriteString("value", field.Value);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteStartObject();
            json.WriteString("type", "TextBlock");
            json.WriteString("text",
                $"{NotificationCatalog.NameOf(message.EventType)} · {message.OccurredAt:yyyy-MM-dd HH:mm} UTC");
            json.WriteString("size", "Small");
            json.WriteBoolean("isSubtle", true);
            json.WriteBoolean("wrap", true);
            json.WriteEndObject();

            json.WriteEndArray();

            if (!string.IsNullOrWhiteSpace(message.Link))
            {
                json.WriteStartArray("actions");
                json.WriteStartObject();
                json.WriteString("type", "Action.OpenUrl");
                json.WriteString("title", "Open in NetRisk");
                json.WriteString("url", message.Link);
                json.WriteEndObject();
                json.WriteEndArray();
            }

            json.WriteEndObject();

            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Describe(OutboundHttpResponse response)
    {
        if (response.StatusCode == 0)
            return $"The Teams workflow could not be reached: {response.TransportError}";

        var body = string.IsNullOrWhiteSpace(response.Body) ? "" : $" — {Trim(response.Body)}";
        return $"The Teams workflow rejected the card with HTTP {response.StatusCode}{body}";
    }

    /// <summary>Power Automate error bodies are verbose; the first 400 characters carry the reason.</summary>
    private static string Trim(string body) =>
        body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
