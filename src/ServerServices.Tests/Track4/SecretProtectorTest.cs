using System;
using JetBrains.Annotations;
using Model.Exceptions;
using Serilog;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Encryption of integration credentials (Track 4).
///
/// The two properties that matter: a stored value is not the plaintext token, and a value encrypted on
/// one installation cannot be silently read as an empty credential on another — the second is what
/// turns a restore-to-a-new-host into a clear "re-enter the token" rather than a puzzling 401 from the
/// provider.
/// </summary>
[TestSubject(typeof(SecretProtector))]
public class SecretProtectorTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static SecretProtector Protector(string rootSecret = "root-a") => new(Log, rootSecret);

    [Fact]
    public void ProtectThenUnprotectRoundTrips()
    {
        var protector = Protector();

        var ciphertext = protector.Protect("https://hooks.slack.com/services/T0/B0/xyz");

        Assert.NotNull(ciphertext);
        Assert.DoesNotContain("hooks.slack.com", ciphertext);
        Assert.Equal("https://hooks.slack.com/services/T0/B0/xyz", protector.Unprotect(ciphertext));
    }

    [Fact]
    public void ProtectedValuesAreTagged()
    {
        var protector = Protector();

        var ciphertext = protector.Protect("token");

        Assert.True(protector.LooksProtected(ciphertext));
        Assert.False(protector.LooksProtected("token"));
        Assert.False(protector.LooksProtected(null));
    }

    [Fact]
    public void ProtectIsIdempotent()
    {
        var protector = Protector();

        var once = protector.Protect("token");

        // Re-encrypting on every save would mean an update that does not touch the token has to decrypt
        // it first, and a form that round-trips a redacted placeholder would encrypt the placeholder.
        Assert.Equal(once, protector.Protect(once));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyInputIsNull(string? input)
    {
        var protector = Protector();

        Assert.Null(protector.Protect(input));
        Assert.Null(protector.Unprotect(input));
    }

    [Fact]
    public void AnUnprotectedStoredValueIsReadThroughUnchanged()
    {
        var protector = Protector();

        // What lets an upgrade read connections written before encryption existed. The service logs a
        // warning; returning null instead would make every pre-upgrade connection fail with an empty
        // credential.
        Assert.Equal("plain-token", protector.Unprotect("plain-token"));
    }

    [Fact]
    public void AValueFromAnotherInstallationIsRefusedWithAnActionableError()
    {
        var ciphertext = Protector("root-a").Protect("token");

        var thrown = Assert.Throws<SecretProtectionException>(() => Protector("root-b").Unprotect(ciphertext));

        // The message has to name the remedy: the value has to be re-entered, not repaired.
        Assert.Contains("re-enter", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentRootSecretsProduceDifferentCiphertext()
    {
        Assert.NotEqual(Protector("root-a").Protect("token"), Protector("root-b").Protect("token"));
    }

    // ---- Track 7 milestone 7.4.2: the format moved from unauthenticated CBC to AES-GCM ----

    /// <summary>
    /// Finding NR-2026-011. The old format keyed AES-CBC on <c>SHA256(passphrase)</c> with an IV of
    /// <c>MD5(passphrase)</c>, so the IV never varied and the same credential always encrypted to the
    /// same bytes. Anyone with read access to the table could therefore tell which connections shared
    /// a token without decrypting anything.
    /// </summary>
    [Fact]
    public void ProtectingTheSameSecretTwiceProducesDifferentCiphertext()
    {
        var protector = Protector();

        var first = protector.Protect("shared-token");
        var second = protector.Protect("shared-token");

        Assert.NotEqual(first, second);
        Assert.Equal("shared-token", protector.Unprotect(first));
        Assert.Equal("shared-token", protector.Unprotect(second));
    }

    [Fact]
    public void NewValuesAreWrittenInTheAuthenticatedV2Format()
    {
        Assert.StartsWith("enc:v2:", Protector().Protect("token"));
    }

    /// <summary>
    /// An installation upgrading in place has v1 rows in the database. They must keep decrypting, or
    /// the upgrade silently breaks every configured integration.
    /// </summary>
    [Fact]
    public void LegacyV1CiphertextStillDecrypts()
    {
        var legacy = "enc:v1:" + Tools.Criptography.AES.Encrypt("legacy-token", LegacyPassphrase("root-a"));

        Assert.Equal("legacy-token", Protector().Unprotect(legacy));
    }

    /// <summary>
    /// Saving a connection whose credential is still v1 rewrites it as v2, which is what retires the
    /// weak format without an offline migration step.
    /// </summary>
    [Fact]
    public void ProtectingALegacyValueUpgradesItToV2()
    {
        var legacy = "enc:v1:" + Tools.Criptography.AES.Encrypt("legacy-token", LegacyPassphrase("root-a"));

        var upgraded = Protector().Protect(legacy);

        Assert.StartsWith("enc:v2:", upgraded);
        Assert.Equal("legacy-token", Protector().Unprotect(upgraded));
    }

    /// <summary>
    /// A v1 value from a *different* installation must be left byte-for-byte alone. v1 is
    /// unauthenticated, so decrypting it here can succeed with garbage; re-encrypting that garbage
    /// would destroy a value that is still recoverable on its own host.
    /// </summary>
    [Fact]
    public void ProtectingAForeignLegacyValueLeavesItUntouched()
    {
        var foreign = "enc:v1:" + Tools.Criptography.AES.Encrypt("legacy-token", LegacyPassphrase("root-b"));

        Assert.Equal(foreign, Protector("root-a").Protect(foreign));
    }

    [Fact]
    public void LooksProtectedRecognisesBothFormats()
    {
        var protector = Protector();

        Assert.True(protector.LooksProtected("enc:v2:anything"));
        Assert.True(protector.LooksProtected("enc:v1:anything"));
        Assert.False(protector.LooksProtected("https://hooks.slack.com/x"));
    }

    /// <summary>
    /// Mirrors <c>SecretProtector.DerivePassphrase</c>, so a test can produce a value in the old
    /// format without the production code exposing its key derivation.
    /// </summary>
    private static string LegacyPassphrase(string rootSecret) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("netrisk.integrations.secret.v1|" + rootSecret)));
}
