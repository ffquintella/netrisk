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
        
        //var files = new List<FileListing>();

        try
        {
            Logger.Information("User:{User} listed all fix requests", user.Value);
            var requests = await FixRequestsService.GetAllFixRequestAsync();
            
            //var files = _filesService.GetAll();

            return Ok(requests);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing fix requests: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
}