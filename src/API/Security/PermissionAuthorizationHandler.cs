using System.Security.Claims;
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
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Log as a warning so that it's very clear in sample output which authorization policies 
        // (and requirements/handlers) are in use
        _logger.LogWarning("Evaluating authorization requirement for permission = {age}", requirement.Permission);

       
        var hasAdminRole = context.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        
        var hasPermission = context.User.HasClaim(c => c.Type == "Permission" && c.Value == requirement.Permission);

        if (hasAdminRole)
        {
            _logger.LogInformation("User is admin so no permission is required");
            context.Succeed(requirement);
        }
        if (hasPermission)
        {
            _logger.LogInformation("User has te required permission: {Permission}", requirement.Permission);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogInformation("Use has not the required permission: {Permission}", requirement.Permission);
        }

        return Task.CompletedTask;
    } 
}

