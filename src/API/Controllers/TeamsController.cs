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
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There was a unexpected error listing team user ids: {Message}", ex.Message);
            return NotFound("Team not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error listing team user ids: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Team> GetBy(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got team:{TeamId} users", user.Value, id);
        

        try
        {
            var team = _teamsService.GetById(id);
            return Ok(team);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There was a unexpected error getting team: {Message}", ex.Message);
            return NotFound("Team not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error getting team: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
    
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpPut]
    [Route("{id}/UserIds")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<int>> GetTeamUsersIds(int id, [FromBody] List<int> userIds)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} updated team:{TeamId} users", user.Value, id);
        

        try
        {
            _teamsService.UpdateTeamUsers(id, userIds);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There was a unexpected error updating team user ids: {Message}", ex.Message);
            return NotFound("Team not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error updating team user ids: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
    
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<int>> GetTeamUsersIds([FromBody] Team team)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} created new team", user.Value);
        
        team.Value = 0;
        
        try
        {
            var newTeam = _teamsService.Create(team);
            return Created($"Teams/{newTeam.Value}", newTeam);
        }

        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error updating team user ids: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }
    
    [Authorize(Policy = "RequireAdminOnly")]
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Delete(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} deleted team:{TeamId} ", user.Value, id);
        

        try
        {
            _teamsService.Delete(id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There was a unexpected error deleting team: {Message}", ex.Message);
            return NotFound("Team not found");
        }
        catch (Exception ex)
        {
            Logger.Warning("There was a unexpected error deleting team: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        
        
    }

}