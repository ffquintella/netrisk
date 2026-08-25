using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Security;

/// <summary>
/// Authenticates <c>Authorization: Bearer scim_…</c> — the provisioning-token scheme
/// (Track 4 milestone 4.3.2).
///
/// A scheme of its own rather than a branch inside the CI-token handler. A SCIM token is not a user: it
/// acts as itself, has no permissions beyond provisioning, and must never inherit a person's rights.
/// Giving it its own handler is what makes that structural rather than a condition somebody can forget.
/// </summary>
public class ScimAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name; also what <see cref="API.AuthenticationBootstrapper"/> forwards to.</summary>
    public const string SchemeName = "ScimToken";

    /// <summary>Claim type carrying the authenticating token's id, for the request audit.</summary>
    public const string TokenIdClaimType = "scim_token_id";

    /// <summary>
    /// The role the token is granted. Every SCIM endpoint requires exactly this role, so a provisioning
    /// token cannot reach anything else in the API even though it is a valid bearer credential.
    /// </summary>
    public const string ScimRole = "scim_provisioner";

    private readonly IScimService _scim;
    private readonly ILogger _log;

    public ScimAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IScimService scim,
        ILogger serilogLogger) : base(options, logger, encoder)
    {
        _scim = scim;
        _log = serilogLogger;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null) return AuthenticateResult.NoResult();

        var authHeader = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader)) return AuthenticateResult.Fail("No Authorization header");

        if (!authHeader.StartsWith("bearer ", System.StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Not a bearer token");

        var presented = authHeader["Bearer ".Length..].Trim();

        // NoResult, not Fail: this is how the scheme says "not mine", so an ordinary user token still
        // reaches the JWT handler without either handler knowing about the other.
        if (!presented.StartsWith(DAL.Entities.ScimToken.SecretPrefix, System.StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var token = await _scim.AuthenticateAsync(presented);

        if (token == null)
        {
            // Uninformative on purpose: unknown, revoked and wrong-secret are one answer to the caller.
            _log.Warning("SCIM authentication failed for a token presented from {Ip}",
                Request.HttpContext.Connection.RemoteIpAddress?.ToString());

            return AuthenticateResult.Fail("Invalid SCIM token");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"scim:{token.Name}"),
            new Claim(TokenIdClaimType, token.Id.ToString()),
            new Claim(ClaimTypes.Role, ScimRole)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
