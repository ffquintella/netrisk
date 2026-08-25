using System.Text.Json;
using System.Text.Json.Serialization;

namespace Model.Notifications;

/// <summary>
/// The provider settings held in <c>notification_channels.configuration_json</c>, parsed
/// (Track 4 milestone 4.1.2).
///
/// One type covering all four providers, with each field documented as to which provider reads it.
/// The alternative — a class per provider and a discriminated deserialization — buys type safety at
/// the cost of the admin UI having to know four shapes, and every field here is optional anyway
/// because a half-filled form has to round-trip.
/// </summary>
public class ChannelConfiguration
{
    /// <summary>
    /// Slack incoming-webhook URL, Teams Workflows URL, or the generic webhook endpoint. A secret in
    /// all three cases: possession of the URL is authorization to post.
    /// </summary>
    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    /// <summary>Email: comma-separated recipients.</summary>
    [JsonPropertyName("recipients")]
    public string? Recipients { get; set; }

    /// <summary>Email: subject prefix, so a mail rule can file NetRisk alerts.</summary>
    [JsonPropertyName("subjectPrefix")]
    public string? SubjectPrefix { get; set; }

    /// <summary>Generic webhook: HMAC-SHA256 signing secret for <c>X-NetRisk-Signature</c>.</summary>
    [JsonPropertyName("signingSecret")]
    public string? SigningSecret { get; set; }

    /// <summary>Generic webhook: extra headers, name → value. Where an API key goes.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Slack: channel override (<c>#soc-alerts</c>). Only honoured by legacy webhooks; modern ones
    /// are bound to a channel at creation, which the test message says out loud.
    /// </summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>
    /// Base URL the deep links are built from — <c>https://netrisk.acme.com</c>. Per channel because
    /// an internal Slack and an external webhook may legitimately need different hostnames.
    /// </summary>
    [JsonPropertyName("appBaseUrl")]
    public string? AppBaseUrl { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Parses stored configuration. A malformed or empty value yields an empty configuration rather
    /// than throwing: the dispatcher then fails the delivery with "no webhook URL configured", which
    /// is a message an operator can act on, instead of a JSON parse error in a job log.
    /// </summary>
    public static ChannelConfiguration Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ChannelConfiguration();

        try
        {
            return JsonSerializer.Deserialize<ChannelConfiguration>(json, Options) ?? new ChannelConfiguration();
        }
        catch (JsonException)
        {
            return new ChannelConfiguration();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// The fields that must never leave the server in clear. Used both to encrypt on write and to
    /// redact on read, so the two lists cannot drift apart — which is how a "redacted" DTO ends up
    /// still carrying the signing secret.
    /// </summary>
    public bool HasSecrets => !string.IsNullOrEmpty(WebhookUrl) || !string.IsNullOrEmpty(SigningSecret)
                                                                || (Headers?.Count ?? 0) > 0;

    /// <summary>
    /// A copy with every secret replaced by a placeholder, for the admin UI. The placeholder is
    /// deliberately recognizable so a form that saves it unchanged can be detected and the stored
    /// value left alone.
    /// </summary>
    public const string RedactedPlaceholder = "••••••••";

    public ChannelConfiguration Redacted() => new()
    {
        WebhookUrl = string.IsNullOrEmpty(WebhookUrl) ? null : RedactedPlaceholder,
        Recipients = Recipients,
        SubjectPrefix = SubjectPrefix,
        SigningSecret = string.IsNullOrEmpty(SigningSecret) ? null : RedactedPlaceholder,
        Headers = Headers?.ToDictionary(h => h.Key, _ => RedactedPlaceholder),
        Channel = Channel,
        AppBaseUrl = AppBaseUrl
    };
}
