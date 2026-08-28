using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Guards the local-development TLS material that <c>src/API</c> and <c>src/WebSite</c> serve with.
///
/// Why this exists: the committed <c>certificate.pfx</c> was issued in September 2022 with a
/// one-year lifetime and expired on 2023-09-14. Nothing noticed. Every local client kept failing its
/// handshake for years, and the desktop client surfaced it only as
/// <c>The SSL connection could not be established</c> — an error that names neither the certificate
/// nor its expiry, and that reads exactly like a wrong URL or a stopped server. The cost was paid in
/// rediscovery, repeatedly.
///
/// So these tests are deliberately calendar-sensitive: the lifetime assertion fails while there is
/// still <see cref="RenewalMargin"/> left on the clock, which turns a silent outage into a test
/// failure with a fix attached. Reissue with
/// <c>./scripts/security/generate-dev-certificates.sh</c>.
///
/// None of this makes the material fit for deployment — its private key is committed, and
/// <c>Tools.Security.CommittedCertificates</c> refuses to boot a host configured with it. See
/// <c>docs/security/SECRETS.md</c>.
/// </summary>
public class DevelopmentCertificateTest
{
    /// <summary>
    /// How much remaining life the committed certificate must have. Large enough that the failure
    /// lands well before anyone's dev loop breaks, and that a checkout sitting on a release branch
    /// for a while does not go red for something nobody is expected to act on same-day.
    /// </summary>
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromDays(30);

    /// <summary>
    /// The password <c>appsettings.json</c> ships in both projects. Asserted on rather than
    /// parameterised, because the generator writing a different one would leave the API unable to
    /// open its own certificate — and that failure appears at start-up, not here, unless it is
    /// pinned.
    /// </summary>
    private const string PlaceholderPassword = "pass";

    public static TheoryData<string> Projects => new() { "API", "WebSite" };

    private static string CertificateDirectory(string project) =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "src", project, "Certificates");

    [Theory]
    [MemberData(nameof(Projects))]
    public void CommittedCertificateIsNotNearingExpiry(string project)
    {
        var path = Path.Combine(CertificateDirectory(project), "certificate.pem");

        Assert.True(File.Exists(path), $"Missing development certificate '{path}'.");

        using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(path));

        var now = DateTime.UtcNow;

        Assert.True(
            certificate.NotBefore.ToUniversalTime() <= now,
            $"src/{project}/Certificates/certificate.pem is not valid until "
            + $"{certificate.NotBefore.ToUniversalTime():u}.");

        Assert.True(
            certificate.NotAfter.ToUniversalTime() > now + RenewalMargin,
            $"src/{project}/Certificates/certificate.pem expires "
            + $"{certificate.NotAfter.ToUniversalTime():u}, which is inside the "
            + $"{RenewalMargin.TotalDays:0}-day renewal margin. Reissue it with "
            + "./scripts/security/generate-dev-certificates.sh — an expired development certificate "
            + "breaks every local client with an error that does not mention certificates.");
    }

    /// <summary>
    /// The desktop client's configured server URL is <c>https://127.0.0.1:5443/</c>, and hostname
    /// validation matches a literal IP against an iPAddress SAN — never against the subject common
    /// name. A certificate carrying only <c>CN=localhost</c> therefore fails against that URL even
    /// once it is trusted, which is the failure mode that makes someone give up and disable
    /// validation instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void CommittedCertificateCoversTheAddressesLocalClientsUse(string project)
    {
        using var certificate = X509Certificate2.CreateFromPem(
            File.ReadAllText(Path.Combine(CertificateDirectory(project), "certificate.pem")));

        var raw = certificate.Extensions
            .FirstOrDefault(extension => extension.Oid?.Value == "2.5.29.17");

        Assert.NotNull(raw);

        var san = new X509SubjectAlternativeNameExtension(raw.RawData, raw.Critical);

        Assert.Contains("localhost", san.EnumerateDnsNames());

        Assert.Contains(
            "127.0.0.1",
            san.EnumerateIPAddresses().Select(address => address.ToString()));
    }

    /// <summary>
    /// Opens the PKCS#12 exactly as Kestrel does. Worth a test of its own because the archive is
    /// produced by whatever OpenSSL the person reissuing it happens to have, and the encryption
    /// defaults of that format have changed — a .pfx that <c>openssl</c> writes happily can still be
    /// one .NET refuses to load, and that shows up as the API failing to start.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void CommittedArchiveOpensWithTheShippedPassword(string project)
    {
        var path = Path.Combine(CertificateDirectory(project), "certificate.pfx");

        Assert.True(File.Exists(path), $"Missing development certificate archive '{path}'.");

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, PlaceholderPassword);

        Assert.True(
            certificate.HasPrivateKey,
            $"src/{project}/Certificates/certificate.pfx carries no private key, so Kestrel cannot "
            + "serve TLS with it.");
    }

    /// <summary>
    /// The certificate-validation bypass is a Debug-only affordance.
    /// <c>ClientConfigurationSources.Release</c> reads <c>appsettings.json</c> and never the
    /// development file, so the opt-in set for local work cannot reach a packaged client — provided
    /// nobody copies it across. This asserts nobody has.
    /// </summary>
    [Fact]
    public void ReleaseClientConfigurationDoesNotDisableCertificateValidation()
    {
        var path = Path.Combine(
            RepositoryPaths.RepositoryRoot, "src", "GUIClient", "appsettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("Server", out var server)) return;

        if (!server.TryGetProperty("AllowInvalidCertificate", out var allow)) return;

        Assert.False(
            allow.ValueKind == JsonValueKind.True
            || (allow.ValueKind == JsonValueKind.String
                && bool.TryParse(allow.GetString(), out var parsed) && parsed),
            "src/GUIClient/appsettings.json is the configuration a Release build ships, and it "
            + "disables TLS certificate validation. docs/security/SECRETS.md requires "
            + "Server:AllowInvalidCertificate to be unset on every deployed client; keep the opt-in "
            + "in appsettings.development.json, which only a Debug build reads.");
    }

    /// <summary>
    /// The reissue command the other failures point at has to exist, and has to still write both
    /// projects — a generator that silently covers only the API leaves the WebSite expired.
    /// </summary>
    [Fact]
    public void ReissueScriptExistsAndCoversBothProjects()
    {
        var path = Path.Combine(
            RepositoryPaths.RepositoryRoot, "scripts", "security", "generate-dev-certificates.sh");

        Assert.True(File.Exists(path), $"Missing '{path}'.");

        var script = File.ReadAllText(path);

        Assert.Contains("API", script);
        Assert.Contains("WebSite", script);
        Assert.Contains("subjectAltName", script);
    }
}
