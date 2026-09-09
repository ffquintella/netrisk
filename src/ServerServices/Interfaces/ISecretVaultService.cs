using Model.Secrets;

namespace ServerServices.Interfaces;

/// <summary>
/// Vault connections, and the resolution of a stored <see cref="SecretReference"/> into a live
/// credential (the BastionVault integration and the secret-vault plugin capability).
///
/// The division of labour with <see cref="ISecretResolver"/> is worth stating, because both take a
/// string and return a secret. This service knows about vaults: connections, plugins, listing,
/// testing, and resolving a parsed reference. The resolver knows about *fields*: given whatever is
/// stored in a credential column, it decides whether that is a literal to decrypt or a reference to
/// resolve. Integration services depend on the resolver and never on this — which is what keeps them
/// from having to know that vaults exist at all.
/// </summary>
public interface ISecretVaultService
{
    /// <summary>Every configured connection. Disabled ones included by default, since the editor lists them.</summary>
    Task<List<SecretVaultConnectionView>> GetConnectionsAsync(bool includeDisabled = true);

    /// <summary>One connection.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such connection.</exception>
    Task<SecretVaultConnectionView> GetConnectionAsync(int id);

    /// <summary>
    /// The secret-vault plugins this installation can use: installed, enabled, and implementing the
    /// capability. Empty is the normal answer on an installation with no vault plugin, and the UI
    /// says so rather than offering an empty picker.
    /// </summary>
    Task<List<SecretVaultPluginInfo>> GetAvailablePluginsAsync();

    Task<SecretVaultConnectionView> CreateConnectionAsync(SecretVaultConnectionInput input, string? apiKey,
        int? userId = null);

    /// <summary>
    /// Updates a connection. A null <paramref name="apiKey"/> leaves the stored key untouched, so a
    /// form showing a redacted placeholder cannot overwrite a working credential with the
    /// placeholder.
    /// </summary>
    Task<SecretVaultConnectionView> UpdateConnectionAsync(SecretVaultConnectionInput input, string? apiKey);

    /// <summary>
    /// Deletes a connection.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">
    /// References to this connection still exist. Deleting it would turn every one of them into a
    /// credential that cannot be resolved — discovered at 3am by whichever sync ran first — so the
    /// delete is refused and the count reported instead.
    /// </exception>
    Task DeleteConnectionAsync(int id);

    /// <summary>Verifies the connection against the vault and records the outcome on the row.</summary>
    Task<SecretVaultTestResultView> TestConnectionAsync(int id);

    /// <summary>
    /// Metadata for every secret the connection's credential can read — what the picker shows.
    /// Never values.
    /// </summary>
    Task<List<VaultSecretSummary>> ListSecretsAsync(int connectionId);

    /// <summary>
    /// Resolves a reference to its secret value, from the obfuscated cache when it is warm and from
    /// the vault when it is not.
    /// </summary>
    /// <exception cref="Model.Exceptions.SecretVaultResolutionException">
    /// The connection is gone or disabled, its plugin is not installed or not enabled, or the vault
    /// refused. Never an empty string: an integration that authenticates with "" gets a 401 from a
    /// third party and sends its operator hunting the wrong problem.
    /// </exception>
    Task<string> ResolveAsync(SecretReference reference, CancellationToken ct = default);

    /// <summary>
    /// Describes what a stored value points at, for display beside a field. Returns
    /// <c>IsVaultReference = false</c> for a literal credential, and
    /// <c>Resolvable = false</c> when the reference no longer names a live connection.
    /// </summary>
    Task<SecretReferenceView> DescribeAsync(string? storedValue);

    /// <summary>
    /// How many credential fields currently point at this connection. The delete guard's input, and
    /// shown in the editor so an operator can see the blast radius before switching a vault off.
    /// </summary>
    Task<int> CountReferencesAsync(int connectionId);

    /// <summary>
    /// Whether the vault feature is usable at all: at least one enabled plugin and one enabled
    /// connection. The GUI asks this once to decide whether to render the picker button beside
    /// secret fields.
    /// </summary>
    Task<bool> IsAvailableAsync();
}
