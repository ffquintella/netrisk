using System.Net;
using JetBrains.Annotations;
using Serilog;
using ServerServices.Http;
using Xunit;

namespace ServerServices.Tests.Track7;

/// <summary>
/// Track 7 finding NR-2026-013 — server-side request forgery through the integration URLs.
///
/// Before this policy existed, <c>OutboundHttpClient</c> sent a request to whatever URL was stored on
/// a notification channel, issue-tracker connection or posture provider, and returned the response
/// body to the caller. The highest-value target is the cloud instance metadata service, whose reply
/// on a default IMDSv1 instance is a set of cloud credentials.
/// </summary>
[TestSubject(typeof(OutboundUrlPolicy))]
public class OutboundUrlPolicyTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static OutboundUrlPolicy Permissive() => new(Log, blockPrivateNetworks: false);
    private static OutboundUrlPolicy Strict(params string[] allowed) =>
        new(Log, blockPrivateNetworks: true, allowed);

    // ---- Always refused ----

    /// <summary>The regression assertion: the AWS/Azure/GCP metadata address, in either family.</summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://169.254.169.254/metadata/instance?api-version=2021-02-01")]
    [InlineData("https://169.254.170.2/v2/credentials")]
    [InlineData("http://[fd00:ec2::254]/latest/meta-data/")]
    [InlineData("http://[fe80::1]/")]
    public void TheMetadataAndLinkLocalAddressesAreNeverAllowed(string url)
    {
        // Even with the permissive private-network setting, which is the default.
        var verdict = Permissive().Evaluate(url);

        Assert.False(verdict.IsAllowed);
        Assert.NotNull(verdict.Reason);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("gopher://example.com/")]
    [InlineData("jar:http://example.com!/")]
    public void OnlyHttpAndHttpsAreAllowed(string url) =>
        Assert.False(Permissive().Evaluate(url).IsAllowed);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void AnEmptyOrRelativeDestinationIsRefused(string? url) =>
        Assert.False(Permissive().Evaluate(url).IsAllowed);

    // ---- Allowed by default, because self-hosted deployments need them ----

    /// <summary>
    /// The counterpart assertion, and the reason this policy is layered rather than a blanket block:
    /// an on-premise Jira on a private address is the normal case for this product, not an attack.
    /// </summary>
    [Theory]
    [InlineData("https://jira.internal.example.com/rest/api/2/issue")]
    [InlineData("https://10.0.0.5/rest/api/2/issue")]
    [InlineData("https://192.168.1.20:8443/api")]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("https://hooks.slack.com/services/T0/B0/xyz")]
    public void PrivateAndPublicDestinationsAreAllowedByDefault(string url) =>
        Assert.True(Permissive().Evaluate(url).IsAllowed, url);

    // ---- Opt-in strict mode, for an installation that only integrates with SaaS ----

    [Theory]
    [InlineData("https://10.0.0.5/api")]
    [InlineData("https://172.16.4.4/api")]
    [InlineData("https://192.168.1.20/api")]
    [InlineData("http://127.0.0.1:9000/api")]
    [InlineData("http://[::1]:9000/api")]
    [InlineData("https://100.100.4.4/api")]
    public void StrictModeRefusesPrivateDestinations(string url) =>
        Assert.False(Strict().Evaluate(url).IsAllowed, url);

    [Fact]
    public void StrictModeStillAllowsAPublicDestination() =>
        Assert.True(Strict().Evaluate("https://hooks.slack.com/services/T0/B0/xyz").IsAllowed);

    /// <summary>
    /// Strict mode has to have an escape hatch, or the installation that wants it cannot use its own
    /// on-premise tracker.
    /// </summary>
    [Fact]
    public void StrictModeHonoursTheAllowedHostList()
    {
        Assert.True(Strict("10.0.0.5").Evaluate("https://10.0.0.5/rest/api/2/issue").IsAllowed);
        Assert.False(Strict("10.0.0.5").Evaluate("https://10.0.0.6/rest/api/2/issue").IsAllowed);
    }

    /// <summary>
    /// The allowlist must not override the metadata block — that would turn the escape hatch into the
    /// bypass the whole guard exists to prevent.
    /// </summary>
    [Fact]
    public void TheAllowedHostListCannotReachTheMetadataService() =>
        Assert.False(Strict("169.254.169.254").Evaluate("http://169.254.169.254/latest/").IsAllowed);

    // ---- The address classifiers, directly ----

    [Theory]
    [InlineData("169.254.169.254", true)]
    [InlineData("169.254.0.1", true)]
    [InlineData("170.254.169.254", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("8.8.8.8", false)]
    public void LinkLocalClassification(string address, bool expected) =>
        Assert.Equal(expected, OutboundUrlPolicy.IsMetadataOrLinkLocal(IPAddress.Parse(address)));

    [Theory]
    [InlineData("10.255.255.255", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void PrivateClassification(string address, bool expected) =>
        Assert.Equal(expected, OutboundUrlPolicy.IsPrivate(IPAddress.Parse(address)));

    /// <summary>
    /// An IPv4-mapped IPv6 literal is the classic way past a naive IPv4-only check. It must classify
    /// as the IPv4 address it maps to.
    /// </summary>
    [Fact]
    public void AnIpv4MappedIpv6AddressIsClassifiedAsItsIpv4Form()
    {
        Assert.True(OutboundUrlPolicy.IsMetadataOrLinkLocal(IPAddress.Parse("::ffff:169.254.169.254")));
        Assert.True(OutboundUrlPolicy.IsPrivate(IPAddress.Parse("::ffff:10.0.0.1")));
    }

    /// <summary>
    /// A hostname that does not resolve must not be reported as a security refusal: the send will
    /// fail a moment later with the real transport error, which is far more useful.
    /// </summary>
    [Fact]
    public void AnUnresolvableHostIsAllowedThroughToFailAsATransportError() =>
        Assert.True(Permissive()
            .Evaluate("https://no-such-host.invalid/api").IsAllowed);
}
