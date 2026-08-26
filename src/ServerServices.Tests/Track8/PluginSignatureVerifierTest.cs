using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JetBrains.Annotations;
using NSubstitute;
using Serilog;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Plugin publisher verification (security finding NR-2026-027's mitigation).
///
/// The finding stays risk-accepted, because this does not confine anything — a loaded plugin still
/// runs with the API's full authority and .NET has no supported in-process sandbox. What is tested
/// here is the trust decision, and the case that has to hold is the negative one: an attacker who can
/// write to the plugins directory can also write a <c>.sig</c> and a <c>.cer</c> beside their DLL, so
/// a verifier that accepts any self-consistent signature pair adds nothing at all. That is why the
/// thumbprint allowlist exists and why the tests below spend most of their effort on rejection.
///
/// Signatures are made here with a self-signed certificate generated in the test, so nothing depends
/// on a machine trust store or on a checked-in key.
/// </summary>
[TestSubject(typeof(PluginSignatureVerifier))]
public class PluginSignatureVerifierTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "nr-plugin-sig-" + Guid.NewGuid().ToString("N"));

    private readonly PluginSignatureVerifier _verifier =
        new(Substitute.For<ILogger>());

    public PluginSignatureVerifierTest() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static X509Certificate2 NewCertificate(string subject = "CN=NetRisk Plugin Test",
        int notBeforeDays = -1, int notAfterDays = 365)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest($"{subject}", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(notBeforeDays),
            DateTimeOffset.UtcNow.AddDays(notAfterDays));
    }

    /// <summary>Writes a fake assembly, and optionally a matching detached signature pair.</summary>
    private string WritePlugin(string name, byte[]? content = null, X509Certificate2? signer = null,
        byte[]? tamperedSignature = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3, 4, 5]);

        if (signer is null) return path;

        File.WriteAllBytes(path + PluginSignatureVerifier.CertificateExtension, signer.RawData);

        if (tamperedSignature is not null)
        {
            File.WriteAllBytes(path + PluginSignatureVerifier.SignatureExtension, tamperedSignature);
            return path;
        }

        byte[] digest;
        using (var stream = File.OpenRead(path)) digest = SHA256.HashData(stream);

        using var key = signer.GetRSAPrivateKey()!;

        File.WriteAllBytes(path + PluginSignatureVerifier.SignatureExtension,
            key.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        return path;
    }

    private static string ThumbprintOf(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    [Fact]
    public void TestAValidDetachedSignatureIsAccepted()
    {
        using var certificate = NewCertificate();

        var result = _verifier.Verify(WritePlugin("Good.Plugin.dll", signer: certificate));

        Assert.True(result.IsSigned, result.Detail);
        Assert.Equal("CN=NetRisk Plugin Test", result.Publisher);
        Assert.Equal(ThumbprintOf(certificate), result.Thumbprint);
        Assert.Null(result.Detail);
    }

    /// <summary>
    /// The whole point of hashing the file rather than trusting the pair: an attacker who swaps the
    /// DLL after it was signed must not pass. One flipped byte is enough.
    /// </summary>
    [Fact]
    public void TestAnAssemblyModifiedAfterSigningIsRefused()
    {
        using var certificate = NewCertificate();

        var path = WritePlugin("Swapped.Plugin.dll", signer: certificate);

        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var result = _verifier.Verify(path);

        Assert.False(result.IsSigned);
        Assert.Contains("does not match", result.Detail);
    }

    [Fact]
    public void TestGarbageInTheSignatureFileIsRefusedRatherThanThrowing()
    {
        using var certificate = NewCertificate();

        var result = _verifier.Verify(WritePlugin("Garbage.Plugin.dll", signer: certificate,
            tamperedSignature: [9, 9, 9, 9]));

        Assert.False(result.IsSigned);
        Assert.NotNull(result.Detail);
    }

    /// <summary>
    /// A signature made with an expired certificate proves the file is unchanged but says nothing
    /// about whether the publisher is still the publisher.
    /// </summary>
    [Fact]
    public void TestASignatureFromAnExpiredCertificateIsRefused()
    {
        using var certificate = NewCertificate(notBeforeDays: -400, notAfterDays: -10);

        var result = _verifier.Verify(WritePlugin("Expired.Plugin.dll", signer: certificate));

        Assert.False(result.IsSigned);
        Assert.Contains("validity window", result.Detail);
    }

    [Fact]
    public void TestACertificateNotYetValidIsRefused()
    {
        using var certificate = NewCertificate(notBeforeDays: 10, notAfterDays: 400);

        var result = _verifier.Verify(WritePlugin("Future.Plugin.dll", signer: certificate));

        Assert.False(result.IsSigned);
        Assert.Contains("validity window", result.Detail);
    }

    [Fact]
    public void TestAMissingFileIsAResultNotAnException()
    {
        var result = _verifier.Verify(Path.Combine(_dir, "Absent.Plugin.dll"));

        Assert.False(result.IsSigned);
        Assert.Contains("does not exist", result.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TestABlankPathIsARefusal(string? path)
    {
        Assert.False(_verifier.Verify(path!).IsSigned);
    }

    /// <summary>
    /// An unsigned plugin on a non-Windows host is told *why*, and specifically what to do about it.
    /// A bare "not signed" would read as a NetRisk limitation rather than a missing artifact.
    /// </summary>
    [Fact]
    public void TestAnUnsignedAssemblyExplainsHowToSignItPortably()
    {
        var result = _verifier.Verify(WritePlugin("Bare.Plugin.dll"));

        Assert.False(result.IsSigned);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Contains(".sig/.cer", result.Detail);
    }

    /// <summary>
    /// The case that makes the whole mechanism worth having. An attacker who can drop a DLL into the
    /// plugins directory can also drop a valid self-signed pair beside it, so with an allowlist
    /// configured their signature must not be enough.
    /// </summary>
    [Fact]
    public void TestAValidSignatureFromAnUnlistedPublisherIsNotTrusted()
    {
        using var ours = NewCertificate("CN=Acme Corp");
        using var theirs = NewCertificate("CN=Attacker");

        var result = _verifier.Verify(WritePlugin("Rogue.Plugin.dll", signer: theirs));

        Assert.True(result.IsSigned, "the attacker's own signature is cryptographically fine");
        Assert.False(PluginSignatureVerifier.IsTrusted(result, [ThumbprintOf(ours)]));
    }

    [Fact]
    public void TestAValidSignatureFromAListedPublisherIsTrusted()
    {
        using var certificate = NewCertificate("CN=Acme Corp");

        var result = _verifier.Verify(WritePlugin("Acme.Plugin.dll", signer: certificate));

        Assert.True(PluginSignatureVerifier.IsTrusted(result, [ThumbprintOf(certificate)]));
    }

    /// <summary>
    /// With no allowlist, any valid signature passes. That is the weaker posture on purpose: it still
    /// proves the file was not swapped after signing, and it does not force an operator to enrol a
    /// publisher before plugins work at all.
    /// </summary>
    [Fact]
    public void TestAnEmptyAllowlistAcceptsAnyValidSignature()
    {
        using var certificate = NewCertificate("CN=Anyone");

        var result = _verifier.Verify(WritePlugin("Any.Plugin.dll", signer: certificate));

        Assert.True(PluginSignatureVerifier.IsTrusted(result, []));
    }

    /// <summary>An unsigned plugin is never trusted, allowlist or not.</summary>
    [Fact]
    public void TestAnUnsignedAssemblyIsNeverTrusted()
    {
        var result = _verifier.Verify(WritePlugin("Bare2.Plugin.dll"));

        Assert.False(PluginSignatureVerifier.IsTrusted(result, []));
        Assert.False(PluginSignatureVerifier.IsTrusted(result, ["ABCD"]));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("ABCD", 1)]
    [InlineData("ABCD,EF01", 2)]
    [InlineData("ABCD; EF01", 2)]
    [InlineData("ABCD\nEF01\tAB02", 3)]
    [InlineData("ABCD,ABCD", 1)]
    public void TestThumbprintParsingHandlesTheFormatsAnOperatorWillActuallyType(string? configured,
        int expected)
    {
        Assert.Equal(expected, PluginSignatureVerifier.ParseThumbprints(configured).Length);
    }

    /// <summary>
    /// Thumbprints are copied out of certificate viewers, which print them colon-separated and in
    /// mixed case. Both the setting and the comparison normalise, so an operator who pastes what
    /// their tool showed them does not get a silent no-match.
    /// </summary>
    [Fact]
    public void TestColonSeparatedAndLowerCaseThumbprintsStillMatch()
    {
        using var certificate = NewCertificate("CN=Paste Test");

        var result = _verifier.Verify(WritePlugin("Paste.Plugin.dll", signer: certificate));

        var thumbprint = ThumbprintOf(certificate);
        var pasted = string.Join(":", thumbprint.Chunk(2).Select(c => new string(c).ToLowerInvariant()));

        Assert.True(PluginSignatureVerifier.IsTrusted(result,
            PluginSignatureVerifier.ParseThumbprints(pasted)));
    }

    /// <summary>
    /// A signature file with no certificate beside it is "unsigned", not "invalid" — the plugin
    /// simply was not shipped with the pair, and the message should say so rather than implying
    /// tampering.
    /// </summary>
    [Fact]
    public void TestASignatureWithoutItsCertificateReadsAsUnsigned()
    {
        var path = WritePlugin("Half.Plugin.dll");
        File.WriteAllBytes(path + PluginSignatureVerifier.SignatureExtension, [1, 2, 3]);

        var result = _verifier.Verify(path);

        Assert.False(result.IsSigned);
        Assert.DoesNotContain("does not match", result.Detail);
    }
}
