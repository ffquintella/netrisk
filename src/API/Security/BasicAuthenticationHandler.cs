using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using ServerServices;
using static BCrypt.Net.BCrypt;
using System.Linq;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;


namespace API.Security;

public class BasicAuthenticationHandler: AuthenticationHandler<AuthenticationSchemeOptions>
{
    private SRDbContext? _dbContext = null;
    private IEnvironmentService _environmentService;
    private IUserManagementService _userManagementService;
    private IRoleManagementService _roleManagementService;
    private ILogger _log;
    
    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder, 
        ISystemClock clock,
        IEnvironmentService environmentService,
        IUserManagementService userManagementService,
        IRoleManagementService roleManagementService,
        DALManager dalManager) : base(options, logger, encoder, clock)
    {
        _dbContext = dalManager.GetContext();
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
        
        // Basic Authentication
        if (authHeader != null && authHeader.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring("Basic ".Length).Trim();
            //System.Console.WriteLine(token);
            var credentialstring = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var credentials = credentialstring.Split(':');
            
            if (credentials[0] != "" && credentials[1] != "")
            {
                /*var user = _dbContext?.Users?
                    .Where(u => u.Type == "simplerisk" && u.Enabled == true && u.Username == Encoding.UTF8.GetBytes(credentials[0]))
                    .FirstOrDefault();*/

                var user = _userManagementService.GetUser(credentials[0]);
                
                if (user != null)
                {
                    if (user.Lockout == 1)
                    {
                        return Task.FromResult(AuthenticateResult.Fail("User is locked out"));
                    }
                    
                    
                    // Check the password
                    var valid = _userManagementService.VerifyPassword(user.Value, credentials[1]);
                    //var valid = Verify(credentials[1], Encoding.UTF8.GetString(user.Password));
                    
                    if (valid)
                    {
                        var clientId = Request.Headers["ClientId"].ToString();
                        // Let´s check if we have the client registred... 
                        var client = _dbContext!.AddonsClientRegistrations!
                            .FirstOrDefault(cl => cl.ExternalId == clientId && cl.Status == "approved");

                        if (client == null) // We should not allow an unauthorized client to login
                        {
                            _log.Error("Unauthorized client {clientId}", clientId);
                            Response.StatusCode = 401;
                            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"sr-netextras.net\"");
                            return Task.FromResult(AuthenticateResult.Fail("Invalid Client"));                    
                        }
                        
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
                            var role = _roleManagementService.GetRole(user.RoleId);
                            claims = claims.Concat(new[] { new Claim(ClaimTypes.Role, role!.Name)}).ToArray(); 
                        }
                        
                        
                        _log.Information("User {0} authenticated using basic from client {1}", user.Name, client.Name);
                        var identity = new ClaimsIdentity(claims, "Basic");
                        
                        var claimsPrincipal = new ClaimsPrincipal(identity);
                        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
                    }
                }
            }

            Response.StatusCode = 401;
            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"simplerisk-netextras.net\"");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
        Response.StatusCode = 401;
        Response.Headers.Add("WWW-Authenticate", "Basic realm=\"simplerisk-netextras.net\"");
        return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header")); 
    }
}