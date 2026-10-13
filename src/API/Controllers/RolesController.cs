using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DecisionCycle;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireAdminOnly")]
[ApiController]
[Route("[controller]")]
public class RolesController: ApiBaseController
{
    private IRolesService RolesService { get; }

    public RolesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IRolesService rolesService): base(logger, httpContextAccessor, usersService)
    {
        RolesService = rolesService;
    }
    
    [HttpGet]
    [Route("")]
    [Authorize(Policy = "RequireValidUser")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Role>> GetAll()
    {
        var user = GetUser();
        Logger.Debug("User:{UserValue} listed all roles", user.Value);

        try
        {
            var roles = RolesService.GetRoles();
            return Ok(roles);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error listing roles messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
    
    [HttpGet]
    [Route("{roleId}")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Role> GetRole(int roleId)
    {
        var user = GetUser();
        Logger.Debug("User:{UserValue} listed role:{RoleId}", user.Value, roleId);

        
        if(roleId < 1)
            return BadRequest("Invalid role id");
        
        try
        {
            var role = RolesService.GetRole(roleId);
            return Ok(role);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error listing roles messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
    
    [HttpDelete]
    [Route("{roleId}")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Role> DeleteRole(int roleId)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} deleted role:{RoleId}", user.Value, roleId);
        
        if(roleId < 1)
            return BadRequest("Invalid role id");
        
        try
        {
            RolesService.DeleteRole(roleId);
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error deleting role messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
    
    [HttpPost]
    [Route("")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Role> CreateRole([FromBody] Role role)
    {
        try
        {
            var newRole = RolesService.CreateRole(role);
            
            var user = GetUser();
            Logger.Information("User:{UserValue} created a new role: {RoleId}", user.Value, newRole!.Value);
            
            return Created("/Roles/" + newRole.Value, newRole);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error creating new role messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
    
    [HttpGet]
    [Route("{roleId}/Permissions")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<string>> GetRolePermissions(int roleId)
    {
        
        var user = GetUser();
        Logger.Debug("User:{UserValue} listed role:{RoleId} permissions", user.Value, roleId);

        if(roleId < 1)
            return BadRequest("Invalid role id");
        
        try
        {
            var permissions = RolesService.GetRolePermissions(roleId);
            return Ok(permissions);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error listing roles messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
    
    [HttpPut]
    [Route("{roleId}/Permissions")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> UpdateRolePermissions(int roleId, [FromBody] List<string> permissions)
    {
        if(roleId < 1)
            return BadRequest("Invalid role id");

        try
        {
            var user = await GetUserAsync();

            // S50 R10: the third-line marker on the role the caller holds puts the caller in the third line, whose
            // read-only rule then refuses every write — the one that would take the marker off the role again included.
            if (user.RoleId == roleId && permissions.Contains(ThirdLineAssurance.PermissionKey))
            {
                Logger.Warning("Refused the third-line marker on role {RoleId} to user {UserValue}, who holds that role",
                    roleId, user.Value);
                return UnprocessableEntity(new
                {
                    error = ThirdLineAssurance.SelfAppointmentRule,
                    message = "You cannot give the third-line marker to the role you hold: your own account would " +
                              "become read-only and could not undo it. Another administrator has to grant it."
                });
            }

            await RolesService.UpdatePermissionsAsync(roleId, permissions);
            
            Logger.Information("User:{UserValue} updated role:{RoleId} permissions", user.Value, roleId);

            
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Role not found while updating roles permissions messages: {Message}", ex.Message);
            return NotFound("Role not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error updating roles permissions messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }

    }
}