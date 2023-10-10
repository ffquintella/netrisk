using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DAL;
using DAL.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
        ISystemClock clock,
        IEnvironmentService environmentService,
        IUsersService usersService,
        IRolesService rolesService,
        DALService dalService) : base(options, logger, encoder, clock)
    {
        _dbContext = dalService.GetContext();
        _environmentService = environmentService;
        _usersService = usersService;
        _rolesService = rolesService;
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

                var user = _usersService.GetUser(credentials[0]);
                
                if (user != null)
                {
                    if (user.Lockout == 1)
                    {
                        return Task.FromResult(AuthenticateResult.Fail("User is locked out"));
                    }
                    
                    
                    // Check the password
                    var valid = _usersService.VerifyPassword(user.Value, credentials[1]);
                    //var valid = Verify(credentials[1], Encoding.UTF8.GetString(user.Password));
                    
                    if (valid)
                    {
                        var clientId = Request.Headers["ClientId"].ToString();
                        // Let´s check if we have the client registred... 
                        var client = _dbContext!.ClientRegistrations!
                            .FirstOrDefault(cl => cl.ExternalId == clientId && cl.Status == "approved");

                        if (client == null) // We should not allow an unauthorized client to login
                        {
                            _log.Error("Unauthorized client {clientId}", clientId);
                            Response.StatusCode = 401;
                            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
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
                            var role = _rolesService.GetRole(user.RoleId);
                            claims = claims.Concat(new[] { new Claim(ClaimTypes.Role, role!.Name)}).ToArray(); 
                        }
                        
                        claims = claims.Concat(new[] {new Claim(ClaimTypes.Sid, user.Value.ToString())}).ToArray();
                        
                        _log.Information("User {0} authenticated using basic from client {1}", user.Name, client.Name);
                        var identity = new ClaimsIdentity(claims, "Basic");
                        
                        var claimsPrincipal = new ClaimsPrincipal(identity);
                        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
                    }
                }
            }

            Response.StatusCode = 401;
            Response.Headers.Add("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
        //Response.StatusCode = 401;
        //Response.Headers.Add("WWW-Authenticate", "Basic realm=\"netrisk.app\"");
        return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header")); 
    }
}