using System.Text.Json;
using Contracts;
using Contracts.Secrets;
using Serilog;

namespace BastionVaultPlugin;

/// <summary>
/// The BastionVault integration: a secret-vault plugin that lets NetRisk store a *reference* to a
/// credential instead of the credential.
///
/// It authenticates with a single API key, optionally accompanied by the machine ID BastionVault
/// issued for the server NetRisk is installed on. The machine ID is optional here rather than
/// required, because BastionVault only binds keys to a machine when an account is configured that
/// way — see <see cref="RequiresMachineId"/> for what that costs and why it is the right default.
///
/// Stateless, as the contract requires: the host creates an instance per lookup, and everything it
/// needs arrives in the <see cref="SecretVaultContext"/>. The only field is the logger, which is set
/// by <see cref="Initialize"/> and is allowed to be null.
///
/// The wire protocol lives in <see cref="BastionVaultApi"/>, deliberately. This class holds the
/// behaviour that the SDK contract specifies — what to do on failure, what may appear in a message,
/// how a field is selected from a structured secret — and none of the JSON.
/// </summary>
public class BastionVaultSecretPlugin : INetriskSecretVaultPlugin
{
    private ILogger? _logger;

    public string PluginName => "BastionVaultPlugin";

    public string PluginVersion => "1.0.0";

    public string PluginDescription =>
        "Resolves NetRisk credential fields from a BastionVault instance, so integration keys, "
        + "database passwords and backup passphrases are stored in the vault and referenced here.";

    public string VaultKind => "bastionvault";

    /// <summary>
    /// False, and this is a judgement rather than an oversight.
    ///
    /// BastionVault binds a key to a machine only when the account is configured for it, so declaring
    /// the machine ID mandatory would make the plugin unusable for every installation that is not.
    /// The cost of the other choice — a machine-bound account whose connection has no machine ID —
    /// is a 401 on the first call, which <see cref="TestConnectionAsync"/> turns into a message that
    /// names the machine ID explicitly. A wrong "required" blocks working configurations; a wrong
    /// "optional" produces one clear error at the moment an administrator is looking at the form.
    /// </summary>
    public bool RequiresMachineId => false;

    public void Initialize(ILogger? logger)
    {
        _logger = logger;
        _logger?.Information("BastionVault secret-vault plugin {Version} initialized", PluginVersion);
    }

    public void Dispose()
    {
        // Nothing to release: the plugin owns no connection, no client and no cached state. The HTTP
        // seam belongs to the host.
        GC.SuppressFinalize(this);
    }

    public async Task<SecretVaultTestResult> TestConnectionAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The test *is* a list call. There is no cheaper "ping" that proves anything useful: an
        // endpoint that answers without checking the key tells an administrator their broken
        // configuration is fine, which is worse than no test at all.
        var response = await SendAsync(context, "GET",
            BastionVaultApi.Url(context.Credentials.BaseUrl, BastionVaultApi.ListPath), ct);

        if (!response.IsSuccess)
        {
            var message = BastionVaultApi.DescribeFailure(response.StatusCode, response.Body,
                response.TransportError);

            // The single most common misconfiguration, and the one the error text alone does not
            // point at: a machine-bound account reached without a machine ID.
            if (response.StatusCode is 401 or 403 && string.IsNullOrWhiteSpace(context.Credentials.MachineId))
                message += " No machine ID is set on this connection; if this BastionVault account is "
                           + "bound to a machine, enter the ID the vault issued for this server.";

            return SecretVaultTestResult.Fail(message);
        }

        try
        {
            var secrets = BastionVaultApi.ParseList(response.Body);

            var usable = secrets.Count(s => !string.IsNullOrWhiteSpace(s.Id));

            return SecretVaultTestResult.Ok(
                usable == 0
                    ? "Connected to BastionVault, but this API key can see no secrets. Grant it access "
                      + "to the secrets NetRisk should use."
                    : $"Connected to BastionVault. This API key can see {usable} secret(s).",
                usable);
        }
        catch (JsonException ex)
        {
            return SecretVaultTestResult.Fail(
                "BastionVault answered, but the response was not in the expected format: " + ex.Message);
        }
    }

    public async Task<IReadOnlyList<VaultSecretDescriptor>> ListSecretsAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = await SendAsync(context, "GET",
            BastionVaultApi.Url(context.Credentials.BaseUrl, BastionVaultApi.ListPath), ct);

        if (!response.IsSuccess)
            throw new SecretVaultException(BastionVaultApi.DescribeFailure(response.StatusCode,
                response.Body, response.TransportError));

        List<BastionVaultApi.SecretJson> secrets;

        try
        {
            secrets = BastionVaultApi.ParseList(response.Body);
        }
        catch (JsonException ex)
        {
            throw new SecretVaultException(
                "BastionVault returned a secret list that could not be read: " + ex.Message, ex);
        }

        var descriptors = new List<VaultSecretDescriptor>(secrets.Count);

        foreach (var secret in secrets)
        {
            // A secret with no id cannot be referenced, so offering it in the picker would produce a
            // selection that never resolves. Dropped, and counted in the log rather than silently.
            if (string.IsNullOrWhiteSpace(secret.Id)) continue;

            descriptors.Add(new VaultSecretDescriptor
            {
                Id = secret.Id,
                Name = string.IsNullOrWhiteSpace(secret.Name) ? secret.Id : secret.Name,
                Path = string.IsNullOrWhiteSpace(secret.Path) ? null : secret.Path,
                Description = secret.Description,
                Fields = secret.Fields?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList()
                         ?? (IReadOnlyList<string>)[],
                Version = secret.Version,
                UpdatedAt = secret.UpdatedAt
            });
        }

        var dropped = secrets.Count - descriptors.Count;
        if (dropped > 0)
            _logger?.Warning("BastionVault listed {Count} secret(s) with no id; they cannot be referenced",
                dropped);

        return descriptors;
    }

    public async Task<VaultSecretValue> GetSecretAsync(SecretVaultContext context,
        VaultSecretReference reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrWhiteSpace(reference.SecretId))
            throw new SecretVaultException("A BastionVault secret id is required.");

        var path = string.Format(BastionVaultApi.ReadPathFormat, Uri.EscapeDataString(reference.SecretId));

        var response = await SendAsync(context, "GET",
            BastionVaultApi.Url(context.Credentials.BaseUrl, path), ct);

        if (!response.IsSuccess)
            throw new SecretVaultException(BastionVaultApi.DescribeFailure(response.StatusCode,
                response.Body, response.TransportError));

        BastionVaultApi.SecretValueJson? payload;

        try
        {
            payload = JsonSerializer.Deserialize<BastionVaultApi.SecretValueJson>(
                response.Body ?? string.Empty, BastionVaultApi.Json);
        }
        catch (JsonException ex)
        {
            throw new SecretVaultException(
                "BastionVault returned a secret that could not be read: " + ex.Message, ex);
        }

        if (payload is null)
            throw new SecretVaultException("BastionVault returned an empty response for this secret.");

        var value = SelectValue(payload, reference);

        return new VaultSecretValue
        {
            Value = value,
            Version = payload.Version,
            MaxCacheAge = payload.MaxCacheSeconds is > 0
                ? TimeSpan.FromSeconds(payload.MaxCacheSeconds.Value)
                : null
        };
    }

    /// <summary>
    /// Picks the requested value out of a read response.
    ///
    /// The three cases and their failure modes, which are the whole of the method's difficulty:
    ///
    ///  * A field was asked for and the secret has it — the ordinary path. Matched
    ///    case-insensitively, because a person choosing "Password" from a picker and a vault storing
    ///    "password" is not a configuration error.
    ///  * A field was asked for and the secret does not have it — the field was renamed in the vault,
    ///    or the secret was replaced by a single-value one. This must fail, and must name the fields
    ///    that <em>do</em> exist: falling back to the single value would silently hand a username to
    ///    something expecting a password.
    ///  * No field was asked for — the single value, or, if the secret turns out to be structured
    ///    with exactly one field, that field. The last case is a convenience with no ambiguity, and it
    ///    is what makes a reference keep working when a vault administrator converts a plain secret
    ///    into a one-field one.
    /// </summary>
    private static string SelectValue(BastionVaultApi.SecretValueJson payload, VaultSecretReference reference)
    {
        var fields = payload.Fields;

        if (!string.IsNullOrWhiteSpace(reference.Field))
        {
            if (fields is not null)
                foreach (var pair in fields)
                    if (string.Equals(pair.Key, reference.Field, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(pair.Value))
                        return pair.Value;

            var available = fields is { Count: > 0 }
                ? "Available fields: " + string.Join(", ", fields.Keys) + "."
                : "This secret has no named fields.";

            throw new SecretVaultException(
                $"BastionVault secret '{reference.SecretId}' has no field '{reference.Field}'. {available} "
                + "Re-select the secret on the field that uses it.");
        }

        if (!string.IsNullOrEmpty(payload.Value)) return payload.Value;

        if (fields is { Count: 1 })
        {
            var only = fields.First();
            if (!string.IsNullOrEmpty(only.Value)) return only.Value;
        }

        if (fields is { Count: > 1 })
            throw new SecretVaultException(
                $"BastionVault secret '{reference.SecretId}' holds several fields "
                + $"({string.Join(", ", fields.Keys)}) and no single value. Re-select it and choose "
                + "which field to use.");

        throw new SecretVaultException(
            $"BastionVault returned no value for secret '{reference.SecretId}'.");
    }

    /// <summary>
    /// One request, with the credential attached.
    ///
    /// Goes through <c>context.Http</c> and never through an <c>HttpClient</c> of its own: that is
    /// what subjects the base URL an operator typed to the host's SSRF policy, and what lets the
    /// plugin's tests run without a network.
    /// </summary>
    private static Task<PluginHttpResponse> SendAsync(SecretVaultContext context, string method, string url,
        CancellationToken ct)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + context.Credentials.ApiKey,
            ["Accept"] = "application/json"
        };

        if (!string.IsNullOrWhiteSpace(context.Credentials.MachineId))
            headers[BastionVaultApi.MachineIdHeader] = context.Credentials.MachineId;

        return context.Http.SendAsync(new PluginHttpRequest
        {
            Method = method,
            Url = url,
            Headers = headers
        }, ct);
    }
}
