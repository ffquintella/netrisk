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
    internal const string Prefix = "enc:v1:";

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
        // the redacted placeholder would encrypt the placeholder.
        if (LooksProtected(plaintext)) return plaintext;

        return Prefix + AES.Encrypt(plaintext, _passphrase);
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
            return AES.Decrypt(ciphertext[Prefix.Length..], _passphrase);
        }
        catch (Exception ex)
        {
            throw new SecretProtectionException(
                "A stored integration credential could not be decrypted with this installation's key. "
                + "It was most likely encrypted on another installation; re-enter it on the connection.", ex);
        }
    }

    public bool LooksProtected(string? value) =>
        value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
}
