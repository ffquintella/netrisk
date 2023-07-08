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
public class TeamsController: ApiBaseController
{

    private ITeamManagementService _teamManagementService;

    public TeamsController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUserManagementService userManagementService,
        ITeamManagementService teamManagementService
        ) : base(logger, httpContextAccessor, userManagementService)
    {
        _teamManagementService = teamManagementService;
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Team>> GetAll()
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} listed all teams", user.Value);
        

        try
        {
            var teams = _teamManagementService.GetAll();
            return Ok(teams);
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error listing teams message: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
}