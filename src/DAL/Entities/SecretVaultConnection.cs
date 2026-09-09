namespace DAL.Entities;

/// <summary>
/// A connection to an external secret vault, serviced by a secret-vault plugin.
///
/// One row is one (vault, credential) pair, not one vault product: an installation may hold a
/// production vault and a staging one, and the whole point of a stored reference is that it names the
/// connection that resolves it, so the two cannot be conflated.
///
/// The API key here is the *only* credential this table holds. Everything else in the product that
/// used to hold a credential holds a <see cref="Model.Secrets.SecretReference"/> pointing through
/// this row instead — which is why deleting one is refused while references to it exist rather than
/// cascading.
/// </summary>
public class SecretVaultConnection
{
    public int Id { get; set; }

    /// <summary>Operator-facing label, unique so a reference's connection is identifiable in the UI.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The plugin assembly that services this connection (<c>INetriskPlugin.PluginName</c>, e.g.
    /// <c>BastionVaultPlugin</c>). Stored rather than derived: an installation may have two vault
    /// plugins loaded, and picking "whichever was found first" is how a connection silently starts
    /// talking to the wrong vault.
    /// </summary>
    public string PluginName { get; set; } = null!;

    /// <summary>The vault's API root. Subject to the host's outbound URL policy on every call.</summary>
    public string BaseUrl { get; set; } = null!;

    /// <summary>The vault API key, encrypted with <c>ISecretProtector</c>.</summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>
    /// The machine identity the vault issued for the host NetRisk is installed on, when the vault
    /// binds keys to a machine. Optional — a vault without machine binding leaves it null, and the
    /// plugin declares whether it is required.
    ///
    /// Not encrypted: it identifies the installation, it is useless without the API key, and an
    /// operator has to be able to read it back to compare it against what the vault shows.
    /// </summary>
    public string? MachineId { get; set; }

    /// <summary>A disabled connection resolves nothing. References to it fail loudly rather than silently.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a value resolved through this connection stays in the obfuscated in-memory cache.
    /// Per connection because a staging vault and a production vault can reasonably differ; clamped
    /// by <c>Model.Secrets.SecretVaultDefaults</c> on read, so a nonsense value in the row cannot
    /// turn the cache into a permanent second copy of the credential.
    /// </summary>
    public int CacheTtlMinutes { get; set; } = 15;

    public DateTime? LastTestAt { get; set; }

    public bool? LastTestSucceeded { get; set; }

    /// <summary>The last test's message. Never contains a credential — the plugin contract forbids it.</summary>
    public string? LastTestMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual User? CreatedBy { get; set; }
}
