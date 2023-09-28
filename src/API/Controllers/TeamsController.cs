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

    private ITeamsService _teamsService;

    public TeamsController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        ITeamsService teamsService
        ) : base(logger, httpContextAccessor, usersService)
    {
        _teamsService = teamsService;
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
            var teams = _teamsService.GetAll();
            return Ok(teams);
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error listing teams message: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
    
    [HttpGet]
    [Route("{id}/UserIds")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<int>> GetTeamUsersIds(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got team:{TeamId} users", user.Value, id);
        

        try
        {
            var userIds = _teamsService.GetUsersIds(id);
            return Ok(userIds);
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error listing team user ids: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
}