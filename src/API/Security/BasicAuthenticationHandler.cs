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
    private ILogger _log;
    
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder, 
        IEnvironmentService environmentService,
        IUsersService usersService,
        IRolesService rolesService,
        IDalService dalService) : base(options, logger, encoder)
    {
        _dbContext = dalService.GetContext();
        _environmentService = environmentService;
        _usersService = usersService;
        _rolesService = rolesService;
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
            var token = authHeader.Substring("Basic ".Length).Trim();
            var credentialstring = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var credentials = credentialstring.Split(':');
            
            if (credentials[0] != "" && credentials[1] != "")
            {

                var user = await _usersService.GetUserAsync(credentials[0]);
                
                if (user != null)
                {
                    if (user.Lockout == 1)
                    {
                        return AuthenticateResult.Fail("User is locked out");
                    }
                    
                    // Check the password
                    var valid = _usersService.VerifyPassword(user.Value, credentials[1]);
                    
                    if (valid)
                    {
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
                        
                        var claims = new[] { new Claim(ClaimTypes.Name, credentials[0]) };
                        
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
                        
                        var identity = new ClaimsIdentity(claims, "Basic");
                        
                        var claimsPrincipal = new ClaimsPrincipal(identity);
                        return AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name));
                    }
                }
            }

            Response.StatusCode = 401;
            Response.Headers.Append("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
            return AuthenticateResult.Fail("Invalid Authorization Header");
        }
        return AuthenticateResult.Fail("Invalid Authorization Header"); 
    }
}