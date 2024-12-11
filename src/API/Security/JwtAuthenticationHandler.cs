using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DAL;
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
    //private SRDbContext? _dbContext = null;
    //private readonly IDalService _dalService;
    private IEnvironmentService _environmentService;
    private readonly ILogger _log;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly IClientRegistrationService _clientRegistrationService;
    
    public JwtAuthenticationHandler(
        IOptionsMonitor<JwtBearerOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder, 
        IClientRegistrationService clientRegistrationService,
        IEnvironmentService environmentService,
        IUsersService usersService,
        IRolesService rolesService,
        IDalService dalService) : base(options, logger, encoder)
    {
        //_dbContext = dalManager.GetContext();
        //_dalService = dalService;
        _environmentService = environmentService;
        _usersService = usersService;
        _rolesService = rolesService;
        _log = Log.Logger;
        _clientRegistrationService = clientRegistrationService;
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
                //Response.Headers.Add("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }
        }catch (Exception ex)
        {
            _log.Error("Error authenticating user: {0}", ex.Message);
            return AuthenticateResult.Fail("Error authenticating user");
        }
        
    }
    
    
    private ClaimsPrincipal? GetPrincipalFromJWT(string token)
    {
        try
        {
            //IdentityModelEventSource.ShowPII = true;

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

            if (jwtToken == null)
                return null;

            //var symmetricKey = Convert.FromBase64String(_environmentService.ServerSecretToken);

            /*var validationParameters = new TokenValidationParameters()
            {
                RequireExpirationTime = true,
                ValidateIssuer = false,
                ValidateAudience = false,
                IssuerSigningKey = new SymmetricSecurityKey(symmetricKey)
            };*/

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
    
    private bool ValidateToken(string token, out string? username)
    {
        username = null;

        var simplePrinciple = GetPrincipalFromJWT(token);
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


        return true;
    }
}