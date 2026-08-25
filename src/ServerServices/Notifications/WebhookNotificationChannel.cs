using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAL.Enums;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Generic webhook provider (Track 4 milestone 4.1.2): a documented, stable JSON body, custom
/// headers, and an HMAC-SHA256 signature so the receiver can prove the request came from NetRisk.
///
/// The signature is the reason this provider is not simply "POST the message". A webhook endpoint is
/// by definition reachable by anyone who learns its URL, so without a signature the receiver cannot
/// tell a NetRisk alert from a forged one — and a receiver that automatically opens tickets from
/// unauthenticated POSTs is a denial-of-service surface.
/// </summary>
public class WebhookNotificationChannel(ILogger logger, IOutboundHttpClient http) : INotificationChannel
{
    /// <summary>Header carrying <c>sha256=&lt;hex&gt;</c> over the exact request body.</summary>
    public const string SignatureHeader = "X-NetRisk-Signature";

    /// <summary>
    /// Header carrying the Unix seconds the signature was computed at. Included in the signed string
    /// so a captured request cannot be replayed indefinitely — the receiver rejects an old timestamp.
    /// </summary>
    public const string TimestampHeader = "X-NetRisk-Timestamp";

    /// <summary>Header naming the event, so a receiver can route without parsing the body.</summary>
    public const string EventHeader = "X-NetRisk-Event";

    /// <summary>Payload schema version. Bumped only for a breaking change to the body shape.</summary>
    public const string SchemaVersion = "1";

    public NotificationChannelKind Kind => NotificationChannelKind.Webhook;

    public string Name => "Webhook";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message,
        ChannelConfiguration configuration, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return DeliveryResult.Permanent("This webhook channel has no endpoint URL configured.");

        var body = BuildPayload(message);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var headers = new Dictionary<string, string>
        {
            [EventHeader] = NotificationCatalog.NameOf(message.EventType),
            [TimestampHeader] = timestamp
        };

        foreach (var (name, value) in configuration.Headers ?? new Dictionary<string, string>())
            headers[name] = value;

        // Unsigned is allowed but is a real gap, so it is stated in the log rather than being
        // indistinguishable from a signed send.
        if (!string.IsNullOrEmpty(configuration.SigningSecret))
            headers[SignatureHeader] = Sign(timestamp, body, configuration.SigningSecret);
        else
            logger.Warning("Webhook channel has no signing secret; the receiver cannot verify authenticity");

        var response = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "POST",
            Url = configuration.WebhookUrl,
            Body = body,
            Headers = headers
        }, ct);

        if (response.IsSuccess) return DeliveryResult.Delivered(response.StatusCode);

        var detail = response.StatusCode == 0
            ? $"The webhook endpoint could not be reached: {response.TransportError}"
            : $"The webhook endpoint answered HTTP {response.StatusCode}";

        return response.IsRetryable
            ? DeliveryResult.Retry(detail, response.StatusCode, response.RetryAfter)
            : DeliveryResult.Permanent(detail, response.StatusCode);
    }

    public async Task<ChannelTestResult> TestAsync(ChannelConfiguration configuration,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.WebhookUrl))
            return ChannelTestResult.Fail("No endpoint URL is configured.");

        var stopwatch = Stopwatch.StartNew();
        var result = await SendAsync(SlackNotificationChannel.TestMessage(), configuration, ct);
        stopwatch.Stop();

        if (!result.Success)
            return ChannelTestResult.Fail(result.Error ?? "The test request was rejected.",
                stopwatch.ElapsedMilliseconds);

        return ChannelTestResult.Ok(
            string.IsNullOrEmpty(configuration.SigningSecret)
                ? "Test request accepted. It was sent unsigned — set a signing secret so the receiver "
                  + "can verify that requests come from NetRisk."
                : "Test request accepted and signed.",
            stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// The documented payload. Field names are part of the contract for anyone who has written a
    /// receiver, so they are stable and <see cref="SchemaVersion"/> is what changes if they cannot be.
    /// </summary>
    internal static string BuildPayload(NotificationMessage message)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("schemaVersion", SchemaVersion);
            json.WriteString("event", NotificationCatalog.NameOf(message.EventType));
            json.WriteString("occurredAt", message.OccurredAt.ToString("o"));
            json.WriteString("title", message.Title);
            json.WriteString("body", message.Body);
            json.WriteString("severityLabel", message.SeverityLabel);

            if (message.Severity != null) json.WriteNumber("severity", message.Severity.Value);
            else json.WriteNull("severity");

            if (message.Link != null) json.WriteString("link", message.Link); else json.WriteNull("link");

            json.WriteNumber("aggregatedCount", message.AggregatedCount);

            json.WriteStartObject("subject");
            if (message.SubjectType != null) json.WriteString("type", message.SubjectType);
            else json.WriteNull("type");
            if (message.SubjectId != null) json.WriteNumber("id", message.SubjectId.Value);
            else json.WriteNull("id");
            if (message.EntityId != null) json.WriteNumber("entityId", message.EntityId.Value);
            else json.WriteNull("entityId");
            json.WriteEndObject();

            // An object rather than an array of {label,value}: a receiver wants
            // payload.fields.Severity, not a scan of a list.
            json.WriteStartObject("fields");
            foreach (var field in message.Fields)
                json.WriteString(field.Label, field.Value);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// <c>sha256=&lt;hex&gt;</c> over <c>"{timestamp}.{body}"</c>.
    ///
    /// The timestamp is inside the signed string, not merely alongside it: signing only the body
    /// lets an observer replay a captured request forever, and a receiver has no way to detect it.
    /// The <c>sha256=</c> prefix follows the convention GitHub and Stripe use, so an existing
    /// verification helper works unchanged.
    /// </summary>
    internal static string Sign(string timestamp, string body, string secret)
    {
        var material = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var key = Encoding.UTF8.GetBytes(secret);
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(key, material)).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a signature the way a receiver should, exposed so the documentation and the tests
    /// share one implementation. Fixed-time comparison, because a byte-by-byte early exit lets an
    /// attacker recover a valid signature one character at a time.
    /// </summary>
    public static bool VerifySignature(string timestamp, string body, string secret, string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;

        var expected = Sign(timestamp, body, secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }
}
