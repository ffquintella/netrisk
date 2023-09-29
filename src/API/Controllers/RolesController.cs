using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            return Created("/Roles/" + newRole!.Value, newRole);
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
    public ActionResult UpdateRolePermissions(int roleId, [FromBody] List<string> permissions)
    {
        if(roleId < 1)
            return BadRequest("Invalid role id");

        try
        {
            RolesService.UpdatePermissions(roleId, permissions);
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