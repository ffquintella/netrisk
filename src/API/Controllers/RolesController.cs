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
    public readonly IRolesService _rolesService;
    
    public RolesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IRolesService rolesService): base(logger, httpContextAccessor, usersService)
    {
        _rolesService = rolesService;
    }
    
    /// <summary>
    /// List all roles
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet]
    [Route("")]
    [Authorize(Policy = "RequireValidUser")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Role>> GetAll(int id)
    {
        
        try
        {
            var roles = _rolesService.GetRoles();
            return Ok(roles);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unexpected error listing roles messages: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
    }
}