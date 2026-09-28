using System.Security.Cryptography;
using System.Text;
using Model.Exceptions;
using Model.Secrets;
using Serilog;
using ServerServices.Interfaces;
using Tools.Criptography;

namespace ServerServices.Security;

/// <summary>
/// AES encryption of integration credentials, keyed off the installation's master key (Track 4).
///
/// <para>
/// The key comes from <see cref="IMasterKeyProvider"/>, which holds it in the most protected place
/// the host offers — a TPM, the macOS keychain, DPAPI, or failing those an owner-only file. It used
/// to be derived from <see cref="IEnvironmentService.ServerSecretToken"/>, the JWT signing key.
/// Sharing one value was wrong in both directions: rotating or losing the signing key made every
/// stored credential unreadable, and a key that only has to sign a token has no reason to live in
/// hardware while a key that decrypts credentials does.
/// </para>
/// <para>
/// The old derivation stays as a <i>decrypt-only</i> fallback, so an installation that upgrades
/// keeps reading credentials it encrypted before the master key existed; any save re-encrypts them
/// under the new key. Removing that fallback is what makes the upgrade destructive, so it stays
/// until a release that can state every install has re-saved.
/// </para>
/// <para>
/// Trying two candidate keys on read is safe rather than a guess: the v2 format is AES-GCM, so the
/// authentication tag makes "wrong key" a clean failure rather than plausible-looking plaintext.
/// </para>
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

    /// <summary>The key everything is written under.</summary>
    private readonly string _passphrase;

    /// <summary>
    /// Superseded keys, tried on read only, in order. Lazy because resolving the legacy one reads —
    /// and on a fresh install creates — the JWT key file, which a process that never decrypts an
    /// old credential has no reason to touch.
    /// </summary>
    private readonly Lazy<IReadOnlyList<string>> _fallbackPassphrases;

    public SecretProtector(ILogger logger, IMasterKeyProvider masterKeyProvider, IEnvironmentService environmentService)
    {
        _logger = logger;
        _passphrase = DerivePassphrase(masterKeyProvider.GetMasterKey());
        _fallbackPassphrases = new Lazy<IReadOnlyList<string>>(
            () => [DerivePassphrase(environmentService.ServerSecretToken)],
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Test seam: construct over an explicit root secret instead of the install's key store.</summary>
    internal SecretProtector(ILogger logger, string rootSecret)
    {
        _logger = logger;
        _passphrase = DerivePassphrase(rootSecret);
        _fallbackPassphrases = new Lazy<IReadOnlyList<string>>(() => []);
    }

    /// <summary>Test seam: an explicit current key plus explicit superseded ones.</summary>
    internal SecretProtector(ILogger logger, string rootSecret, params string[] supersededRootSecrets)
    {
        _logger = logger;
        _passphrase = DerivePassphrase(rootSecret);
        _fallbackPassphrases = new Lazy<IReadOnlyList<string>>(
            () => supersededRootSecrets.Select(DerivePassphrase).ToList());
    }

    private static string DerivePassphrase(string rootSecret)
    {
        var material = Encoding.UTF8.GetBytes(KeyLabel + "|" + rootSecret);
        return Convert.ToBase64String(SHA256.HashData(material));
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        // A vault reference is not a secret and is stored in the clear, deliberately. Encrypting it
        // would cost nothing in security — it names a secret, it is not one — and would cost two
        // things that matter: the reference would no longer be visible in a database dump as
        // "this column holds a pointer, not a credential", and "which fields point at vault
        // connection 3" would stop being a query, which is what makes refusing to delete a
        // connection in use possible at all. See Model.Secrets.SecretReference.
        if (SecretReference.IsReference(plaintext)) return plaintext;

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
        var body = legacy[LegacyPrefix.Length..];

        foreach (var candidate in Candidates())
        {
            try
            {
                var recovered = AES.Decrypt(body, candidate);

                if (!string.Equals(AES.Encrypt(recovered, candidate), body, StringComparison.Ordinal))
                    continue;

                return Prefix + AesGcm256.Encrypt(recovered, _passphrase);
            }
            catch (Exception)
            {
                // Try the next candidate key; a v1 value written before the master key existed is
                // readable only under the superseded derivation.
            }
        }

        _logger.Warning(
            "A stored credential is in the superseded enc:v1 format but does not decrypt with any of "
            + "this installation's keys; leaving it untouched");
        return legacy;
    }

    /// <summary>The current key first, then any superseded one, for read paths that may meet either.</summary>
    private IEnumerable<string> Candidates()
    {
        yield return _passphrase;
        foreach (var fallback in _fallbackPassphrases.Value) yield return fallback;
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;

        // Handed back untouched, and before the unencrypted-value warning below: a reference is
        // stored in the clear on purpose, so warning about it would be advice to do the wrong
        // thing — on every read, for every vault-backed field. Turning the reference into a live
        // credential is ISecretResolver's job, not this class's; a caller that has not been moved
        // over yet receives the reference and fails visibly rather than authenticating with an
        // empty string.
        if (SecretReference.IsReference(ciphertext)) return ciphertext;

        if (!LooksProtected(ciphertext))
        {
            // A pre-encryption row, or a fixture. Returning it as-is is what lets an upgrade read
            // existing connections; the warning is what stops it from being permanent.
            _logger.Warning("An integration credential is stored unencrypted; re-save the connection to protect it");
            return ciphertext;
        }

        var isLegacyFormat = ciphertext.StartsWith(LegacyPrefix, StringComparison.Ordinal);
        var body = ciphertext[(isLegacyFormat ? LegacyPrefix.Length : Prefix.Length)..];
        Exception? firstFailure = null;
        var underCurrentKey = true;

        foreach (var candidate in Candidates())
        {
            try
            {
                string plaintext;

                if (isLegacyFormat)
                {
                    // v1 is unauthenticated CBC: the wrong key does not fail, it returns garbage.
                    // With more than one candidate key that is no longer a theoretical problem —
                    // the current key would "succeed" on a value written under the superseded one
                    // and hand a caller rubbish to authenticate with. v1 is deterministic, so
                    // re-encrypting and comparing is an exact check, and it is the same one
                    // UpgradeLegacy uses.
                    plaintext = AES.Decrypt(body, candidate);
                    if (!string.Equals(AES.Encrypt(plaintext, candidate), body, StringComparison.Ordinal))
                        throw new SecretProtectionException("the enc:v1 value does not round-trip under this key");
                }
                else
                {
                    plaintext = AesGcm256.Decrypt(body, candidate);
                }

                if (!underCurrentKey)
                    _logger.Information(
                        "A stored credential decrypted under a superseded installation key; re-save the "
                        + "connection to re-encrypt it under the current one");

                return plaintext;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                underCurrentKey = false;
            }
        }

        throw new SecretProtectionException(
            "A stored integration credential could not be decrypted with this installation's key. "
            + "It was most likely encrypted on another installation; re-enter it on the connection.",
            firstFailure!);
    }

    public bool LooksProtected(string? value) =>
        value != null && (value.StartsWith(Prefix, StringComparison.Ordinal)
                          || value.StartsWith(LegacyPrefix, StringComparison.Ordinal));
}
