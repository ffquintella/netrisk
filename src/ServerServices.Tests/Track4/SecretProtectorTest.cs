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
}
