using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

public class AssignEntityRoleRequest
{
    public int EntityId { get; set; }
    public int RoleId { get; set; }
}

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class UserAccessController(
    IDalService dalService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IDalService DalService { get; } = dalService;

    [HttpGet]
    [Route("users/{userId}/entity-roles")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<UserEntityRole>))]
    public async Task<ActionResult<List<UserEntityRole>>> GetUserEntityRoles(int userId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} listed entity roles for user {UserId}", user.Value, userId);

        using var dbContext = DalService.GetContext();
        var assignments = await dbContext.UserEntityRoles
            .Include(a => a.Entity)
            .Include(a => a.Role)
            .Where(a => a.UserId == userId && a.RevokedAt == null)
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost]
    [Route("users/{userId}/entity-roles")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(UserEntityRole))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserEntityRole>> AssignEntityRole(int userId, [FromBody] AssignEntityRoleRequest request)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is assigning role {RoleId} in entity {EntityId} to user {UserId}", 
            user.Value, request.RoleId, request.EntityId, userId);

        using var dbContext = DalService.GetContext();

        // Check if assignment already exists
        var existing = await dbContext.UserEntityRoles
            .FirstOrDefaultAsync(a => a.UserId == userId && a.EntityId == request.EntityId && a.RoleId == request.RoleId && a.RevokedAt == null);

        if (existing != null)
        {
            return BadRequest("This role assignment already exists and is active");
        }

        var assignment = new UserEntityRole
        {
            UserId = userId,
            EntityId = request.EntityId,
            RoleId = request.RoleId,
            CreatedAt = DateTime.UtcNow,
            RevokedAt = null
        };

        dbContext.UserEntityRoles.Add(assignment);
        await dbContext.SaveChangesAsync();

        return Created($"UserAccess/user-entity-roles/{assignment.Id}", assignment);
    }

    [HttpDelete]
    [Route("user-entity-roles/{assignmentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeEntityRole(int assignmentId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} is revoking entity role assignment {AssignmentId}", user.Value, assignmentId);

        using var dbContext = DalService.GetContext();
        var assignment = await dbContext.UserEntityRoles.FindAsync(assignmentId);

        if (assignment == null)
        {
            return NotFound($"Role assignment with ID {assignmentId} not found");
        }

        // Soft-revoke by setting RevokedAt instead of hard deleting (required for audit history!)
        assignment.RevokedAt = DateTime.UtcNow;
        dbContext.UserEntityRoles.Update(assignment);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }
}
