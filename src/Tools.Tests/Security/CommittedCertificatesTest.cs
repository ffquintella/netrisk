using System;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-003 — the shipped configuration served with a committed private key.
///
/// <c>src/API/appsettings.json</c> named <c>Certificates/certificate.pfx</c> with the password
/// <c>"pass"</c>. That file is in the repository, private key included, so an installation that
/// deployed the template unchanged had no transport security against anyone who had read the source.
/// </summary>
[TestSubject(typeof(CommittedCertificates))]
public class CommittedCertificatesTest
{
    /// <summary>The regression assertion, for every file the repository actually ships.</summary>
    [Theory]
    [InlineData("Certificates/certificate.pfx")]
    [InlineData("Certificates/localhost.pfx")]
    [InlineData("Certificates/demowebapp.local.pfx")]
    [InlineData("/etc/netrisk/certificate.pfx")]
    [InlineData("C:\\ProgramData\\NetRisk\\certificate.pfx")]
    public void ACommittedCertificateIsRefused(string file)
    {
        var problem = CommittedCertificates.Inspect(file, "a-real-password", allowed: false);

        Assert.NotNull(problem);
        Assert.Contains("private key is public", problem);
    }

    /// <summary>Copying the file elsewhere does not make its key private, hence the name-only match.</summary>
    [Fact]
    public void TheCheckIsOnTheFileNameNotThePath() =>
        Assert.NotNull(CommittedCertificates.Inspect(
            "/opt/somewhere/else/certificate.pem", "a-real-password", allowed: false));

    [Theory]
    [InlineData("pass")]
    [InlineData("password")]
    [InlineData("changeit")]
    public void APlaceholderPasswordIsRefused(string password)
    {
        var problem = CommittedCertificates.Inspect("/etc/netrisk/prod.pfx", password, allowed: false);

        Assert.NotNull(problem);
        Assert.Contains("placeholder", problem);
    }

    [Fact]
    public void BothProblemsAreReportedTogether()
    {
        var problem = CommittedCertificates.Inspect("Certificates/certificate.pfx", "pass", allowed: false);

        Assert.NotNull(problem);
        Assert.Contains("private key is public", problem);
        Assert.Contains("placeholder", problem);
    }

    [Fact]
    public void ARealCertificateAndPasswordPass() =>
        Assert.Null(CommittedCertificates.Inspect(
            "/etc/netrisk/netrisk.example.com.pfx", "s0me-real-secret", allowed: false));

    /// <summary>
    /// Case matters for the password (it is compared exactly) but not for the file name, which
    /// crosses case-insensitive filesystems.
    /// </summary>
    [Fact]
    public void FileNamesAreMatchedWithoutRegardToCaseButPasswordsAreNot()
    {
        Assert.NotNull(CommittedCertificates.Inspect("CERTIFICATE.PFX", "real", allowed: false));
        Assert.Null(CommittedCertificates.Inspect("/etc/netrisk/prod.pfx", "PASS", allowed: false));
    }

    /// <summary>Running locally against the committed certificate is what the opt-out is for.</summary>
    [Fact]
    public void TheOptOutPermitsEverything() =>
        Assert.Null(CommittedCertificates.Inspect("Certificates/certificate.pfx", "pass", allowed: true));

    [Fact]
    public void AnEmptyOrMissingFileIsNotTreatedAsCommitted()
    {
        Assert.Null(CommittedCertificates.Inspect(null, "real", allowed: false));
        Assert.Null(CommittedCertificates.Inspect("   ", "real", allowed: false));
    }

    [Fact]
    public void EnforceThrowsTheSameMessageInspectReturns()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => CommittedCertificates.Enforce("Certificates/certificate.pfx", "pass", allowed: false));

        Assert.Equal(
            CommittedCertificates.Inspect("Certificates/certificate.pfx", "pass", allowed: false),
            thrown.Message);
    }

    [Fact]
    public void EnforceIsSilentOnAnAcceptableConfiguration() =>
        CommittedCertificates.Enforce("/etc/netrisk/prod.pfx", "s0me-real-secret", allowed: false);

    /// <summary>
    /// The message has to tell the operator what to do, not merely that something is wrong: this is
    /// a refusal to boot, and the only place they will read about it is the log line.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheSettingAndTheDocumentation()
    {
        var problem = CommittedCertificates.Inspect("Certificates/certificate.pfx", "pass", allowed: false)!;

        Assert.Contains(CommittedCertificates.AllowKey, problem);
        Assert.Contains("docs/security/SECRETS.md", problem);
        Assert.Contains("https:certificate:file", problem);
    }
}
