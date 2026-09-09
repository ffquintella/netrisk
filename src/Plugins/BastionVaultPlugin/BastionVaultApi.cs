using System.Text.Json;
using System.Text.Json.Serialization;

namespace BastionVaultPlugin;

/// <summary>
/// The BastionVault REST surface this plugin speaks, and the JSON it exchanges.
///
/// <b>Everything the vault's wire protocol dictates is in this one file.</b> That is the point of
/// separating it from <see cref="BastionVaultSecretPlugin"/>: if BastionVault's paths, header names
/// or payload field names differ from what is assumed here, this is the only file to change, and
/// <c>BastionVaultPlugin.Tests</c> pins the mapping so the change is visible.
///
/// The assumed contract:
///
/// <code>
/// GET  {base}/api/v1/secrets            -> { "secrets": [ { id, name, path, description,
///                                                            fields: [..], version, updatedAt } ] }
/// GET  {base}/api/v1/secrets/{id}       -> { id, version, value, fields: { name: value },
///                                            maxCacheSeconds }
/// </code>
///
/// with <c>Authorization: Bearer {apiKey}</c> on every request and
/// <c>X-BastionVault-Machine-Id: {machineId}</c> added when the connection carries one.
///
/// The response readers are deliberately tolerant about *shape* and strict about *content*: a list
/// that comes back as a bare JSON array rather than wrapped in <c>secrets</c> is accepted, because
/// that difference costs an operator an afternoon and costs us four lines; but a secret with no id
/// is dropped, because a descriptor whose id cannot be stored produces a reference that never
/// resolves.
/// </summary>
internal static class BastionVaultApi
{
    /// <summary>Path of the list endpoint, relative to the connection's base URL.</summary>
    internal const string ListPath = "/api/v1/secrets";

    /// <summary>Path template of the read endpoint. <c>{0}</c> is the URL-escaped secret id.</summary>
    internal const string ReadPathFormat = "/api/v1/secrets/{0}";

    /// <summary>
    /// The machine-binding header. BastionVault issues a machine id for the host an installation runs
    /// on; a key presented without it from a machine-bound account is refused by the vault.
    /// </summary>
    internal const string MachineIdHeader = "X-BastionVault-Machine-Id";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Builds an absolute URL from the connection's base URL and a relative path.</summary>
    internal static string Url(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    /// <summary>The list envelope, and its bare-array fallback.</summary>
    internal sealed class ListResponse
    {
        [JsonPropertyName("secrets")]
        public List<SecretJson>? Secrets { get; set; }
    }

    internal sealed class SecretJson
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Field names for a structured secret. Names only — the list endpoint returns no values.</summary>
        [JsonPropertyName("fields")]
        public List<string>? Fields { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>The read response: a single value, a map of fields, or both.</summary>
    internal sealed class SecretValueJson
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>The value of a single-value secret.</summary>
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        /// <summary>Field name → value, for a structured secret.</summary>
        [JsonPropertyName("fields")]
        public Dictionary<string, string>? Fields { get; set; }

        /// <summary>
        /// The vault's own cap on how long this value may be held, in seconds. NetRisk takes the
        /// shorter of this and the connection's configured TTL.
        /// </summary>
        [JsonPropertyName("maxCacheSeconds")]
        public int? MaxCacheSeconds { get; set; }
    }

    /// <summary>
    /// Reads a list response, accepting either the documented envelope or a bare array.
    /// </summary>
    internal static List<SecretJson> ParseList(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var trimmed = body.TrimStart();

        if (trimmed.StartsWith('['))
            return JsonSerializer.Deserialize<List<SecretJson>>(body, Json) ?? [];

        return JsonSerializer.Deserialize<ListResponse>(body, Json)?.Secrets ?? [];
    }

    /// <summary>
    /// Extracts an error message from a failed response without echoing a credential.
    ///
    /// A vault's error body is the one place a credential is most likely to be reflected back — "key
    /// abc123 is not authorized" is a real thing APIs say — and this message reaches an operator's
    /// screen and NetRisk's log. So the body is used only when it parses as JSON with an
    /// <c>error</c> or <c>message</c> field, and is truncated; a body that is anything else is
    /// reduced to its status code.
    /// </summary>
    internal static string DescribeFailure(int statusCode, string? body, string? transportError)
    {
        if (statusCode == 0)
            return $"BastionVault could not be reached: {transportError ?? "no response"}";

        var detail = ExtractErrorField(body);

        var reason = statusCode switch
        {
            401 or 403 =>
                "BastionVault rejected the credential (HTTP " + statusCode
                + "). Check the API key, and the machine ID if the vault binds keys to a machine.",
            404 => "BastionVault has no such secret (HTTP 404).",
            429 => "BastionVault is rate limiting this connection (HTTP 429).",
            _ => $"BastionVault returned HTTP {statusCode}."
        };

        return detail is null ? reason : reason + " " + detail;
    }

    private static string? ExtractErrorField(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in (string[])["error", "message", "detail"])
            {
                if (!document.RootElement.TryGetProperty(name, out var element)) continue;
                if (element.ValueKind != JsonValueKind.String) continue;

                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                return text.Length > 200 ? text[..200] + "…" : text;
            }
        }
        catch (JsonException)
        {
            // Not JSON. An HTML error page or a proxy's plain-text refusal says nothing useful and
            // may contain anything at all, so the status code stands on its own.
        }

        return null;
    }
}
