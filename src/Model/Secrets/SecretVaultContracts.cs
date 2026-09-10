namespace Model.Secrets;

/// <summary>
/// A vault connection as a client sees it. The API key is never returned — only whether one is set,
/// which is the same rule every other integration credential follows.
/// </summary>
public class SecretVaultConnectionView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The plugin that services this connection, e.g. <c>BastionVaultPlugin</c>.</summary>
    public string PluginName { get; set; } = string.Empty;

    /// <summary>The vault protocol the plugin reported, e.g. <c>bastionvault</c>. Empty when the plugin is absent.</summary>
    public string VaultKind { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public bool HasApiKey { get; set; }

    /// <summary>
    /// The machine identity, returned in the clear. It is an installation identifier, not a
    /// credential — it is useless without the API key, and hiding it would make "which machine is
    /// this connection bound to" unanswerable from the UI, which is the question an operator asks
    /// when the vault starts refusing.
    /// </summary>
    public string? MachineId { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Cache lifetime for values resolved through this connection, in minutes.</summary>
    public int CacheTtlMinutes { get; set; }

    public DateTime? LastTestAt { get; set; }

    public bool? LastTestSucceeded { get; set; }

    public string? LastTestMessage { get; set; }

    /// <summary>
    /// Whether the plugin this connection names is installed *and* enabled right now. False makes the
    /// UI say why nothing resolves, instead of leaving an operator to guess.
    /// </summary>
    public bool PluginAvailable { get; set; }

    /// <summary>Whether the plugin requires <see cref="MachineId"/>. Drives the required marker in the UI.</summary>
    public bool RequiresMachineId { get; set; }
}

/// <summary>
/// Create/update payload. The API key travels in its own property, and null means "leave the stored
/// one alone" — the same convention as the Track 4 connection requests, so a form that shows a
/// redacted placeholder cannot overwrite a working credential with the placeholder.
/// </summary>
public class SecretVaultConnectionRequest
{
    public SecretVaultConnectionInput Connection { get; set; } = new();

    /// <summary>The vault API key. Null on update leaves the stored key unchanged.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>The editable fields of a vault connection.</summary>
public class SecretVaultConnectionInput
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PluginName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional machine identity issued by the vault for this host.</summary>
    public string? MachineId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache lifetime in minutes. Defaults to the product default of 15; the resolver clamps it, so a
    /// value out of range is corrected rather than refused.
    /// </summary>
    public int CacheTtlMinutes { get; set; } = SecretVaultDefaults.CacheTtlMinutes;
}

/// <summary>One secret an operator can pick, as the API returns it. Metadata only — no value.</summary>
public class VaultSecretSummary
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? Description { get; set; }

    /// <summary>Named fields, for a structured secret. Empty for a single-value secret.</summary>
    public List<string> Fields { get; set; } = new();

    public string? Version { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Folder plus name, for the picker's single-line display.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Path) ? Name : Path + " / " + Name;
}

/// <summary>The outcome of testing a vault connection.</summary>
public class SecretVaultTestResultView
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? VisibleSecretCount { get; set; }

    /// <summary>
    /// The node the test actually talked to, for a connection whose address is a cluster name. Empty
    /// for a direct address, where it would only repeat what the operator typed.
    ///
    /// Reported because "the connection works" and "the connection works against the one node of
    /// three that is up" are different answers, and an operator who cannot see which node answered
    /// cannot tell a healthy cluster from a cluster that is one outage from silence.
    /// </summary>
    public string ResolvedEndpoint { get; set; } = string.Empty;

    /// <summary>How discovery chose that node, when there was anything worth reporting. Null otherwise.</summary>
    public string? EndpointNote { get; set; }
}

/// <summary>
/// What a stored reference points at, resolved for display. Returned so a form can show
/// "db-prod / password" beside a field instead of the raw <c>vault:v1:…</c> string.
/// </summary>
public class SecretReferenceView
{
    /// <summary>Whether the field currently holds a vault reference at all.</summary>
    public bool IsVaultReference { get; set; }

    public int ConnectionId { get; set; }

    public string ConnectionName { get; set; } = string.Empty;

    public string SecretId { get; set; } = string.Empty;

    public string? Field { get; set; }

    /// <summary>Human-readable label, or an explanation when the reference no longer resolves.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>False when the connection was deleted or the secret is gone from the vault.</summary>
    public bool Resolvable { get; set; }
}

/// <summary>Product-wide defaults for vault behaviour, in one place so the tiers cannot disagree.</summary>
public static class SecretVaultDefaults
{
    /// <summary>
    /// How long a resolved secret stays in the obfuscated cache. Fifteen minutes: long enough that a
    /// sync job making dozens of calls hits the vault once, short enough that a rotation or a
    /// revocation takes effect inside a coffee break.
    /// </summary>
    public const int CacheTtlMinutes = 15;

    /// <summary>Lower bound. Zero would mean a vault round trip per HTTP request an integration makes.</summary>
    public const int MinCacheTtlMinutes = 1;

    /// <summary>
    /// Upper bound. An hour, because past that the cache stops being a cache and becomes a second copy
    /// of the credential — which is the thing this feature exists to remove.
    /// </summary>
    public const int MaxCacheTtlMinutes = 60;

    /// <summary>The <c>Plugins</c> subdirectory secret-vault plugins are installed into.</summary>
    public const string PluginDirectory = "Secrets";

    /// <summary>
    /// The DNS SRV label prepended to a bare cluster name, so that <c>vault.example.com</c> is
    /// looked up as <c>_bvault._tcp.vault.example.com</c>.
    ///
    /// BastionVault's own convention, matched deliberately: an operator who has already put a bare
    /// cluster name in <c>bastionvault::client::server_url</c> should be able to paste the same
    /// string here and get the same nodes. A vault whose SRV label is different is still reachable —
    /// the operator writes the owner name out in full with the <c>srv+https://</c> form, which
    /// bypasses this default entirely.
    /// </summary>
    public const string SrvServiceLabel = "_bvault._tcp";

    /// <summary>
    /// The <c>srv+</c> scheme prefix that marks a base URL as a discovery name rather than a node
    /// address. Chosen over a boolean column because it keeps the whole address in one field: an
    /// operator can see what a connection points at without cross-referencing a flag.
    /// </summary>
    public const string SrvSchemePrefix = "srv+";

    /// <summary>
    /// The path probed on each discovered node to decide which one to talk to.
    ///
    /// HashiCorp-Vault-compatible, which is what BastionVault is. It is a host-side constant rather
    /// than something the plugin declares because <c>INetriskSecretVaultPlugin</c> has no health
    /// hook, and adding one would break every plugin already built against the SDK. A vault that
    /// does not serve it simply fails every probe, and the resolver then falls back to SRV order —
    /// which is the behaviour of no discovery scoring at all, not a broken connection.
    /// </summary>
    public const string HealthProbePath = "/sys/health";

    /// <summary>Lower and upper bounds on how long a discovered endpoint choice is reused.</summary>
    public const int MinEndpointCacheSeconds = 5;

    /// <summary>
    /// Upper bound on the endpoint cache. Five minutes: long enough that a busy resolver is not
    /// doing a DNS lookup and three health probes per secret, short enough that a node taken out of
    /// the cluster stops being used without an operator restarting anything.
    /// </summary>
    public const int MaxEndpointCacheSeconds = 300;
}

/// <summary>
/// A secret-vault plugin this installation could point a connection at: installed, enabled and
/// implementing the capability.
///
/// Offered as a list so the connection editor is a picker rather than a text box. A plugin name typed
/// by hand is how a connection ends up naming an assembly that is not there, which fails at
/// resolution time — in the middle of a sync — instead of at save time.
/// </summary>
public class SecretVaultPluginInfo
{
    /// <summary>The plugin assembly's own name; what a connection stores.</summary>
    public string PluginName { get; set; } = string.Empty;

    /// <summary>The vault protocol, e.g. <c>bastionvault</c>.</summary>
    public string VaultKind { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Whether this vault binds credentials to a machine identity and so needs one.</summary>
    public bool RequiresMachineId { get; set; }
}

/// <summary>
/// The body of a describe request: whatever is stored in a credential field.
///
/// A wrapper object rather than a bare string because the value can be a vault reference, and a
/// reference belongs in a request body — not in a URL that ends up in an access log.
/// </summary>
public class SecretReferenceDescribeRequest
{
    public string? Value { get; set; }
}
