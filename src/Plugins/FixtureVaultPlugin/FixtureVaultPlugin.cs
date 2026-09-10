using Contracts.Secrets;
using Serilog;

namespace FixtureVaultPlugin;

/// <summary>
/// A secret-vault plugin with no vault behind it, for <c>ServerServices.Tests</c> to load off disk.
///
/// Only the parts the host inspects during discovery are real — the name, version, description,
/// <see cref="VaultKind"/> and <see cref="RequiresMachineId"/>. The three vault operations answer a
/// fixed, obviously-fake value, because nothing that loads this is testing a vault; the tests that
/// exercise vault behaviour use a substitute, and the tests that exercise a real protocol live in
/// that plugin's own repository.
///
/// It makes no outbound call of any kind, so a test host that loads it cannot reach a network even
/// by accident.
/// </summary>
public class FixtureVaultPlugin : INetriskSecretVaultPlugin
{
    /// <summary>The single secret this plugin pretends to hold.</summary>
    private const string SecretId = "fixture/secret";

    public string PluginName => "FixtureVaultPlugin";

    public string PluginVersion => "1.0.0";

    public string PluginDescription =>
        "Test fixture: a secret vault plugin with no vault. Not a feature — it exists so the plugin "
        + "loader can be tested against a real assembly.";

    public string VaultKind => "fixture";

    public bool RequiresMachineId => false;

    public void Initialize(ILogger? logger) { }

    public void Dispose() => GC.SuppressFinalize(this);

    public Task<SecretVaultTestResult> TestConnectionAsync(SecretVaultContext context,
        CancellationToken ct = default) =>
        Task.FromResult(SecretVaultTestResult.Ok("Fixture vault: nothing to connect to.", 1));

    public Task<IReadOnlyList<VaultSecretDescriptor>> ListSecretsAsync(SecretVaultContext context,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VaultSecretDescriptor>>([
            new VaultSecretDescriptor
            {
                Id = SecretId,
                Name = "secret",
                Path = "fixture",
                Fields = ["value"]
            }
        ]);

    public Task<VaultSecretValue> GetSecretAsync(SecretVaultContext context,
        VaultSecretReference reference, CancellationToken ct = default)
    {
        if (reference.SecretId != SecretId)
            throw new SecretVaultException($"The fixture vault holds no secret '{reference.SecretId}'.");

        return Task.FromResult(new VaultSecretValue { Value = "fixture-value" });
    }
}
