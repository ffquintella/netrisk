using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FixRequestController: ApiBaseController
{
    private IFixRequestsService FixRequestsService { get; }
    
    public FixRequestController(ILogger logger, IHttpContextAccessor httpContextAccessor, IUsersService usersService, IFixRequestsService fixRequestsService) 
        : base(logger, httpContextAccessor, usersService)
    {
        FixRequestsService = fixRequestsService;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FixRequest>>> GetAll()
    {

        var user = GetUser();
        if(!user.Admin) return Unauthorized("Only admins can list all fix requests");

        try
        {
            Logger.Information("User:{User} listed all fix requests", user.Value);
            var requests = await FixRequestsService.GetAllFixRequestAsync();

            return Ok(requests);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing fix requests: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPost]
    [PermissionAuthorize("vulnerabilities_create")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FixRequest>>> Create([FromBody] FixRequest fixRequest)
    {

        var user = GetUser();
        //if(!user.Admin) return Unauthorized("Only admins can create fix requests");

        try
        {
            Logger.Information("User:{User} created a fix request", user.Value);
            var requests = await FixRequestsService.CreateFixRequestAsync(fixRequest);

            return Ok(requests);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating fix requests: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }

    
}