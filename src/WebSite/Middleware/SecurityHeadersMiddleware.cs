using Tools.Security;

namespace WebSite.Middleware;

/// <summary>
/// Applies <see cref="SecurityHeaderPolicy"/> to every WebSite response (Track 7 milestone 7.4.3).
///
/// A near-copy of the API's middleware, and deliberately so: sharing one middleware type would mean
/// putting a <c>Microsoft.AspNetCore.App</c> framework reference into <c>Tools</c>, which the
/// Avalonia desktop client also consumes. The part worth sharing — which headers, with which values
/// and why — is shared, in <see cref="SecurityHeaderPolicy"/>; what is duplicated is the ten lines
/// that push a dictionary onto a response.
///
/// The WebSite serves pages rather than data, so it passes the looser
/// <see cref="SecurityHeaderPolicy.WebSiteContentSecurityPolicy"/> as its default.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeaderPolicy policy)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            foreach (var name in SecurityHeaderPolicy.HeadersToRemove)
                context.Response.Headers.Remove(name);

            foreach (var (name, value) in policy.BuildHeaders(context.Request.IsHttps))
                context.Response.Headers[name] = value;

            return Task.CompletedTask;
        });

        await next(context);
    }

    /// <summary>Reads the policy from <c>Security:Headers</c>, defaulting to the page policy.</summary>
    public static SecurityHeaderPolicy PolicyFrom(IConfiguration configuration)
    {
        var section = configuration.GetSection("Security:Headers");

        return SecurityHeaderPolicy.From(
            section["HstsMaxAgeSeconds"],
            section["HstsIncludeSubDomains"],
            section["HstsPreload"],
            section["ContentSecurityPolicy"],
            SecurityHeaderPolicy.WebSiteContentSecurityPolicy);
    }
}
