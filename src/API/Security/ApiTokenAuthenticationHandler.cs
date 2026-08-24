using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DAL.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Model.Findings;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Security;

/// <summary>
/// Authenticates <c>Authorization: Bearer nrk_…</c> — the non-interactive CI token scheme
/// (Track 3 milestone 3.5.1).
///
/// A separate scheme from JWT rather than a branch inside it: a CI token carries scope claims and no
/// password-derived identity, and mixing the two authentication models in one handler is how a
/// scope check ends up being skipped for one of them.
/// </summary>
public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name; also what <see cref="API.AuthenticationBootstrapper"/> forwards to.</summary>
    public const string SchemeName = "ApiToken";

    /// <summary>Claim type carrying one granted scope. One claim per scope.</summary>
    public const string ScopeClaimType = "api_scope";

    /// <summary>Claim type carrying the authenticating token's id, for auditing and rate limiting.</summary>
    public const string TokenIdClaimType = "api_token_id";

    private readonly IApiTokensService _tokensService;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly IDalService _dalService;
    private readonly ILogger _log;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokensService tokensService,
        IUsersService usersService,
        IRolesService rolesService,
        IDalService dalService,
        ILogger serilogLogger) : base(options, logger, encoder)
    {
        _tokensService = tokensService;
        _usersService = usersService;
        _rolesService = rolesService;
        _dalService = dalService;
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

        var presented = authHeader.Substring("Bearer ".Length).Trim();

        // NoResult, not Fail: this is how the scheme says "not mine", which lets the JWT handler
        // take an ordinary user bearer token without either handler having to know about the other.
        if (!presented.StartsWith(DAL.Entities.ApiToken.SecretPrefix, System.StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var token = await _tokensService.AuthenticateAsync(presented);
        if (token == null)
        {
            // Deliberately uninformative to the caller: revoked, expired, unknown and wrong-secret
            // are all one answer. The log is where the distinction lives.
            _log.Warning("API token authentication failed for a token presented from {Ip}",
                Request.HttpContext.Connection.RemoteIpAddress?.ToString());
            return AuthenticateResult.Fail("Invalid API token");
        }

        var user = await _usersService.GetUserByIdAsync(token.UserId);
        if (user == null)
        {
            _log.Error("API token {KeyId} acts as user {User}, who no longer exists", token.KeyId, token.UserId);
            return AuthenticateResult.Fail("Invalid API token");
        }

        if (user.Lockout == 1)
        {
            // A locked-out human must not keep acting through a token they issued.
            _log.Warning("API token {KeyId} refused: the user it acts as is locked out", token.KeyId);
            return AuthenticateResult.Fail("Invalid API token");
        }

        var claims = await BuildClaimsAsync(token, user);

        // Not awaited: recording the use is a nicety, and making a valid request wait on an extra
        // write (or fail with it) would be the wrong trade.
        _ = _tokensService.TouchAsync(token.Id, System.DateTime.UtcNow);

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private async Task<List<Claim>> BuildClaimsAsync(DAL.Entities.ApiToken token, DAL.Entities.User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Sid, user.Value.ToString()),
            new(TokenIdClaimType, token.Id.ToString())
        };

        foreach (var scope in ApiTokenScopes.Parse(token.Scopes))
            claims.Add(new Claim(ScopeClaimType, scope));

        // The token inherits the permissions of the user it acts as, so a token can never do more
        // than that person could. Its scopes then narrow that down further — the two are an AND,
        // not an OR.
        var permissions = await _usersService.GetUserPermissionsAsync(user.Value);
        foreach (var permission in permissions.Where(p => !string.IsNullOrEmpty(p)))
            claims.Add(new Claim("Permission", permission));

        if (user.RoleId != 0)
        {
            var role = await _rolesService.GetRoleAsync(user.RoleId);
            if (role != null) claims.Add(new Claim(ClaimTypes.Role, role.Name));
        }

        // Admin is deliberately NOT granted through a token, even when the user it acts as is one.
        // A CI runner holding a credential that bypasses every permission check is the outcome
        // scoped tokens exist to prevent; an admin who needs that can still use their own login.
        if (user.Admin)
            _log.Debug("API token {KeyId} acts as an administrator; the Admin role is not granted to tokens",
                token.KeyId);

        // Entity binding, if any, overrides the user's own scope: that is what makes a
        // pipeline-specific token safe to hand to one team (Track 2.3).
        if (token.EntityId != null)
        {
            claims.Add(new Claim("entity_id", token.EntityId.Value.ToString()));
        }
        else
        {
            await using var db = _dalService.GetContext();
            var entityRoles = await db.UserEntityRoles
                .Where(uer => uer.UserId == user.Value && uer.RevokedAt == null)
                .Select(uer => uer.EntityId)
                .ToListAsync();

            foreach (var entityId in entityRoles)
                claims.Add(new Claim("entity_id", entityId.ToString()));

            if (user.Admin) claims.Add(new Claim("scope", "global"));
        }

        return claims;
    }
}
