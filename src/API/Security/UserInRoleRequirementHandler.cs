﻿using System.Text;
using System.Threading.Tasks;
using DAL;
using DAL.Context;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Model.Exceptions;
using System.Linq;
using ServerServices.Services;

namespace API.Security;

public class UserInRoleRequirementHandler: AuthorizationHandler<UserInRoleRequirement>
{
    private NRDbContext? _dbContext = null;

    public UserInRoleRequirementHandler(IDalService dalService)
    {
        _dbContext = dalService.GetContext();
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        UserInRoleRequirement requirement)
    {
        var userClaimPrincipal = context.User;
        
        string? userName = userClaimPrincipal.Identities.FirstOrDefault()?.Name;
        if (userName == null)
        {
            context.Fail(new AuthorizationFailureReason(this, "User do not exists"));
            return Task.CompletedTask;
        }

        if (userName.Contains('@')) userName = userName.Split('@')[0];
        
        var user = _dbContext?.Users
            .FirstOrDefault<User>(u => u.Login.ToLower() ==  userName.ToLower());
        
        if (user != null)
        {

            var role = _dbContext?.Roles.FirstOrDefault(r => r.Name == requirement.Role);

            if (role == null) throw new RoleNotFoundException();
                
            if (user.RoleId == role.Value)
            {
                context.Succeed(requirement); 
            }
            else
            {
                context.Fail(new AuthorizationFailureReason(this, "User is not in role"));
            }
            
            
            
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "User do not exists"));
        }
        
        
        return Task.CompletedTask;
    }

}