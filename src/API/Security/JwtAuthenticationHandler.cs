using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;


namespace API.Security;

public class JwtAuthenticationHandler: AuthenticationHandler<JwtBearerOptions>
{
    private IEnvironmentService _environmentService;
    private readonly ILogger _log;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly IClientRegistrationService _clientRegistrationService;
    private readonly IPluginsService _pluginsService;
    private readonly IFaceIDService _faceIdService;
    private readonly IDalService _dalService;
    private readonly ITokenRevocationService _revocation;

    public JwtAuthenticationHandler(
        IOptionsMonitor<JwtBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IClientRegistrationService clientRegistrationService,
        IEnvironmentService environmentService,
        IUsersService usersService,
        IRolesService rolesService,
        IPluginsService pluginsService,
        IFaceIDService faceIdService,
        IDalService dalService,
        ITokenRevocationService revocation) : base(options, logger, encoder)
    {
        _revocation = revocation;
        _environmentService = environmentService;
        _usersService = usersService;
        _rolesService = rolesService;
        _log = Log.Logger;
        _clientRegistrationService = clientRegistrationService;
        _pluginsService = pluginsService;
        _faceIdService = faceIdService;
        _dalService = dalService;
    }
    
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            return AuthenticateResult.NoResult();
        }
        
            var authHeader = Request.Headers["Authorization"].ToString();
            var clientId = Request.Headers["ClientId"].ToString();
            
            
            // JWT Authentication 
            if (authHeader != null && authHeader.StartsWith("bearer", StringComparison.OrdinalIgnoreCase))
            {
                if (Options.RequireHttpsMetadata)
                {
                    if (!Request.IsHttps)
                    {
                        Response.StatusCode = 401;
                        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
                        return AuthenticateResult.Fail("Https is required");                    
                    }
                }
                
                string? username;
                var token = authHeader.Substring("Bearer ".Length).Trim();
                
                if (ValidateToken(token, out username))
                {
                    
                    var client = await _clientRegistrationService.FindApprovedRegistrationAsync(clientId);

                    if (client == null) // We should not allow an unauthorized client to login
                    {
                        _log.Error("Unauthorized client {clientId}", clientId);
                        Response.StatusCode = 401;
                        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
                        return AuthenticateResult.Fail("Invalid Client");                    
                    }

                    if (username == null) throw new Exception("Invalid username");
                    string usu = "";
                    if (username!.Contains('@')) usu = username.Split('@')[0];
                    else usu = username;
                    
                    var userObj = await _usersService.GetUserAsync(usu);
                    
                    var permissions = await _usersService.GetUserPermissionsAsync(userObj!.Value);
                    
                    // based on username to get more information from database 
                    // in order to build local identity
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, username!)
                        // Add more claims if needed: Roles, ...
                    };
                    
                    string? faceIdAuthHeader = null;
            
                    // Check for the faceId header only if the plugin is enabled
                    if (await _faceIdService.IsFaceIDPluginEnabled() && Request.Headers.ContainsKey("FaceId"))
                    {
                        faceIdAuthHeader = Request.Headers["FaceId"].ToString();
                        if (!string.IsNullOrEmpty(faceIdAuthHeader))
                        {
                            var validationResult = await _faceIdService.ValidateTokenAndLocateTransaction(userObj.Value!, faceIdAuthHeader);

                            if (validationResult.Item1)
                            {
                                claims.Add(new Claim("FaceIdTransaction", validationResult.Item2!.ToString()!));
                                claims.Add(new Claim("FaceIdToken", faceIdAuthHeader));
                            }
                            
                        }
                    }
                    
                    if (userObj.Admin)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                    }
                    
                    if (userObj.RoleId == 0)
                    {
                        claims.Add( new Claim(ClaimTypes.Role, "user"));    
                    }
                    else
                    {
                        var role = await _rolesService.GetRoleAsync(userObj.RoleId);
                        claims.Add( new Claim(ClaimTypes.Role, role!.Name));
                    }

                    foreach (var permission in permissions)
                    {
                        if(permission != null)
                            claims.Add( new Claim("Permission", permission));
                    }
                    
                    claims.Add(new Claim(ClaimTypes.Sid, userObj.Value.ToString()));

                    // Load Scoped Multi-Entity / Multi-Tenant Roles
                    using (var dbContext = _dalService.GetContext())
                    {
                        var activeEntityRoles = await dbContext.UserEntityRoles
                            .Where(uer => uer.UserId == userObj.Value && uer.RevokedAt == null)
                            .ToListAsync();

                        foreach (var entityRole in activeEntityRoles)
                        {
                            claims.Add(new System.Security.Claims.Claim("entity_id", entityRole.EntityId.ToString()));
                        }

                        // Check if the user is a Global Administrator
                        if (userObj.Admin)
                        {
                            claims.Add(new System.Security.Claims.Claim("scope", "global"));
                        }
                    }

                    var identity = new ClaimsIdentity(claims, "Bearer");
                    var user = new ClaimsPrincipal(identity);
                    _log.Debug("User {0} authenticated using token from client {1}", username, client.Name);
                    return AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name));
                    
                }
                
                Response.StatusCode = 401;
                Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }
            else
            {
                Response.StatusCode = 401;
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }
        }catch (Exception ex)
        {
            _log.Error("Error authenticating user: {0}", ex.Message);
            return AuthenticateResult.Fail("Error authenticating user");
        }
        
    }
    
    
    private ClaimsPrincipal? GetPrincipalFromJwt(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

            if (jwtToken == null)
                return null;
            
            SecurityToken securityToken;
            var principal = tokenHandler.ValidateToken(token, Options.TokenValidationParameters, out securityToken);

            return principal;
        }
        catch (Exception ex)
        {
            _log.Error("Error extracting credentials from token message: {0}", ex.Message);
            return null;
        }
    }
    
    private bool IsTokenExpired(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

            if (jwtToken == null)
                return true; // Token inválido ou não é um JWT

            // Verifica se o token já expirou
            return jwtToken.ValidTo < DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log.Error("Error checking token expiration: {0}", ex.Message);
            return true; // Considera o token como expirado em caso de erro
        }
    }
    
    private bool ValidateToken(string token, out string? username)
    {
        username = null;
        
        if (IsTokenExpired(token))
        {
            return false;
        }

        // Security finding NR-2026-028. Checked before anything expensive: a revoked token must not
        // reach a database read of the user, and the revocation lookup is cached.
        if (IsRevoked(token))
        {
            _log.Information("Refused a session token that has been revoked");
            return false;
        }

        var simplePrinciple = GetPrincipalFromJwt(token);
        if (simplePrinciple == null) return false;
        
        var identity = simplePrinciple.Identity as ClaimsIdentity;

        if (identity == null || !identity.IsAuthenticated)
            return false;

        var usernameClaim = identity.FindFirst(ClaimTypes.Name);
        username = usernameClaim?.Value;

        if (string.IsNullOrEmpty(username))
            return false;

        string usu;
        if (username.Contains('@'))
        {
            usu = username.Split('@')[0];
        } else usu = username;
        
        var user = _usersService.FindEnabledActiveUserAsync(usu).Result;

        if (user == null) return false;

        if (WasIssuedBeforeLastPasswordChange(token, user))
        {
            _log.Information(
                "Refused a session token for {User} issued before their last password change", usu);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Per-session revocation (security finding NR-2026-028): the token's own <c>jti</c> is on the
    /// revocation list.
    ///
    /// A token with no <c>jti</c> — minted before that claim was added — cannot be revoked
    /// individually and is not refused here; it is still covered by the mass-revocation checks below
    /// and by its own expiry.
    /// </summary>
    private bool IsRevoked(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadToken(token) as JwtSecurityToken;
            var jti = jwt?.Id;

            if (string.IsNullOrWhiteSpace(jti)) return false;

            return _revocation.IsRevokedAsync(jti).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A token that cannot be parsed is refused by the signature validation a few lines
            // later; failing open here rather than throwing keeps that the single rejection point.
            _log.Warning("Could not check the revocation list for a presented token: {Message}",
                ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Server-side session revocation (Track 7 milestone 7.3.2).
    ///
    /// NetRisk has no refresh-token flow and no token store, so there was nothing that could
    /// invalidate a token before it expired — changing a password left every session minted
    /// beforehand working, which is precisely the situation a password change is a reaction to.
    /// Comparing the token's <c>iat</c> against the user's last password change gives real
    /// revocation using a column that already exists: one write invalidates every outstanding
    /// session for that account.
    ///
    /// A small tolerance is allowed because the token is minted and the row is written in different
    /// requests and, in a clustered deployment, on different clocks; without it the very token
    /// handed back by a password-change flow would be rejected.
    /// </summary>
    private bool WasIssuedBeforeLastPasswordChange(string token, DAL.Entities.User user)
    {
        if (user.LastPasswordChangeDate == default) return false;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadToken(token) as JwtSecurityToken;
            if (jwt == null) return false;

            // IssuedAt is DateTime.MinValue on a token minted before the iat claim was added, and
            // those must keep working until they expire on their own.
            if (jwt.IssuedAt == default) return false;

            return jwt.IssuedAt.ToUniversalTime()
                   < DateTime.SpecifyKind(user.LastPasswordChangeDate, DateTimeKind.Utc)
                       .AddSeconds(-PasswordChangeToleranceSeconds);
        }
        catch (Exception ex)
        {
            _log.Error("Error reading the issue time of a session token: {Message}", ex.Message);
            return false;
        }
    }

    private const int PasswordChangeToleranceSeconds = 30;
}