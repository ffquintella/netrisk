using System.Collections.Generic;
using System.Net.Security;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 findings NR-2026-004 and NR-2026-005 — the desktop client accepted any server
/// certificate.
///
/// <c>RestService</c> set <c>RemoteCertificateValidationCallback = (…) =&gt; true</c> for every call,
/// and the first-run URL check in <c>App.axaml.cs</c> did the same. That removed transport
/// authentication entirely: anything able to answer on the configured host and port could read and
/// rewrite the session, including the password in the basic-auth header.
/// </summary>
[TestSubject(typeof(ServerCertificatePolicy))]
public class ServerCertificatePolicyTest
{
    /// <summary>The regression assertion: a bad certificate is refused when nobody opted in.</summary>
    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch)]
    public void AnInvalidCertificateIsRefusedByDefault(SslPolicyErrors errors) =>
        Assert.False(ServerCertificatePolicy.ShouldAccept(errors, allowInvalid: false));

    [Fact]
    public void AValidCertificateIsAcceptedEitherWay()
    {
        Assert.True(ServerCertificatePolicy.ShouldAccept(SslPolicyErrors.None, allowInvalid: false));
        Assert.True(ServerCertificatePolicy.ShouldAccept(SslPolicyErrors.None, allowInvalid: true));
    }

    /// <summary>
    /// The bypass has to remain available — a fresh self-hosted install runs on a self-signed
    /// certificate — but only when the operator has said so.
    /// </summary>
    [Fact]
    public void TheExplicitOptInStillWorks() =>
        Assert.True(ServerCertificatePolicy.ShouldAccept(
            SslPolicyErrors.RemoteCertificateChainErrors, allowInvalid: true));

    /// <summary>
    /// Null means "use the platform's own validation". Returning a callback that reimplements the
    /// platform's answer would replace a well-tested code path with ours for no benefit.
    /// </summary>
    [Fact]
    public void NoCallbackIsCreatedWhenValidationIsEnabled()
    {
        var warnings = new List<string>();

        Assert.Null(ServerCertificatePolicy.CreateCallback(false, warnings.Add));
        Assert.Empty(warnings);
    }

    /// <summary>
    /// Turning validation off must be loud. Asserted on so it cannot quietly become a debug line.
    /// </summary>
    [Fact]
    public void EnablingTheBypassWarnsImmediatelyAndMentionsTheSetting()
    {
        var warnings = new List<string>();

        var callback = ServerCertificatePolicy.CreateCallback(true, warnings.Add);

        Assert.NotNull(callback);
        Assert.Single(warnings);
        Assert.Contains("DISABLED", warnings[0]);
        Assert.Contains(ServerCertificatePolicy.ConfigurationKey, warnings[0]);
    }

    [Fact]
    public void TheBypassCallbackAcceptsAndWarnsOncePerRejection()
    {
        var warnings = new List<string>();
        var callback = ServerCertificatePolicy.CreateCallback(true, warnings.Add)!;

        Assert.True(callback(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(callback(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors));

        // One warning at construction, one the first time a certificate was actually rejected — and
        // not one per request, which would drown the log.
        Assert.Equal(2, warnings.Count);
        Assert.Contains("RemoteCertificateChainErrors", warnings[1]);
    }

    [Fact]
    public void TheBypassCallbackDoesNotWarnForAValidCertificate()
    {
        var warnings = new List<string>();
        var callback = ServerCertificatePolicy.CreateCallback(true, warnings.Add)!;

        Assert.True(callback(this, null, null, SslPolicyErrors.None));

        Assert.Single(warnings);
    }

    [Fact]
    public void TheConfigurationKeyIsTheOneTheClientsRead() =>
        Assert.Equal("Server:AllowInvalidCertificate", ServerCertificatePolicy.ConfigurationKey);

    // ---- Resolving the opt-in ----------------------------------------------------------------

    /// <summary>
    /// The regression assertion for a setting that was documented and unreachable. The first-run
    /// server check read only the persisted client store, while the error message it showed told the
    /// operator to set <c>Server:AllowInvalidCertificate</c> in <c>appsettings.json</c>. Since that
    /// check gates whether the server URL is ever saved, a client facing a self-signed server could
    /// never be configured at all — following the instruction verbatim changed nothing.
    /// </summary>
    [Fact]
    public void TheApplicationSettingIsHonouredWhenNothingIsPersisted()
    {
        Assert.True(ServerCertificatePolicy.Resolve(persistedValue: null, applicationSetting: true));
        Assert.False(ServerCertificatePolicy.Resolve(persistedValue: null, applicationSetting: false));
    }

    /// <summary>
    /// A value an operator set on the installed machine wins over what the build shipped with —
    /// including turning the bypass back *off*.
    /// </summary>
    [Theory]
    [InlineData("true", false, true)]
    [InlineData("false", true, false)]
    [InlineData("True", false, true)]
    public void ThePersistedValueOverridesTheApplicationSetting(
        string persisted, bool setting, bool expected) =>
        Assert.Equal(expected, ServerCertificatePolicy.Resolve(persisted, setting));

    /// <summary>
    /// An unparseable persisted value falls back rather than being read as "true". A configuration
    /// store holding the string "yes" must not disable certificate validation.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("  ")]
    public void AnUnparseablePersistedValueFallsBackToTheApplicationSetting(string persisted)
    {
        Assert.False(ServerCertificatePolicy.Resolve(persisted, applicationSetting: false));
        Assert.True(ServerCertificatePolicy.Resolve(persisted, applicationSetting: true));
    }
}
