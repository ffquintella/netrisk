using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Tools.Security;

namespace API.Middleware;

/// <summary>
/// Applies <see cref="SecurityHeaderPolicy"/> to every API response (Track 7 milestone 7.4.3).
///
/// The API set none of these headers before. It is consumed by a desktop client rather than a
/// browser, which is why it was easy to overlook — but its JSON and its error bodies render
/// perfectly well in a browser, and a security product that scores badly on a headers scan is a bad
/// look regardless of how it is normally called.
///
/// The policy itself, and the reasoning for each header, lives in <see cref="SecurityHeaderPolicy"/>
/// so that the WebSite computes exactly the same set.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeaderPolicy policy)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // On OnStarting rather than set inline: headers have to be written before the response
        // begins, and anything downstream may start it.
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

    /// <summary>
    /// Reads the policy from <c>Security:Headers</c>, defaulting to the data-only API policy.
    /// </summary>
    public static SecurityHeaderPolicy PolicyFrom(IConfiguration configuration)
    {
        var section = configuration.GetSection("Security:Headers");

        return SecurityHeaderPolicy.From(
            section["HstsMaxAgeSeconds"],
            section["HstsIncludeSubDomains"],
            section["HstsPreload"],
            section["ContentSecurityPolicy"],
            SecurityHeaderPolicy.ApiContentSecurityPolicy);
    }
}
