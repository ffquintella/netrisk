﻿using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace API.Security;

public class PermissionAuthorizationHandler: AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    // Check whether a given MinimumAgeRequirement is satisfied or not for a particular context
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {

        var hasAdminRole = false;
        var hasPermission = false;
        
        await Task.Run(() =>
        {
            _logger.LogDebug("Evaluating authorization requirement for permission = {Permission}", requirement.Permission);
       
            hasAdminRole = context.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        
            hasPermission = context.User.HasClaim(c => c.Type == "Permission" && c.Value == requirement.Permission);
        });

        if (hasAdminRole)
        {
            _logger.LogDebug("User is admin so no permission is required");
            context.Succeed(requirement);
        }
        if (hasPermission)
        {
            _logger.LogDebug("User has te required permission: {Permission}", requirement.Permission);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation("Use has not the required permission: {Permission}", requirement.Permission);
        }

        return ;
    } 
}

