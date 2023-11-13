using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using ServerServices.Services;

namespace API.Security;

public class ValidUserRequirementHandler: AuthorizationHandler<ValidUserRequirement>
{
    
    //private readonly SRDbContext? _dbContext = null;
    private DALService _dalService;
    public ValidUserRequirementHandler(DALService dalService)
    {
        _dalService = dalService;
    }
    
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ValidUserRequirement requirement)
    {
        
        using var dbContext = _dalService.GetContext(false);
        
        var userClaimPrincipal = context.User;

        string? userName = userClaimPrincipal.Identities.FirstOrDefault()?.Name;

        if (userName == null)
        {
            context.Fail(new AuthorizationFailureReason(this, "User do not exists"));
            return Task.CompletedTask;
        }

        if (userName.Contains('@'))
        {
            userName = userName.Split('@')[0];
        }
        

        User? user;

        switch (requirement.UserType)
        {
            case UserType.SAML:
                user = dbContext?.Users.Where(u => u.Type == "saml")
                    .FirstOrDefault(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
                break;
            case UserType.Local:
                user = dbContext?.Users.Where(u => u.Type == "local" || u.Type == "simplerisk")
                    .FirstOrDefault(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
                break;
            default:
                user = dbContext?.Users.FirstOrDefault<User>(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
                break;
        }

        if (user != null)
        {
            context.Succeed(requirement);
            
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "User do not exists"));
        }
        
        return Task.CompletedTask;
        
        //throw new NotImplementedException();
    }
}