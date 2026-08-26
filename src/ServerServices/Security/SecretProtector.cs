using System.Security.Cryptography;
using System.Text;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Tools.Criptography;

namespace ServerServices.Security;

/// <summary>
/// AES encryption of integration credentials keyed off the per-installation server secret
/// (Track 4).
///
/// The key is derived from <see cref="IEnvironmentService.ServerSecretToken"/> with a fixed label
/// rather than being the token itself. That costs nothing and means a value stolen from
/// <c>encrypted_api_key</c> is not decryptable by anything that happens to know the JWT signing key,
/// and vice versa.
/// </summary>
public class SecretProtector : ISecretProtector
{
    /// <summary>
    /// Marks a value as produced by this protector. Without it there is no way to tell ciphertext
    /// from a plaintext token that happens to be valid base64 — and a webhook URL pasted into a row
    /// before encryption existed is exactly that case.
    /// </summary>
    internal const string Prefix = "enc:v2:";

    /// <summary>
    /// The original marker: AES-256-CBC with the key and IV both derived from the passphrase alone
    /// (Track 4). Track 7 finding NR-2026-011 replaced it — a constant IV made two identical stored
    /// credentials produce identical ciphertext, and CBC on its own cannot tell a tampered value
    /// from a valid one. Values already in the database still carry this prefix, so
    /// <see cref="Unprotect"/> keeps reading it; nothing writes it any more, and any save re-encrypts
    /// under <see cref="Prefix"/>.
    /// </summary>
    internal const string LegacyPrefix = "enc:v1:";

    /// <summary>Domain separation label. Changing it invalidates every stored secret, so it does not change.</summary>
    private const string KeyLabel = "netrisk.integrations.secret.v1";

    private readonly ILogger _logger;
    private readonly string _passphrase;

    public SecretProtector(ILogger logger, IEnvironmentService environmentService)
    {
        _logger = logger;
        _passphrase = DerivePassphrase(environmentService.ServerSecretToken);
    }

    /// <summary>Test seam: construct over an explicit root secret instead of the install's key file.</summary>
    internal SecretProtector(ILogger logger, string rootSecret)
    {
        _logger = logger;
        _passphrase = DerivePassphrase(rootSecret);
    }

    private static string DerivePassphrase(string rootSecret)
    {
        var material = Encoding.UTF8.GetBytes(KeyLabel + "|" + rootSecret);
        return Convert.ToBase64String(SHA256.HashData(material));
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        // Already protected: re-encrypting on every save would work, but it would also mean an
        // update that does not touch the token has to decrypt it first, and a form that round-trips
        // the redacted placeholder would encrypt the placeholder. A legacy v1 value is upgraded in
        // place, though — that is what eventually retires the weak format without an offline
        // migration step.
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;

        if (plaintext.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            return UpgradeLegacy(plaintext);

        return Prefix + AesGcm256.Encrypt(plaintext, _passphrase);
    }

    /// <summary>
    /// Re-encrypts a v1 value as v2, so that saving a connection retires the weak format without an
    /// offline migration.
    ///
    /// The round-trip check is the important part. v1 is unauthenticated CBC, so decrypting with the
    /// wrong key does not reliably fail — it can return plausible-looking garbage. Re-encrypting the
    /// result and comparing catches that, because v1 is deterministic: identical input under the same
    /// passphrase always produces byte-identical ciphertext. If the check fails the value is left
    /// exactly as it was, so a credential encrypted on another installation stays recoverable there
    /// instead of being overwritten with rubbish here.
    /// </summary>
    private string UpgradeLegacy(string legacy)
    {
        try
        {
            var body = legacy[LegacyPrefix.Length..];
            var recovered = AES.Decrypt(body, _passphrase);

            if (!string.Equals(AES.Encrypt(recovered, _passphrase), body, StringComparison.Ordinal))
            {
                _logger.Warning(
                    "A stored credential is in the superseded enc:v1 format but does not decrypt with "
                    + "this installation's key; leaving it untouched");
                return legacy;
            }

            return Prefix + AesGcm256.Encrypt(recovered, _passphrase);
        }
        catch (Exception)
        {
            return legacy;
        }
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;

        if (!LooksProtected(ciphertext))
        {
            // A pre-encryption row, or a fixture. Returning it as-is is what lets an upgrade read
            // existing connections; the warning is what stops it from being permanent.
            _logger.Warning("An integration credential is stored unencrypted; re-save the connection to protect it");
            return ciphertext;
        }

        try
        {
            return ciphertext.StartsWith(LegacyPrefix, StringComparison.Ordinal)
                ? AES.Decrypt(ciphertext[LegacyPrefix.Length..], _passphrase)
                : AesGcm256.Decrypt(ciphertext[Prefix.Length..], _passphrase);
        }
        catch (Exception ex)
        {
            throw new SecretProtectionException(
                "A stored integration credential could not be decrypted with this installation's key. "
                + "It was most likely encrypted on another installation; re-enter it on the connection.", ex);
        }
    }

    public bool LooksProtected(string? value) =>
        value != null && (value.StartsWith(Prefix, StringComparison.Ordinal)
                          || value.StartsWith(LegacyPrefix, StringComparison.Ordinal));
}
