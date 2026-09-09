using System.Text.Json;
using Contracts;
using Contracts.Secrets;
using Serilog;

namespace BastionVaultPlugin;

/// <summary>
/// The BastionVault integration: a secret-vault plugin that lets NetRisk store a *reference* to a
/// credential instead of the credential.
///
/// BastionVault is HashiCorp-Vault-compatible, which shapes three things about this plugin.
///
/// <para><b>The "API key" is a vault token.</b> It goes in <c>X-Vault-Token</c>, and everything the
/// connection can reach is whatever that token's policies allow — so the operator scopes NetRisk's
/// access in the vault, not here.</para>
///
/// <para><b>A secret is a path, and a path is a map.</b> There is no flat catalogue of ids: secrets
/// live under mounted engines (<c>secret/prod/db</c>), and reading one returns
/// <c>{ username: …, password: … }</c>. So a reference to a BastionVault secret almost always names
/// a field as well.</para>
///
/// <para><b>Machine identity is not a header.</b> FerroGate machine auth is a DPoP-bound attestation
/// flow that requires a local Machine Identity Agent; a headless application obtains a machine-bound
/// token with <c>bvault ferrogate token</c> and then presents it like any other token. So the machine
/// ID on a NetRisk connection is not something this plugin *sends* — it is what
/// <see cref="TestConnectionAsync"/> checks the supplied token is actually bound to. See
/// <see cref="RequiresMachineId"/>.</para>
///
/// Stateless, as the contract requires: the host creates an instance per lookup and everything
/// arrives in the <see cref="SecretVaultContext"/>. The wire protocol lives in
/// <see cref="BastionVaultApi"/>.
/// </summary>
public class BastionVaultSecretPlugin : INetriskSecretVaultPlugin
{
    /// <summary>
    /// How many secrets a single enumeration will return.
    ///
    /// A vault is a tree and NetRisk walks it one LIST per folder, so an estate with tens of
    /// thousands of secrets would otherwise turn opening the picker into a sustained burst of
    /// requests against somebody's production vault. The cap is generous enough that no realistic
    /// NetRisk installation notices it, and the picker has a filter for the rest.
    /// </summary>
    private const int MaxSecrets = 2000;

    /// <summary>
    /// How deep the walk goes. Guards against a pathological or cyclic-looking tree; ten levels is
    /// far past any sane secret layout.
    /// </summary>
    private const int MaxDepth = 10;

    private ILogger? _logger;

    public string PluginName => "BastionVaultPlugin";

    public string PluginVersion => "1.1.0";

    public string PluginDescription =>
        "Resolves NetRisk credential fields from a BastionVault instance, so integration keys, "
        + "database passwords and backup passphrases are stored in the vault and referenced here.";

    public string VaultKind => "bastionvault";

    /// <summary>
    /// False, and this is a judgement rather than an oversight.
    ///
    /// A BastionVault server only refuses non-machine-bound sessions when an administrator has turned
    /// <c>require_machine_identity</c> on, and most have not. Declaring the machine ID mandatory would
    /// make the plugin unusable for every server that has not — so instead
    /// <see cref="TestConnectionAsync"/> asks the server (through the unauthenticated
    /// <c>auth/ferrogate/requirement</c> endpoint) whether it demands machine identity, and fails the
    /// test with a specific message when the supplied token cannot satisfy it. A wrong "required"
    /// blocks working configurations; this way the operator learns the truth from the server, at the
    /// moment they are looking at the form.
    /// </summary>
    public bool RequiresMachineId => false;

    public void Initialize(ILogger? logger)
    {
        _logger = logger;
        _logger?.Information("BastionVault secret-vault plugin {Version} initialized", PluginVersion);
    }

    public void Dispose()
    {
        // Nothing to release: the plugin owns no connection, no client and no cached state.
        GC.SuppressFinalize(this);
    }

    // --- connection test --------------------------------------------------------------------

    public async Task<SecretVaultTestResult> TestConnectionAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Introspecting the token is the cheapest call that actually proves it is valid, and the
        // `default` policy grants it to every token — so a failure here is a real authentication
        // problem rather than a missing grant.
        var lookup = await SendAsync(context, "GET", BastionVaultApi.LookupSelfPath, ct);

        if (!lookup.IsSuccess)
            return SecretVaultTestResult.Fail(BastionVaultApi.DescribeFailure(
                lookup.StatusCode, lookup.Body, lookup.TransportError));

        BastionVaultApi.TokenInfo token;

        try
        {
            token = BastionVaultApi.ParseTokenInfo(BastionVaultApi.ParseEnvelope(lookup.Body));
        }
        catch (JsonException ex)
        {
            return SecretVaultTestResult.Fail(
                "BastionVault answered, but the token introspection response was not in the expected "
                + "format: " + ex.Message);
        }

        if (await CheckMachineIdentityAsync(context, token, ct) is { } refusal)
            return SecretVaultTestResult.Fail(refusal);

        // Only now is it worth walking the tree.
        List<VaultSecretDescriptor> secrets;

        try
        {
            secrets = await WalkAsync(context, ct);
        }
        catch (SecretVaultException ex)
        {
            return SecretVaultTestResult.Fail(
                $"The token is valid, but its secrets could not be listed: {ex.Message}");
        }

        var bound = token.MachineIdentity is null
            ? string.Empty
            : $" The token is bound to machine {token.MachineIdentity}.";

        var policies = token.Policies.Count == 0
            ? string.Empty
            : $" Policies: {string.Join(", ", token.Policies)}.";

        return SecretVaultTestResult.Ok(
            secrets.Count == 0
                ? "Connected to BastionVault, but this token can see no secrets. Grant its policies "
                  + "read and list on the paths NetRisk should use." + policies + bound
                : $"Connected to BastionVault. This token can see {secrets.Count} secret(s)."
                  + policies + bound,
            secrets.Count);
    }

    /// <summary>
    /// Checks the token's machine binding against what the server demands and what the connection
    /// declares, returning a refusal message or null.
    ///
    /// Two independent things can be wrong, and they have different remedies, so they get different
    /// messages: the server may require a machine-bound token and this one is not, or the connection
    /// may name a machine that is not the one the token is bound to. The second matters because a
    /// machine-bound token is the credential of a specific host — silently accepting one issued for a
    /// different machine would make the NetRisk connection's machine ID decorative.
    /// </summary>
    private async Task<string?> CheckMachineIdentityAsync(SecretVaultContext context,
        BastionVaultApi.TokenInfo token, CancellationToken ct)
    {
        var declared = context.Credentials.MachineId?.Trim();

        if (!string.IsNullOrEmpty(declared)
            && token.MachineIdentity is { } actual
            && !string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase))
            return $"This connection declares machine '{declared}', but the token is bound to "
                   + $"'{actual}'. Mint the token on the NetRisk server with "
                   + "`bvault ferrogate token`, or correct the machine ID.";

        var requirement = await ReadMachineRequirementAsync(context, ct);

        if (requirement is not { RequireMachineIdentity: true }) return null;

        if (token.MachineIdentity is not null) return null;

        // The server will refuse every subsequent request, so saying so now — with the command that
        // produces a usable token — is the difference between a five-minute fix and an afternoon.
        var environment = string.IsNullOrWhiteSpace(requirement.MiaEnvironment)
            ? string.Empty
            : $" (MIA environment '{requirement.MiaEnvironment}')";

        return "This BastionVault server requires machine identity on every session, and the token "
               + "supplied is not machine-bound. On the NetRisk server, with a FerroGate Machine "
               + "Identity Agent running" + environment + ", mint one with "
               + $"`bvault ferrogate token --field client_token --audience {context.Credentials.BaseUrl}` "
               + "and use that as the API key.";
    }

    /// <summary>
    /// Reads the unauthenticated machine-identity requirement, or null when the question does not
    /// apply.
    ///
    /// A 404 is the ordinary answer on a server with no FerroGate auth method mounted, and any other
    /// failure is deliberately swallowed: this is advisory, and a connection test must not fail
    /// because an optional endpoint was unreachable.
    /// </summary>
    private async Task<BastionVaultApi.MachineRequirement?> ReadMachineRequirementAsync(
        SecretVaultContext context, CancellationToken ct)
    {
        try
        {
            var response = await SendAsync(context, "GET", BastionVaultApi.MachineRequirementPath, ct);

            if (!response.IsSuccess) return null;

            var envelope = BastionVaultApi.ParseEnvelope(response.Body);

            if (envelope?.Data is null) return null;

            return JsonSerializer.Deserialize<BastionVaultApi.MachineRequirement>(
                envelope.Data.Value.GetRawText(), BastionVaultApi.Json);
        }
        catch (Exception ex) when (ex is JsonException or SecretVaultException)
        {
            _logger?.Debug("BastionVault machine-identity requirement could not be read: {Message}",
                ex.Message);
            return null;
        }
    }

    // --- listing ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<VaultSecretDescriptor>> ListSecretsAsync(SecretVaultContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return await WalkAsync(context, ct);
    }

    /// <summary>
    /// Enumerates every secret the token can see, by walking each secrets-engine mount.
    ///
    /// A BastionVault listing is one level deep and marks folders with a trailing slash, so this is a
    /// breadth-first walk rather than a single call. A folder that answers 403 is skipped rather than
    /// failing the whole enumeration: a token scoped to <c>secret/netrisk/*</c> is a *good*
    /// configuration, and it will be denied on its siblings.
    /// </summary>
    private async Task<List<VaultSecretDescriptor>> WalkAsync(SecretVaultContext context,
        CancellationToken ct)
    {
        var mountsResponse = await SendAsync(context, "GET", BastionVaultApi.MountsPath, ct);

        List<string> mounts;

        if (mountsResponse.IsSuccess)
        {
            try
            {
                mounts = BastionVaultApi.ParseSecretMounts(BastionVaultApi.ParseEnvelope(mountsResponse.Body));
            }
            catch (JsonException ex)
            {
                throw new SecretVaultException(
                    "BastionVault returned a mount table that could not be read: " + ex.Message, ex);
            }
        }
        else if (mountsResponse.StatusCode is 403)
        {
            // Reading the mount table is a sys/ privilege NetRisk does not need and a careful operator
            // will not grant. Falling back to the conventional mount keeps the common configuration
            // — a token scoped to one KV path — working.
            _logger?.Information(
                "BastionVault denied sys/mounts; falling back to the conventional 'secret/' mount");
            mounts = ["secret/"];
        }
        else
        {
            throw new SecretVaultException(BastionVaultApi.DescribeFailure(
                mountsResponse.StatusCode, mountsResponse.Body, mountsResponse.TransportError));
        }

        var descriptors = new List<VaultSecretDescriptor>();
        var denied = 0;
        var truncated = false;

        foreach (var mount in mounts)
        {
            if (descriptors.Count >= MaxSecrets) { truncated = true; break; }

            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((mount, 0));

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var (folder, depth) = pending.Dequeue();

                if (descriptors.Count >= MaxSecrets) { truncated = true; break; }

                var response = await SendAsync(context, BastionVaultApi.ListMethod, folder, ct);

                // 403 is a scoped token doing its job; 404 is an empty or absent folder. Neither is a
                // reason to abandon the enumeration.
                if (response.StatusCode is 403) { denied++; continue; }
                if (response.StatusCode is 404) continue;

                if (!response.IsSuccess)
                    throw new SecretVaultException(BastionVaultApi.DescribeFailure(
                        response.StatusCode, response.Body, response.TransportError));

                List<string> keys;

                try
                {
                    keys = BastionVaultApi.ParseKeys(BastionVaultApi.ParseEnvelope(response.Body));
                }
                catch (JsonException ex)
                {
                    throw new SecretVaultException(
                        $"BastionVault returned a listing of '{folder}' that could not be read: "
                        + ex.Message, ex);
                }

                foreach (var key in keys)
                {
                    if (key.EndsWith('/'))
                    {
                        if (depth + 1 <= MaxDepth) pending.Enqueue((folder + key, depth + 1));
                        continue;
                    }

                    if (descriptors.Count >= MaxSecrets) { truncated = true; break; }

                    descriptors.Add(Describe(folder + key, mount));
                }
            }
        }

        if (denied > 0)
            _logger?.Information(
                "BastionVault denied listing on {Count} path(s); the token's policies do not cover them",
                denied);

        if (truncated)
            _logger?.Warning(
                "BastionVault listing stopped at {Max} secrets. Narrow the token's policies to the "
                + "paths NetRisk should use.", MaxSecrets);

        return descriptors;
    }

    /// <summary>
    /// Turns a full logical path into a descriptor.
    ///
    /// <see cref="VaultSecretDescriptor.Fields"/> is left empty, and that is a property of
    /// BastionVault rather than an omission: a listing returns names only, and the only way to learn a
    /// secret's field names is to read the secret — which would mean every click in the picker
    /// reading a credential and writing an access record in the vault's audit log. So the field is
    /// typed rather than chosen, and <see cref="GetSecretAsync"/> answers a wrong one by naming the
    /// fields that do exist.
    /// </summary>
    private static VaultSecretDescriptor Describe(string fullPath, string mount)
    {
        var lastSeparator = fullPath.LastIndexOf('/');

        var name = lastSeparator >= 0 ? fullPath[(lastSeparator + 1)..] : fullPath;
        var folder = lastSeparator >= 0 ? fullPath[..lastSeparator] : mount.TrimEnd('/');

        return new VaultSecretDescriptor
        {
            Id = fullPath,
            Name = name.Length > 0 ? name : fullPath,
            Path = folder.Length > 0 ? folder : null,
            Description = null,
            Fields = []
        };
    }

    // --- reading ----------------------------------------------------------------------------

    public async Task<VaultSecretValue> GetSecretAsync(SecretVaultContext context,
        VaultSecretReference reference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrWhiteSpace(reference.SecretId))
            throw new SecretVaultException("A BastionVault secret path is required.");

        // A path, not an opaque id — so a reference that ends in '/' would list rather than read, and
        // one containing '..' would reach somewhere else entirely.
        var path = reference.SecretId.Trim().Trim('/');

        if (path.Length == 0 || path.Split('/').Any(segment => segment is "." or ".."))
            throw new SecretVaultException(
                $"'{reference.SecretId}' is not a usable BastionVault secret path.");

        var response = await SendAsync(context, "GET", path, ct);

        if (!response.IsSuccess)
            throw new SecretVaultException(BastionVaultApi.DescribeFailure(
                response.StatusCode, response.Body, response.TransportError));

        BastionVaultApi.Envelope? envelope;

        try
        {
            envelope = BastionVaultApi.ParseEnvelope(response.Body);
        }
        catch (JsonException ex)
        {
            throw new SecretVaultException(
                $"BastionVault returned a secret at '{path}' that could not be read: " + ex.Message, ex);
        }

        var fields = BastionVaultApi.ParseFields(envelope);

        return new VaultSecretValue
        {
            Value = SelectValue(fields, path, reference.Field),
            Version = null,
            MaxCacheAge = envelope is { LeaseDuration: > 0 }
                ? TimeSpan.FromSeconds(envelope.LeaseDuration)
                : null
        };
    }

    /// <summary>
    /// Picks the requested field out of a secret.
    ///
    /// Every BastionVault secret is a map, so there is no "the value" to fall back to — except in the
    /// one unambiguous case where the map has exactly one entry, which is how a single-valued secret
    /// is conventionally stored and what makes a reference with no field usable at all.
    ///
    /// A requested field that is missing is an error, never a fallback: silently returning some other
    /// field would hand a username to something expecting a password, and nothing would report it
    /// until a third party rejected the credential.
    /// </summary>
    private static string SelectValue(Dictionary<string, string> fields, string path, string? field)
    {
        if (fields.Count == 0)
            throw new SecretVaultException($"BastionVault returned no data for secret '{path}'.");

        if (!string.IsNullOrWhiteSpace(field))
        {
            foreach (var candidate in fields)
                if (string.Equals(candidate.Key, field, StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate.Value.Length > 0) return candidate.Value;

                    throw new SecretVaultException(
                        $"Field '{field}' of BastionVault secret '{path}' is empty.");
                }

            throw new SecretVaultException(
                $"BastionVault secret '{path}' has no field '{field}'. Available fields: "
                + string.Join(", ", fields.Keys) + ". Re-select the secret on the field that uses it.");
        }

        if (fields.Count == 1)
        {
            var only = fields.First();
            if (only.Value.Length > 0) return only.Value;

            throw new SecretVaultException(
                $"BastionVault secret '{path}' holds a single empty field ('{only.Key}').");
        }

        throw new SecretVaultException(
            $"BastionVault secret '{path}' holds several fields ({string.Join(", ", fields.Keys)}) and "
            + "no field was selected. Re-select it and name the field to use.");
    }

    // --- transport --------------------------------------------------------------------------

    /// <summary>
    /// One request, with the token attached.
    ///
    /// Goes through <c>context.Http</c> and never through an <c>HttpClient</c> of its own: that is
    /// what subjects the base URL an operator typed to the host's SSRF policy, and what lets these
    /// tests run without a network.
    /// </summary>
    private static Task<PluginHttpResponse> SendAsync(SecretVaultContext context, string method,
        string logicalPath, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>
        {
            [BastionVaultApi.TokenHeader] = context.Credentials.ApiKey,
            ["Accept"] = "application/json"
        };

        return context.Http.SendAsync(new PluginHttpRequest
        {
            Method = method,
            Url = BastionVaultApi.Url(context.Credentials.BaseUrl, logicalPath),
            Headers = headers
        }, ct);
    }
}
