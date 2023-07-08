using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using ServerServices;
using System.Linq;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;


namespace API.Security;

public class JwtAuthenticationHandler: AuthenticationHandler<JwtBearerOptions>
{
    //private SRDbContext? _dbContext = null;
    private DALManager _dalManager;
    private IEnvironmentService _environmentService;
    private ILogger _log;
    private IUserManagementService _userManagementService;
    private IRoleManagementService _roleManagementService;
    
    public JwtAuthenticationHandler(
        IOptionsMonitor<JwtBearerOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder, 
        ISystemClock clock,
        IEnvironmentService environmentService,
        IUserManagementService userManagementService,
        IRoleManagementService roleManagementService,
        DALManager dalManager) : base(options, logger, encoder, clock)
    {
        //_dbContext = dalManager.GetContext();
        _dalManager = dalManager;
        _environmentService = environmentService;
        _userManagementService = userManagementService;
        _roleManagementService = roleManagementService;
        _log = Log.Logger;
    }
    
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
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
                    Response.Headers.Add("WWW-Authenticate", "Basic realm=\"sr-netextras.net\"");
                    return Task.FromResult(AuthenticateResult.Fail("Https is required"));                    
                }
            }
            
            string? username;
            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            if (ValidateToken(token, out username))
            {
                var dbContext = _dalManager.GetContext();
                // Let´s check if we have the client registred... 
                var client = dbContext!.AddonsClientRegistrations!
                    .FirstOrDefault(cl => cl.ExternalId == clientId && cl.Status == "approved");

                if (client == null) // We should not allow an unauthorized client to login
                {
                    _log.Error("Unauthorized client {clientId}", clientId);
                    Response.StatusCode = 401;
                    Response.Headers.Add("WWW-Authenticate", "Basic realm=\"sr-netextras.net\"");
                    return Task.FromResult(AuthenticateResult.Fail("Invalid Client"));                    
                }

                if (username == null) throw new Exception("Invalid username");
                string usu = "";
                if (username!.Contains('@')) usu = username.Split('@')[0];
                else usu = username;
                
                var userObj = _userManagementService.GetUser(usu);
                
                var permissions = _userManagementService.GetUserPermissions(userObj!.Value);
                
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
                    var role = _roleManagementService.GetRole(userObj.RoleId);
                    claims.Add( new Claim(ClaimTypes.Role, role!.Name));
                }

                foreach (var permission in permissions)
                {
                    claims.Add( new Claim("Permission", permission));
                }
                

                var identity = new ClaimsIdentity(claims, "Bearer");
                var user = new ClaimsPrincipal(identity);
                _log.Information("User {0} authenticated using token from client {1}", username, client.Name);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(user, Scheme.Name)));
                
            }
            
            Response.StatusCode = 401;
            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"sr-netextras.net\"");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
        else
        {
            Response.StatusCode = 401;
            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"simplerisk-netextras.net\"");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
    }
    
    
    private ClaimsPrincipal? GetPrincipalFromJWT(string token)
    {
        try
        {
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
        
        // Validate to check whether username exists in system
        var dbContext = _dalManager.GetContext();
        var user = dbContext?.Users?
            .Where(u => u.Enabled == true && u.Lockout == 0 && u.Username == Encoding.UTF8.GetBytes(usu))
            .FirstOrDefault();

        if (user == null) return false;


        return true;
    }
}