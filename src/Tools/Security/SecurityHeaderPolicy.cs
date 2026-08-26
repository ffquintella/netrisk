using System;
using System.Collections.Generic;

namespace Tools.Security;

/// <summary>
/// The security response headers every NetRisk HTTP surface sends, as data (Track 7 milestone
/// 7.4.3).
///
/// This is the policy, not the middleware. It lives in <c>Tools</c> — which neither ASP.NET host
/// references for web types — so that the API and the WebSite compute an identical header set from
/// one place while each keeps its own three-line middleware. The alternative, a shared middleware,
/// would mean putting a <c>Microsoft.AspNetCore.App</c> framework reference into a library the
/// Avalonia desktop client also consumes.
///
/// Why each header, since "we added the OWASP headers" is not a reason:
///  * <c>Strict-Transport-Security</c> — both hosts redirect HTTP to HTTPS, and a redirect is one
///    plaintext round trip an attacker on the path can intercept. HSTS removes it for every
///    subsequent visit. The max-age is configurable rather than fixed because committing a browser
///    to HTTPS-only for a host whose certificate does not yet validate locks users out, and that is
///    not recoverable from the server side.
///  * <c>X-Content-Type-Options: nosniff</c> — stops a browser deciding a JSON error body is really
///    HTML and running it.
///  * <c>X-Frame-Options: DENY</c> plus <c>frame-ancestors</c> in the CSP — nothing here is meant to
///    be embedded, and the API carries no anti-CSRF token, so framing it is pure downside.
///  * <c>Referrer-Policy: no-referrer</c> — request paths carry record ids, which must not leak to a
///    third party through a navigation.
///  * <c>Content-Security-Policy</c> — severe on the API, whose responses are data; looser on the
///    WebSite, which serves pages.
///  * <c>X-Permitted-Cross-Domain-Policies</c> and <c>Cross-Origin-Resource-Policy</c> — close the
///    legacy Flash/Silverlight and the cross-origin read paths.
/// <see cref="HeadersToRemove"/> also strips <c>Server</c>, which volunteers the Kestrel version.
/// </summary>
public sealed class SecurityHeaderPolicy
{
    /// <summary>
    /// The Content-Security-Policy for a host whose responses are almost entirely data.
    ///
    /// <c>default-src 'none'</c> is the load-bearing directive: no script, no image, no frame, no
    /// connection. What it is *not* is <c>form-action 'none'</c>, and that is a correction rather
    /// than a relaxation. The API serves exactly one HTML page — the single-sign-on consent screen
    /// (<c>AuthenticationController.SAMLSingIn</c>), whose whole purpose is a form the user submits
    /// back to <c>/Authentication/SAMLApprove</c>. With <c>form-action 'none'</c> the browser refuses
    /// that submission outright, which would silently break every desktop SSO sign-in while every
    /// unit test still passed, because a controller test never sees a Content-Security-Policy.
    /// <c>'self'</c> permits that one same-origin post and nothing else.
    ///
    /// <c>style-src 'unsafe-inline'</c> is for the same page's <c>style=</c> attributes. It is safe
    /// here in a way it usually is not: the page is server-generated, every interpolated value goes
    /// through HTML encoding, and there is no script directive to pair it with — an injected style
    /// on a page that cannot execute script has nothing to do.
    /// </summary>
    public const string ApiContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; "
        + "base-uri 'none'; form-action 'self'";

    /// <summary>
    /// The Content-Security-Policy for the public WebSite.
    ///
    /// <c>'unsafe-inline'</c> on styles is present and not an oversight: the Razor views and the
    /// bundled CSS framework carry inline style attributes, and a policy that breaks the site is a
    /// policy that gets removed. Scripts are restricted to same-origin with no inline allowance,
    /// which is the half that actually stops an injected payload from executing.
    /// </summary>
    public const string WebSiteContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; "
        + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>180 days — long enough to matter, short enough to back out of.</summary>
    public const int DefaultMaxAgeSeconds = 15_552_000;

    /// <summary>
    /// HSTS <c>max-age</c>, in seconds. Zero omits the header, which is the correct setting while an
    /// installation is still on a self-signed certificate.
    /// </summary>
    public int MaxAgeSeconds { get; init; } = DefaultMaxAgeSeconds;

    public bool IncludeSubDomains { get; init; }

    /// <summary>
    /// Off by default and deliberately so: submission to the browser preload list is effectively
    /// irreversible, which is not something a deployment should acquire by accident.
    /// </summary>
    public bool Preload { get; init; }

    /// <summary>The policy sent as <c>Content-Security-Policy</c>; null or empty omits it.</summary>
    public string? ContentSecurityPolicy { get; init; } = ApiContentSecurityPolicy;

    /// <summary>Headers to delete from every response before it is sent.</summary>
    public static IReadOnlyList<string> HeadersToRemove { get; } =
        ["Server", "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version"];

    /// <summary>
    /// The headers to set on a response.
    /// </summary>
    /// <param name="isHttps">
    /// Whether the request arrived over TLS. HSTS is omitted otherwise: sending it over plain HTTP
    /// is meaningless per the specification, and it would also fire on a local HTTP debugging
    /// session and pin the developer's browser.
    /// </param>
    public IReadOnlyDictionary<string, string> BuildHeaders(bool isHttps)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "no-referrer",
            ["X-Permitted-Cross-Domain-Policies"] = "none",
            ["Cross-Origin-Resource-Policy"] = "same-origin"
        };

        if (isHttps && MaxAgeSeconds > 0)
        {
            var value = "max-age=" + MaxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (IncludeSubDomains) value += "; includeSubDomains";
            if (Preload) value += "; preload";
            headers["Strict-Transport-Security"] = value;
        }

        if (!string.IsNullOrWhiteSpace(ContentSecurityPolicy))
            headers["Content-Security-Policy"] = ContentSecurityPolicy;

        return headers;
    }

    /// <summary>
    /// Builds a policy from three configuration strings, so each host can read them from its own
    /// <c>Security:Headers</c> section without this type depending on the configuration stack.
    /// An unparseable value falls back to the default rather than throwing at startup: a typo in
    /// <c>HstsMaxAgeSeconds</c> should not stop the service from booting.
    /// </summary>
    public static SecurityHeaderPolicy From(
        string? maxAgeSeconds, string? includeSubDomains, string? preload,
        string? contentSecurityPolicy, string defaultContentSecurityPolicy)
    {
        return new SecurityHeaderPolicy
        {
            MaxAgeSeconds = int.TryParse(maxAgeSeconds, out var maxAge) && maxAge >= 0
                ? maxAge
                : DefaultMaxAgeSeconds,
            IncludeSubDomains = bool.TryParse(includeSubDomains, out var sub) && sub,
            Preload = bool.TryParse(preload, out var pre) && pre,
            ContentSecurityPolicy = string.IsNullOrWhiteSpace(contentSecurityPolicy)
                ? defaultContentSecurityPolicy
                : contentSecurityPolicy
        };
    }
}
