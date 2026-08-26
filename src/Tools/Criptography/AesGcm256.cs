using System;
using System.Security.Cryptography;
using System.Text;

namespace Tools.Criptography;

/// <summary>
/// Authenticated symmetric encryption of short secrets — AES-256-GCM with an HKDF-derived key
/// (Track 7 milestone 7.4.2).
///
/// This exists because the older <see cref="AES"/> helper has three properties a security product
/// should not ship for stored credentials:
///  * the key is a bare <c>SHA256(passphrase)</c>, with no salt, so every stored value on an
///    installation is encrypted under the same key and one precomputation covers any installation
///    that happens to share a passphrase;
///  * the IV is <c>MD5(passphrase)</c>, i.e. constant for a given passphrase. Reusing an IV across
///    messages in CBC mode means two identical plaintexts produce identical ciphertexts, which
///    leaks equality of stored secrets, and it is the precondition for several classical CBC
///    attacks;
///  * CBC alone is unauthenticated, so a tampered ciphertext decrypts to attacker-influenced
///    plaintext (or to plausible garbage) rather than being rejected.
///
/// GCM fixes all three: a fresh 96-bit nonce per message, a 128-bit authentication tag that makes
/// "wrong key" and "tampered" both a clean failure, and a per-ciphertext salt fed through HKDF so
/// that no two stored values share an encryption key.
///
/// Wire format, all concatenated then base64: <c>salt(16) ‖ nonce(12) ‖ tag(16) ‖ ciphertext</c>.
/// </summary>
public static class AesGcm256
{
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;   // 96 bits — the size GCM is specified for.
    private const int TagBytes = 16;     // 128 bits — the full tag; truncating it buys nothing here.
    private const int KeyBytes = 32;     // AES-256.

    /// <summary>
    /// HKDF info string. It pins the derived key to this format and version, so a key derived here
    /// can never coincide with one derived for another purpose from the same installation secret.
    /// </summary>
    private const string KdfInfo = "netrisk.aes-gcm-256.v1";

    /// <summary>Encrypts <paramref name="plaintext"/> under <paramref name="passphrase"/>.</summary>
    public static string Encrypt(string plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(passphrase);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var key = DeriveKey(passphrase, salt);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagBytes];

        using (var gcm = new AesGcm(key, TagBytes))
        {
            gcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        CryptographicOperations.ZeroMemory(key);

        var envelope = new byte[SaltBytes + NonceBytes + TagBytes + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, envelope, 0, SaltBytes);
        Buffer.BlockCopy(nonce, 0, envelope, SaltBytes, NonceBytes);
        Buffer.BlockCopy(tag, 0, envelope, SaltBytes + NonceBytes, TagBytes);
        Buffer.BlockCopy(cipherBytes, 0, envelope, SaltBytes + NonceBytes + TagBytes, cipherBytes.Length);

        return Convert.ToBase64String(envelope);
    }

    /// <summary>
    /// Decrypts a value produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <exception cref="CryptographicException">
    /// The envelope is malformed, or the key is wrong, or the ciphertext was altered. The three are
    /// deliberately one outcome: distinguishing them is what a padding oracle is.
    /// </exception>
    public static string Decrypt(string envelopeBase64, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(envelopeBase64);
        ArgumentNullException.ThrowIfNull(passphrase);

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(envelopeBase64);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The ciphertext is not valid base64.", ex);
        }

        if (envelope.Length < SaltBytes + NonceBytes + TagBytes)
            throw new CryptographicException("The ciphertext is too short to be a GCM envelope.");

        var salt = new byte[SaltBytes];
        var nonce = new byte[NonceBytes];
        var tag = new byte[TagBytes];
        var cipherBytes = new byte[envelope.Length - SaltBytes - NonceBytes - TagBytes];

        Buffer.BlockCopy(envelope, 0, salt, 0, SaltBytes);
        Buffer.BlockCopy(envelope, SaltBytes, nonce, 0, NonceBytes);
        Buffer.BlockCopy(envelope, SaltBytes + NonceBytes, tag, 0, TagBytes);
        Buffer.BlockCopy(envelope, SaltBytes + NonceBytes + TagBytes, cipherBytes, 0, cipherBytes.Length);

        var key = DeriveKey(passphrase, salt);
        var plainBytes = new byte[cipherBytes.Length];

        try
        {
            using var gcm = new AesGcm(key, TagBytes);
            gcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// HKDF-SHA256 rather than PBKDF2.
    ///
    /// PBKDF2 is a *password* KDF: its iteration count exists to slow down guessing a low-entropy
    /// human secret. The passphrase reaching this helper is not that — it is a 256-bit
    /// per-installation key — so iterations would buy nothing while adding hundreds of milliseconds
    /// to every notification dispatch, which decrypts a stored credential. HKDF is the correct
    /// primitive for high-entropy input keying material: it extracts with the per-ciphertext salt
    /// and expands with a fixed info label, giving a distinct key per stored value at no cost.
    /// </summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(passphrase),
            KeyBytes,
            salt,
            Encoding.UTF8.GetBytes(KdfInfo));
}
