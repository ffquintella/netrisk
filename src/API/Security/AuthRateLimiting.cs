using System;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace API.Security;

/// <summary>
/// Request-rate limiting in front of the credential endpoints (Track 7 milestone 7.3.2).
///
/// Complementary to <see cref="ServerServices.Security.LoginAttemptTracker"/>, not a duplicate of
/// it. The tracker throttles by identity and only counts *failures*, which is what stops password
/// guessing; this limits requests per source address regardless of outcome, which is what stops a
/// caller from spending the server's CPU on bcrypt verifications — bcrypt at a work factor of 15 is
/// deliberately expensive, so a sustained flood is a denial of service on its own even if every
/// attempt is refused.
///
/// A global limiter rather than per-endpoint attributes: the credential check happens in an
/// authentication handler, before MVC has selected an endpoint, so an attribute on a controller
/// would be evaluated too late for the paths that matter.
/// </summary>
public static class AuthRateLimiting
{
    /// <summary>The policy name, for the <c>[EnableRateLimiting]</c> attribute if ever needed.</summary>
    public const string AuthPolicy = "netrisk-auth";

    /// <summary>
    /// Requests per minute per source address on the credential paths.
    ///
    /// Sized by the *legitimate* traffic, not by what feels strict. The desktop client polls
    /// <c>/Authentication/AppSAMLToken</c> once a second for up to five minutes while the user
    /// completes a single-sign-on in their browser, so anything at or below 300 would cut a slow
    /// sign-in off partway through and present as "SAML authentication timed out". A limit that
    /// breaks the normal case is a limit somebody sets to zero.
    ///
    /// That is affordable because this is the *anti-flood* control, not the anti-guessing one.
    /// Guessing is stopped by <see cref="ServerServices.Security.LoginAttemptTracker"/>, which locks
    /// an identity out after four failures and is consulted before bcrypt runs — so an attacker
    /// spending this budget on password attempts gets four verifications and then 296 refusals that
    /// cost nothing.
    /// </summary>
    private const int DefaultPermitPerMinute = 300;
    private const int DefaultQueueLimit = 0;

    /// <summary>
    /// Registers the limiter. Configurable through <c>Security:RateLimit:AuthRequestsPerMinute</c>;
    /// zero or a negative value disables it, which an operator behind their own WAF may legitimately
    /// want, and which is logged so the choice is visible.
    /// </summary>
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var permitPerMinute = DefaultPermitPerMinute;
        var configured = configuration["Security:RateLimit:AuthRequestsPerMinute"];
        if (int.TryParse(configured, out var parsed)) permitPerMinute = parsed;

        if (permitPerMinute <= 0)
        {
            Log.Warning("Authentication rate limiting is disabled by configuration "
                        + "(Security:RateLimit:AuthRequestsPerMinute = {Configured})", configured);
            services.AddRateLimiter(_ => { });
            return;
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    60.ToString(CultureInfo.InvariantCulture);

                Log.Warning("Rate-limited {Method} {Path} from {Ip}",
                    context.HttpContext.Request.Method, context.HttpContext.Request.Path,
                    context.HttpContext.Connection.RemoteIpAddress);

                return System.Threading.Tasks.ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsCredentialPath(context.Request.Path)) return RateLimitPartition.GetNoLimiter("open");

                // Partitioned by source address. Not by account: the account name is inside a
                // base64 header this layer would have to decode and trust, and an attacker choosing
                // a different name per attempt would then get a fresh bucket each time.
                var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = DefaultQueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
            });
        });

        Log.Information("Authentication endpoints are rate limited to {Permits} requests per minute per source",
            permitPerMinute);
    }

    /// <summary>
    /// The paths that cost a password verification, a signature verification or a token mint.
    ///
    /// An allowlist of prefixes rather than "everything": limiting the whole API to a per-minute
    /// budget would break a bulk scan import, which legitimately makes thousands of calls.
    /// </summary>
    public static bool IsCredentialPath(PathString path)
    {
        if (!path.HasValue) return false;

        var value = path.Value!;

        return value.StartsWith("/Authentication", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/Registration", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/WebAuthn", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/FaceID", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/BiometricTransaction", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/IdentityProviders", StringComparison.OrdinalIgnoreCase);
    }
}
