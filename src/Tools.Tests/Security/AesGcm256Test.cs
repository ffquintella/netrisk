using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Tools.Criptography;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-011: stored integration credentials were encrypted with AES-CBC under a
/// key of <c>SHA256(passphrase)</c> and an IV of <c>MD5(passphrase)</c> — a constant IV, and no
/// authentication. These tests pin the three properties the replacement has to have.
/// </summary>
[TestSubject(typeof(AesGcm256))]
public class AesGcm256Test
{
    private const string Passphrase = "an-installation-key-of-plenty-entropy";

    [Fact]
    public void RoundTrips()
    {
        var cipher = AesGcm256.Encrypt("https://hooks.slack.com/services/T0/B0/xyz", Passphrase);

        Assert.DoesNotContain("hooks.slack.com", cipher);
        Assert.Equal("https://hooks.slack.com/services/T0/B0/xyz", AesGcm256.Decrypt(cipher, Passphrase));
    }

    [Fact]
    public void RoundTripsTheEmptyStringAndNonAsciiText()
    {
        Assert.Equal("", AesGcm256.Decrypt(AesGcm256.Encrypt("", Passphrase), Passphrase));
        Assert.Equal("çãé—key", AesGcm256.Decrypt(AesGcm256.Encrypt("çãé—key", Passphrase), Passphrase));
    }

    /// <summary>
    /// The regression assertion for the constant-IV defect: the same plaintext encrypted twice must
    /// not produce the same ciphertext, or an observer of the database learns which stored
    /// credentials are equal. The old CBC helper failed this deterministically.
    /// </summary>
    [Fact]
    public void EncryptingTheSameValueTwiceProducesDifferentCiphertext()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 25; i++)
            Assert.True(seen.Add(AesGcm256.Encrypt("same-token", Passphrase)),
                "the same plaintext produced identical ciphertext — the nonce is not fresh");
    }

    /// <summary>The regression assertion for missing integrity: a flipped bit must be rejected.</summary>
    [Fact]
    public void TamperingWithTheCiphertextIsDetected()
    {
        var envelope = Convert.FromBase64String(AesGcm256.Encrypt("token", Passphrase));
        envelope[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(
            () => AesGcm256.Decrypt(Convert.ToBase64String(envelope), Passphrase));
    }

    [Fact]
    public void TamperingWithTheSaltIsDetected()
    {
        var envelope = Convert.FromBase64String(AesGcm256.Encrypt("token", Passphrase));
        // Byte 0 is inside the salt, so the derived key changes and the tag no longer verifies.
        envelope[0] ^= 0x01;

        Assert.Throws<AuthenticationTagMismatchException>(
            () => AesGcm256.Decrypt(Convert.ToBase64String(envelope), Passphrase));
    }

    [Fact]
    public void TheWrongPassphraseIsRejectedRatherThanReturningGarbage()
    {
        var cipher = AesGcm256.Encrypt("token", Passphrase);

        Assert.Throws<AuthenticationTagMismatchException>(
            () => AesGcm256.Decrypt(cipher, "a-different-installation-key"));
    }

    [Fact]
    public void MalformedInputIsACryptographicFailureNotACrash()
    {
        Assert.Throws<CryptographicException>(() => AesGcm256.Decrypt("not base64 at all!!", Passphrase));
        // Long enough to be base64, far too short to be salt+nonce+tag.
        Assert.Throws<CryptographicException>(() => AesGcm256.Decrypt(Convert.ToBase64String(new byte[8]), Passphrase));
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => AesGcm256.Encrypt(null!, Passphrase));
        Assert.Throws<ArgumentNullException>(() => AesGcm256.Encrypt("x", null!));
        Assert.Throws<ArgumentNullException>(() => AesGcm256.Decrypt(null!, Passphrase));
        Assert.Throws<ArgumentNullException>(() => AesGcm256.Decrypt("x", null!));
    }
}
