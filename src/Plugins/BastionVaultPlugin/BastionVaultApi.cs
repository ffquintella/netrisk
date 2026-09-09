using System.Text.Json;
using System.Text.Json.Serialization;

namespace BastionVaultPlugin;

/// <summary>
/// The BastionVault HTTP surface this plugin speaks, and the JSON it exchanges.
///
/// BastionVault is HashiCorp-Vault-compatible: every route lives under <c>/v1/</c>, the caller
/// authenticates with a token in <c>X-Vault-Token</c>, and secrets are reached through a mounted
/// secrets engine at a logical path rather than through a flat catalogue of ids.
///
/// <b>Everything the wire protocol dictates is in this one file</b>, so a change to BastionVault is a
/// change here and nowhere else, and <c>BastionVaultPlugin.Tests</c> pins the mapping.
///
/// Four details are easy to get wrong and were verified against the server source rather than the
/// reference documentation:
///
///  * <b>Listing needs the <c>LIST</c> verb.</b> <c>docs/api.md</c> also offers
///    <c>GET …?list=true</c>, but the logical router maps <c>GET</c> to <c>Operation::Read</c>
///    unconditionally and lifts only <c>env</c> and <c>version</c> out of the query string
///    (<c>bv-logical/src/util.rs</c>). A <c>GET …?list=true</c> therefore <em>reads the secret</em>
///    instead of listing under it — quietly, and with a value in the response.
///  * <b>A listing is one level deep and folders carry a trailing slash.</b>
///    <c>bv-storage/.../local.rs</c> appends <c>/</c> to directory entries and strips the storage
///    prefix from leaves, so enumerating an estate is a recursive walk, not a single call.
///  * <b>Every secret is a map</b>, not a value: a read answers <c>data: { field: value, … }</c>.
///    There is no separate single-value shape to fall back to.
///  * <b>The lease is in <c>lease_duration</c> seconds</b>, at the envelope level rather than inside
///    <c>data</c>.
/// </summary>
internal static class BastionVaultApi
{
    /// <summary>The API prefix. Both <c>/v1</c> and <c>/v2</c> are served; <c>/v1</c> is the documented one.</summary>
    internal const string ApiPrefix = "/v1/";

    /// <summary>
    /// The authentication header. Not <c>Authorization: Bearer</c> — BastionVault reads
    /// <c>X-Vault-Token</c> (or a <c>token</c> cookie), and a bearer header is simply ignored, which
    /// presents as an unauthenticated request rather than as a rejected credential.
    /// </summary>
    internal const string TokenHeader = "X-Vault-Token";

    /// <summary>The non-standard verb BastionVault's clients use for list operations.</summary>
    internal const string ListMethod = "LIST";

    /// <summary>Mount table: which secrets engines exist and what type each is.</summary>
    internal const string MountsPath = "sys/mounts";

    /// <summary>Introspects the presented token — the cheapest call that actually proves it is valid.</summary>
    internal const string LookupSelfPath = "auth/token/lookup-self";

    /// <summary>
    /// Unauthenticated: whether this server refuses any session that is not machine-bound.
    /// Answers 404 when the FerroGate auth method is not mounted, which is the common case.
    /// </summary>
    internal const string MachineRequirementPath = "auth/ferrogate/requirement";

    /// <summary>
    /// The token metadata key a FerroGate machine login writes
    /// (<c>bv-auth-ferrogate/src/path_machines.rs</c>). It is a reserved key, so
    /// <c>auth/token/create</c> refuses a request that supplies one — which is what makes its
    /// presence trustworthy evidence that the token really is machine-bound.
    /// </summary>
    internal const string MachineIdentityMetaKey = "spiffe_id";

    /// <summary>The engine types whose contents are secrets a caller can reference.</summary>
    private static readonly string[] SecretEngineTypes = ["kv", "generic"];

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Builds an absolute URL for a logical path such as <c>secret/prod/db</c>.</summary>
    internal static string Url(string baseUrl, string logicalPath) =>
        baseUrl.TrimEnd('/') + ApiPrefix + logicalPath.TrimStart('/');

    /// <summary>
    /// The standard response envelope. <c>data</c> is the payload for every endpoint this plugin
    /// touches; <c>errors</c> is present instead on a failure.
    /// </summary>
    internal sealed class Envelope
    {
        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        /// <summary>Seconds the value may be held. 0 means the server expressed no opinion.</summary>
        [JsonPropertyName("lease_duration")]
        public long LeaseDuration { get; set; }

        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }
    }

    /// <summary>The <c>requirement</c> endpoint's payload.</summary>
    internal sealed class MachineRequirement
    {
        [JsonPropertyName("require_machine_identity")]
        public bool RequireMachineIdentity { get; set; }

        [JsonPropertyName("expected_audience")]
        public string? ExpectedAudience { get; set; }

        [JsonPropertyName("trust_domain")]
        public string? TrustDomain { get; set; }

        [JsonPropertyName("mia_environment")]
        public string? MiaEnvironment { get; set; }
    }

    /// <summary>What <c>auth/token/lookup-self</c> tells us about the presented token.</summary>
    internal sealed class TokenInfo
    {
        public List<string> Policies { get; init; } = [];

        public Dictionary<string, string> Meta { get; init; } = new(StringComparer.Ordinal);

        public string? DisplayName { get; init; }

        /// <summary>The machine this token is bound to, or null when it is an ordinary token.</summary>
        public string? MachineIdentity =>
            Meta.TryGetValue(MachineIdentityMetaKey, out var id) && !string.IsNullOrWhiteSpace(id)
                ? id
                : null;
    }

    internal static Envelope? ParseEnvelope(string? body) =>
        string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<Envelope>(body, Json);

    /// <summary>
    /// The KV mounts in the mount table, as logical prefixes ending in <c>/</c>.
    ///
    /// <c>cubbyhole</c> is excluded deliberately even though it is a kv engine: it is per-token
    /// private storage, so anything NetRisk could see there would vanish with the token that listed
    /// it, and a reference into it would never resolve again.
    /// </summary>
    internal static List<string> ParseSecretMounts(Envelope? envelope)
    {
        var mounts = new List<string>();

        if (envelope?.Data is not { ValueKind: JsonValueKind.Object } data) return mounts;

        foreach (var mount in data.EnumerateObject())
        {
            if (!mount.Value.TryGetProperty("type", out var type)) continue;
            if (type.ValueKind != JsonValueKind.String) continue;

            var engine = type.GetString();
            if (engine is null || !SecretEngineTypes.Contains(engine, StringComparer.OrdinalIgnoreCase))
                continue;

            var path = mount.Name;
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (path.StartsWith("cubbyhole", StringComparison.OrdinalIgnoreCase)) continue;

            mounts.Add(path.EndsWith('/') ? path : path + "/");
        }

        mounts.Sort(StringComparer.OrdinalIgnoreCase);
        return mounts;
    }

    /// <summary>The <c>keys</c> of a list response. Folder entries keep their trailing slash.</summary>
    internal static List<string> ParseKeys(Envelope? envelope)
    {
        var keys = new List<string>();

        if (envelope?.Data is not { ValueKind: JsonValueKind.Object } data) return keys;
        if (!data.TryGetProperty("keys", out var array) || array.ValueKind != JsonValueKind.Array) return keys;

        foreach (var key in array.EnumerateArray())
            if (key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 } name)
                keys.Add(name);

        return keys;
    }

    /// <summary>
    /// A secret's fields. Non-string values are rendered as their raw JSON, because a credential
    /// stored as a number or an object is still the credential the caller asked for, and refusing it
    /// would be a surprise the operator cannot act on.
    /// </summary>
    internal static Dictionary<string, string> ParseFields(Envelope? envelope)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        if (envelope?.Data is not { ValueKind: JsonValueKind.Object } data) return fields;

        foreach (var field in data.EnumerateObject())
            fields[field.Name] = field.Value.ValueKind == JsonValueKind.String
                ? field.Value.GetString() ?? string.Empty
                : field.Value.GetRawText();

        return fields;
    }

    internal static TokenInfo ParseTokenInfo(Envelope? envelope)
    {
        var info = new TokenInfo();

        if (envelope?.Data is not { ValueKind: JsonValueKind.Object } data) return info;

        if (data.TryGetProperty("policies", out var policies) && policies.ValueKind == JsonValueKind.Array)
            foreach (var policy in policies.EnumerateArray())
                if (policy.ValueKind == JsonValueKind.String && policy.GetString() is { } name)
                    info.Policies.Add(name);

        if (data.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            foreach (var entry in meta.EnumerateObject())
                info.Meta[entry.Name] = entry.Value.ValueKind == JsonValueKind.String
                    ? entry.Value.GetString() ?? string.Empty
                    : entry.Value.GetRawText();

        var displayName = data.TryGetProperty("display_name", out var display)
                          && display.ValueKind == JsonValueKind.String
            ? display.GetString()
            : null;

        return new TokenInfo
        {
            Policies = info.Policies,
            Meta = info.Meta,
            DisplayName = displayName
        };
    }

    /// <summary>
    /// Turns a failed response into a message for an operator, without echoing the token.
    ///
    /// BastionVault reports failures as <c>{"errors":["…"]}</c>, and those strings are written for a
    /// human, so they are quoted back — but only when the body actually parses as that shape.
    /// Anything else (a proxy's HTML page, a TLS interception notice) is reduced to its status code,
    /// because an arbitrary body may contain anything at all, including a reflected credential.
    /// </summary>
    internal static string DescribeFailure(int statusCode, string? body, string? transportError)
    {
        if (statusCode == 0)
            return $"BastionVault could not be reached: {transportError ?? "no response"}";

        var reason = statusCode switch
        {
            400 => "BastionVault refused the request as invalid (HTTP 400).",
            403 =>
                "BastionVault denied this token (HTTP 403). Either the token is invalid or expired, or "
                + "its policies do not grant access to this path.",
            404 => "BastionVault has nothing at that path (HTTP 404).",
            405 =>
                "BastionVault refused the method (HTTP 405). A list operation needs the LIST verb; a "
                + "proxy in front of the vault may be dropping it.",
            429 => "BastionVault is rate limiting this connection (HTTP 429).",
            // The one status with a remedy that has nothing to do with NetRisk's configuration.
            503 => "BastionVault is sealed (HTTP 503). It must be unsealed before it can serve secrets.",
            _ => $"BastionVault returned HTTP {statusCode}."
        };

        var detail = ExtractError(body);

        return detail is null ? reason : reason + " " + detail;
    }

    private static string? ExtractError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(body, Json);

            if (envelope?.Errors is not { Count: > 0 } errors) return null;

            var text = string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));

            if (text.Length == 0) return null;

            return text.Length > 200 ? text[..200] + "…" : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
