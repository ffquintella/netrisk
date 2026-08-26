using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;


namespace API.Security;

public class BasicAuthenticationHandler: AuthenticationHandler<AuthenticationSchemeOptions>
{
    private NRDbContext? _dbContext = null;
    private IEnvironmentService _environmentService;
    private IUsersService _usersService;
    private IRolesService _rolesService;
    private readonly ILoginAttemptTracker _loginAttempts;
    private ILogger _log;
    
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder, 
        IEnvironmentService environmentService,
        IUsersService usersService,
        IRolesService rolesService,
        ILoginAttemptTracker loginAttempts,
        IDalService dalService) : base(options, logger, encoder)
    {
        _dbContext = dalService.GetContext();
        _environmentService = environmentService;
        _usersService = usersService;
        _rolesService = rolesService;
        _loginAttempts = loginAttempts;
        _log = Log.Logger;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            return AuthenticateResult.NoResult();
        }
        
        if( string.IsNullOrEmpty( Request.Headers["Authorization"])) return AuthenticateResult.Fail("Invalid Authorization Header");
        
        var authHeader = Request.Headers["Authorization"].ToString();
        
        // Basic Authentication
        if (authHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
        {
            // Track 7 finding NR-2026-018: the header used to be decoded and split without
            // guarding either step, so a value that was not base64, or carried no colon, threw out
            // of the handler and surfaced as a 500. An unauthenticated caller should not be able to
            // choose between "401" and "server error" by malforming a header.
            if (!TryReadCredentials(authHeader, out var login, out var password))
                return Unauthenticated("Invalid Authorization Header");

            var remoteIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString();

            // Track 7 finding NR-2026-008: consulted before the password is checked, so a locked-out
            // identity costs an attacker a round trip and tells them nothing.
            var throttle = _loginAttempts.Check(login, remoteIp);
            if (throttle.IsLockedOut)
            {
                Response.Headers.RetryAfter = ((int)Math.Ceiling(throttle.RetryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Unauthenticated("Too many failed attempts");
            }

            {
                var user = await _usersService.GetUserAsync(login);
                
                if (user != null)
                {
                    if (user.Lockout == 1)
                    {
                        return AuthenticateResult.Fail("User is locked out");
                    }

                    // Track 7 finding NR-2026-007: only Lockout was checked here, so a user who had
                    // been *disabled* — the flag the admin UI and SCIM deprovisioning both set —
                    // could still sign in with basic authentication. The JWT path already refused
                    // them, which is what made the gap easy to miss.
                    if (user.Enabled != true)
                    {
                        _log.Warning("Refused basic authentication for disabled user {UserId}", user.Value);
                        return AuthenticateResult.Fail("User is not enabled");
                    }
                    
                    // Check the password
                    var valid = _usersService.VerifyPassword(user.Value, password);
                    
                    if (valid)
                    {
                        _loginAttempts.RegisterSuccess(login, remoteIp);
                        var clientId = Request.Headers["ClientId"].ToString();
                        // Let´s check if we have the client registred... 
                        var client = await _dbContext!.ClientRegistrations!
                            .FirstOrDefaultAsync(cl => cl.ExternalId == clientId && cl.Status == "approved");

                        if (client == null) // We should not allow an unauthorized client to login
                        {
                            _log.Error("Unauthorized client {clientId}", clientId);
                            Response.StatusCode = 401;
                            Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
                            return AuthenticateResult.Fail("Invalid Client");                    
                        }
                        
                        var userPermissions = await _usersService.GetUserPermissionsAsync(user.Value);
                        
                        var claims = new[] { new Claim(ClaimTypes.Name, login) };
                        
                        if (user.Admin)
                        {
                            claims = claims.Concat(new[] {new Claim(ClaimTypes.Role, "Admin")}).ToArray();
                        }
                        
                        if (user.RoleId == 0)
                        {
                            claims = claims.Concat(new[] { new Claim(ClaimTypes.Role, "user")}).ToArray();    
                        }
                        else
                        {
                            var role = await _rolesService.GetRoleAsync(user.RoleId);
                            claims = claims.Concat(new[] { new Claim(ClaimTypes.Role, role!.Name)}).ToArray(); 
                        }
                        
                        foreach (var permission in userPermissions)
                        {
                            if(!string.IsNullOrEmpty(permission)) claims = claims.Concat(new[] {new Claim("Permission", permission)}).ToArray();
                        }
                        
                        claims = claims.Concat(new[] {new Claim(ClaimTypes.Sid, user.Value.ToString())}).ToArray();
                        
                        _log.Information("User {0} authenticated using basic from client {1}", user.Name, client.Name);
                        
                        _= _usersService.RegisterLoginAsync(user.Value, Request.HttpContext.Connection.RemoteIpAddress!.ToString());
                        
                        var finalClaims = claims.ToList();
                        
                        // Load Scoped Multi-Entity / Multi-Tenant Roles
                        if (_dbContext != null)
                        {
                            var activeEntityRoles = await _dbContext.UserEntityRoles
                                .Where(uer => uer.UserId == user.Value && uer.RevokedAt == null)
                                .ToListAsync();

                            foreach (var entityRole in activeEntityRoles)
                            {
                                finalClaims.Add(new Claim("entity_id", entityRole.EntityId.ToString()));
                            }

                            // Check if the user is a Global Administrator
                            if (user.Admin)
                            {
                                finalClaims.Add(new Claim("scope", "global"));
                            }
                        }

                        var identity = new ClaimsIdentity(finalClaims, "Basic");
                        
                        var claimsPrincipal = new ClaimsPrincipal(identity);
                        return AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name));
                    }
                }
            }

            _loginAttempts.RegisterFailure(login, remoteIp);
            return Unauthenticated("Invalid Authorization Header");
        }
        return AuthenticateResult.Fail("Invalid Authorization Header"); 
    }

    /// <summary>
    /// Emits the 401 challenge and the matching failure in one place, so no path can set the status
    /// code without the <c>WWW-Authenticate</c> header or vice versa.
    /// </summary>
    private AuthenticateResult Unauthenticated(string reason)
    {
        Response.StatusCode = 401;
        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
        return AuthenticateResult.Fail(reason);
    }

    /// <summary>
    /// Decodes an <c>Authorization: Basic</c> header into a login and a password.
    ///
    /// The password is everything after the *first* colon, not the second field of a split: RFC 7617
    /// says the user-id may not contain a colon but places no such restriction on the password, and
    /// splitting on every colon silently truncated any password containing one.
    /// </summary>
    private static bool TryReadCredentials(string authHeader, out string login, out string password)
    {
        login = "";
        password = "";

        if (authHeader.Length <= "Basic ".Length) return false;

        var encoded = authHeader.Substring("Basic ".Length).Trim();

        Span<byte> decoded = new byte[encoded.Length];
        if (!Convert.TryFromBase64String(encoded, decoded, out var written)) return false;

        var pair = Encoding.UTF8.GetString(decoded[..written]);
        var separator = pair.IndexOf(':');
        if (separator <= 0) return false;

        login = pair[..separator];
        password = pair[(separator + 1)..];

        return login.Length > 0 && password.Length > 0;
    }
}