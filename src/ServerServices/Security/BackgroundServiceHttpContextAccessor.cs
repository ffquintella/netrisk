using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ServerServices.Security;

/// <summary>
/// The <see cref="IHttpContextAccessor"/> for hosts with no HTTP pipeline — the console client
/// and the Hangfire job host. Services below them resolve the acting user from the accessor
/// (<c>DalService.GetUserId</c> reads <see cref="ClaimTypes.Sid"/>), so those hosts must present
/// a principal rather than none. Replaces a Moq mock in shipped code; see CHANGELOG.md, [NEXT].
/// </summary>
public sealed class BackgroundServiceHttpContextAccessor : IHttpContextAccessor
{
    /// <summary>User id the background principal acts as. Matches the seeded admin row.</summary>
    public const string ServiceAccountSid = "1";

    /// <summary>Name the background principal is logged and audited under.</summary>
    public const string ServiceAccountName = "BackgroundServices";

    /// <summary>
    /// Any non-null authentication type makes <c>ClaimsIdentity.IsAuthenticated</c> true, which is
    /// what the services below actually test. Kept as the original literal so audit rows written
    /// before and after this change read the same.
    /// </summary>
    public const string AuthenticationType = "mock";

    public BackgroundServiceHttpContextAccessor()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, ServiceAccountSid),
                    new Claim(ClaimTypes.Name, ServiceAccountName),
                ],
                AuthenticationType)),
        };
    }

    /// <summary>
    /// The synthetic context, from construction. Settable because the interface says so; no host
    /// assigns it, and assigning null would leave <c>GetUserId</c> resolving nobody.
    /// </summary>
    public HttpContext? HttpContext { get; set; }
}
