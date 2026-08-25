namespace ServerServices.Interfaces;

/// <summary>
/// Encrypts and decrypts the credentials an integration connection carries — Slack webhook URLs,
/// Jira PATs, Vision One API keys, OIDC client secrets (Track 4).
///
/// Exists as an interface for two reasons. It keeps every integration service from re-deriving a key
/// (four slightly different derivations is four slightly different bugs), and it lets tests exercise
/// the connection services without a per-install key file on disk.
///
/// The threat model is a stolen database, not a compromised host: the key lives in the server's
/// application-data folder, so an attacker with the machine has both. That is the same trade the JWT
/// signing key already makes, and it is the difference between a leaked dump being a working Slack
/// credential and being ciphertext.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for storage. Returns null for null or empty input, so a
    /// caller can pass an unset optional secret straight through.
    /// </summary>
    string? Protect(string? plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Returns null for null or empty input.
    ///
    /// Throws <see cref="Model.Exceptions.SecretProtectionException"/> when the ciphertext cannot be
    /// decrypted — a key that has been rotated or a value copied between installations. Deliberately
    /// not a silent null: a connection that quietly authenticates with an empty token produces a 401
    /// from the provider and an operator hunting the wrong problem.
    /// </summary>
    string? Unprotect(string? ciphertext);

    /// <summary>
    /// Whether <paramref name="value"/> looks like something this protector produced. Used when
    /// reading a row that may predate encryption, so an upgrade does not have to rewrite every
    /// connection before the first read succeeds.
    /// </summary>
    bool LooksProtected(string? value);
}
