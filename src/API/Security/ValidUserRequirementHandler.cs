using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.AspNetCore.Identity;

namespace API.Security;

public class ValidUserRequirementHandler: AuthorizationHandler<ValidUserRequirement>
{
    
    private SRDbContext? _dbContext = null;

    public ValidUserRequirementHandler(DALManager dalManager)
    {
        _dbContext = dalManager.GetContext();
    }
    
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ValidUserRequirement requirement)
    {
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
                user = _dbContext?.Users.Where(u => u.Type == "saml")
                    .FirstOrDefault<User>(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
                break;
            case UserType.Simplerisk:
                user = _dbContext?.Users.Where(u => u.Type == "simplerisk")
                    .FirstOrDefault<User>(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
                break;
            default:
                user = _dbContext?.Users.FirstOrDefault<User>(u => u.Username ==  Encoding.UTF8.GetBytes(userName));
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