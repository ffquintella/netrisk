using System.Linq;
using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-015: neither the API nor the WebSite sent a single security response
/// header. These tests pin the set both hosts now compute.
/// </summary>
[TestSubject(typeof(SecurityHeaderPolicy))]
public class SecurityHeaderPolicyTest
{
    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    [InlineData("X-Permitted-Cross-Domain-Policies", "none")]
    [InlineData("Cross-Origin-Resource-Policy", "same-origin")]
    public void TheUnconditionalHeadersAreAlwaysPresent(string name, string expected)
    {
        var headers = new SecurityHeaderPolicy().BuildHeaders(isHttps: true);

        Assert.Equal(expected, headers[name]);
    }

    [Fact]
    public void HstsIsSentOverHttpsWithTheConfiguredMaxAge()
    {
        var headers = new SecurityHeaderPolicy { MaxAgeSeconds = 31_536_000 }.BuildHeaders(isHttps: true);

        Assert.Equal("max-age=31536000", headers["Strict-Transport-Security"]);
    }

    /// <summary>
    /// Sending HSTS over plain HTTP is meaningless per the specification, and worse, it would fire on
    /// a local HTTP debugging session and pin the developer's browser to a scheme the dev server does
    /// not serve.
    /// </summary>
    [Fact]
    public void HstsIsNotSentOverPlainHttp()
    {
        var headers = new SecurityHeaderPolicy().BuildHeaders(isHttps: false);

        Assert.DoesNotContain("Strict-Transport-Security", headers.Keys);
    }

    /// <summary>
    /// Zero has to be a real "off" switch: an installation still on a self-signed certificate that
    /// pins its users to HTTPS-only cannot undo it from the server side.
    /// </summary>
    [Fact]
    public void AZeroMaxAgeDisablesHsts()
    {
        var headers = new SecurityHeaderPolicy { MaxAgeSeconds = 0 }.BuildHeaders(isHttps: true);

        Assert.DoesNotContain("Strict-Transport-Security", headers.Keys);
    }

    [Fact]
    public void SubDomainAndPreloadDirectivesAreAppendedOnlyWhenAskedFor()
    {
        var plain = new SecurityHeaderPolicy().BuildHeaders(true)["Strict-Transport-Security"];
        Assert.DoesNotContain("includeSubDomains", plain);
        Assert.DoesNotContain("preload", plain);

        var full = new SecurityHeaderPolicy { IncludeSubDomains = true, Preload = true }
            .BuildHeaders(true)["Strict-Transport-Security"];
        Assert.Contains("includeSubDomains", full);
        Assert.Contains("preload", full);
    }

    [Fact]
    public void TheApiPolicyForbidsScriptsFramesAndCrossOriginForms()
    {
        var csp = new SecurityHeaderPolicy().BuildHeaders(true)["Content-Security-Policy"];

        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
    }

    /// <summary>
    /// The regression assertion for a self-inflicted break. The API's policy was originally
    /// <c>form-action 'none'</c>, which a browser applies by refusing to submit *any* form on the
    /// page — including the single-sign-on consent form the API itself serves, whose submission is
    /// the whole point of the NR-2026-001 rework. Every controller test still passed, because a
    /// controller test never sees a Content-Security-Policy. Same-origin submission must be allowed;
    /// cross-origin must not.
    /// </summary>
    [Fact]
    public void TheApiPolicyAllowsItsOwnConsentFormToBeSubmitted()
    {
        var csp = new SecurityHeaderPolicy().BuildHeaders(true)["Content-Security-Policy"];

        Assert.Contains("form-action 'self'", csp);
        Assert.DoesNotContain("form-action 'none'", csp);
    }

    /// <summary>
    /// The consent page carries <c>style=</c> attributes. Safe to allow here because the page cannot
    /// execute script at all — but the *script* directive must stay closed, which
    /// <c>default-src 'none'</c> with no <c>script-src</c> override does.
    /// </summary>
    [Fact]
    public void TheApiPolicyAllowsInlineStylesButStillNoScript()
    {
        var csp = new SecurityHeaderPolicy().BuildHeaders(true)["Content-Security-Policy"];

        Assert.Contains("style-src 'unsafe-inline'", csp);
        Assert.DoesNotContain("script-src", csp);
    }

    /// <summary>
    /// The WebSite serves pages, so its policy is looser — but the half that matters, no inline
    /// script, must still hold, or an injected payload executes.
    /// </summary>
    [Fact]
    public void TheWebSitePolicyAllowsStylesButNotInlineScript()
    {
        var csp = SecurityHeaderPolicy.WebSiteContentSecurityPolicy;

        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public void AnEmptyContentSecurityPolicyOmitsTheHeader()
    {
        var headers = new SecurityHeaderPolicy { ContentSecurityPolicy = "" }.BuildHeaders(true);

        Assert.DoesNotContain("Content-Security-Policy", headers.Keys);
    }

    /// <summary>
    /// <c>Server</c> is the one that matters: Kestrel volunteers its version to anyone who asks.
    /// </summary>
    [Fact]
    public void TheChattyHeadersAreOnTheRemovalList()
    {
        Assert.Contains("Server", SecurityHeaderPolicy.HeadersToRemove);
        Assert.Contains("X-Powered-By", SecurityHeaderPolicy.HeadersToRemove);
    }

    [Fact]
    public void FromReadsConfigurationAndFallsBackOnGarbage()
    {
        var good = SecurityHeaderPolicy.From("600", "true", "true", "default-src 'self'",
            SecurityHeaderPolicy.ApiContentSecurityPolicy);

        Assert.Equal(600, good.MaxAgeSeconds);
        Assert.True(good.IncludeSubDomains);
        Assert.True(good.Preload);
        Assert.Equal("default-src 'self'", good.ContentSecurityPolicy);

        // A typo in the configuration must not stop the service booting, and must not silently
        // become "HSTS off" either — it falls back to the default.
        var bad = SecurityHeaderPolicy.From("six hundred", "yes please", null, "  ",
            SecurityHeaderPolicy.WebSiteContentSecurityPolicy);

        Assert.Equal(SecurityHeaderPolicy.DefaultMaxAgeSeconds, bad.MaxAgeSeconds);
        Assert.False(bad.IncludeSubDomains);
        Assert.False(bad.Preload);
        Assert.Equal(SecurityHeaderPolicy.WebSiteContentSecurityPolicy, bad.ContentSecurityPolicy);
    }

    [Fact]
    public void ANegativeMaxAgeFallsBackRatherThanEmittingNonsense() =>
        Assert.Equal(SecurityHeaderPolicy.DefaultMaxAgeSeconds,
            SecurityHeaderPolicy.From("-1", null, null, null,
                SecurityHeaderPolicy.ApiContentSecurityPolicy).MaxAgeSeconds);

    [Fact]
    public void HeaderNamesAreMatchedWithoutRegardToCase()
    {
        var headers = new SecurityHeaderPolicy().BuildHeaders(true);

        Assert.True(headers.ContainsKey("x-frame-options"));
    }
}
