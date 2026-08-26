using System.Security.Claims;

namespace RiskPortal.Services;

/// <summary>
/// Reads the signed-in reviewer's API token out of the authentication cookie.
///
/// The token lives in the cookie's claims rather than in server-side session state: the portal is
/// meant to run behind a load balancer with no sticky sessions, and a data-protected cookie is
/// unreadable to the browser while needing no shared cache. It is the same trade the ASP.NET Core
/// cookie handler is designed for.
/// </summary>
public interface IPortalSession
{
    /// <summary>The API bearer token for the current request, or null when nobody is signed in.</summary>
    string? Token { get; }

    /// <summary>The signed-in reviewer's login.</summary>
    string? Login { get; }
}

public class PortalSession(IHttpContextAccessor accessor) : IPortalSession
{
    /// <summary>
    /// The claim the API token is carried in. Not a standard claim type on purpose — a bespoke name
    /// makes it obvious in a cookie dump that this is not an identity assertion.
    /// </summary>
    public const string TokenClaim = "netrisk:api_token";

    public string? Token =>
        accessor.HttpContext?.User.FindFirst(TokenClaim)?.Value;

    public string? Login =>
        accessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
}
