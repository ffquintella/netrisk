using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireMitigation")]
[ApiController]
[Route("[controller]")]
public class MitigationsController: ApiBaseController
{
    #region FIELDS
    private IRisksService _risks;
    private readonly IMitigationsService _mitigations;
    private ITeamsService _teams;
    #endregion
    
    public MitigationsController(
        ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IMitigationsService mitigationsService,
        ITeamsService teamsService,
        IRisksService risks) : base(logger, httpContextAccessor, usersService)
    {
        _risks = risks;
        _mitigations = mitigationsService;
        _teams = teamsService;
    }
    
    #region METHODS
    /// <summary>
    /// Creates a mitigation
    /// </summary>
    /// <param name="mitigation"></param>
    /// <returns></returns>
    [HttpPost]
    [Authorize(Policy = "RequirePlanMitigations")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Mitigation))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<Mitigation> Create([FromBody]Mitigation mitigation)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} creating mitigation", user.Value);
        
        try
        {
            var createdMitigation = _mitigations.Create(mitigation);
            Logger.Information("User:{UserValue} created mitigation with id={Id}", user.Value, createdMitigation.Id);
            return CreatedAtAction(nameof(GetById), new {id = createdMitigation.Id}, createdMitigation);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The risk with id: {Id} does not exists: {ExMessage}", mitigation.RiskId, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error creating mitigation: {ExMessage}", ex.Message);
            return this.BadRequest();
        }
    }
    
    /// <summary>
    /// Gets a Mitigation by it´s Id
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <returns></returns>
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Mitigation))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Mitigation> GetById(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got mitigation with id={Id}", user.Value, id);

        Mitigation mitigation;
        
        try
        {
            mitigation = _mitigations.GetById(id);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The mitigation with id: {Id} does not exists: {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting mitigation: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(mitigation);

    }


    /// <summary>
    /// Updates an existing mitigation
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <param name="mitigation">Mitigation object</param>
    /// <returns></returns>
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Mitigation))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult UpdateById(int id, [FromBody] Mitigation mitigation)
    {
        var user = GetUser();
        //Logger.Information("User:{UserValue} updating mitigation: {Id} ", user.Value, id);
        
        // To avoid inconsistencies
        mitigation.Id = id;

        try
        {
            _mitigations.Save(mitigation);
            Logger.Information("User:{UserValue} updated mitigation: {Id} ", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The mitigation with id: {Id} does not exists: {ExMessage}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error updating mitigation: {Message}", ex.Message);
            return StatusCode(500, ex.Message);
        }

    }

    /// <summary>
    /// Gets mitigation teams by mitigation Ids
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <returns>List of Mitigation Teams</returns>
    [HttpGet]
    [Route("{id}/Teams")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Team>> GetTeamsById(int id)
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} got mitigation´s {Id} teams", user.Value, id);

        List<Team> teams;
        
        try
        {
            teams = _teams.GetByMitigationId(id);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There is no teams with mitigation: {Id} : {ExMessage}", id, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting mitigation´s teams: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(teams);

    }

    [HttpGet]
    [Authorize(Policy = "RequirePlanMitigations")]
    [Route("{id}/Teams/Associate/{teamId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult AssociateTeamToMitigation(int id, int teamId)
    {
        var user = GetUser();

        Logger.Information("User:{UserValue} associated team {TeamId} to mitigation {MitigationId}", user.Value, teamId, id);
        
        try
        {
            _teams.AssociateTeamToMitigation(id, teamId);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There is no teams or mitigation with defined ids: {MitigationId} - {TeamId}: {ExMessage}", 
                id, teamId, ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error associating mitigation to teams: {Message}", ex.Message);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Deletes all teams associations to this mitigation
    /// </summary>
    /// <param name="id">Mitigation Id</param>
    /// <returns></returns>
    [HttpDelete]
    [Authorize(Policy = "RequirePlanMitigations")]
    [Route("{id}/Teams")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Team>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult AssociateTeamToMitigation(int id)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} deleted teams association to mitigation {MitigationId}", user.Value,  id);
        
        try
        {
            _mitigations.DeleteTeamsAssociations(id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("There is no mitigation with defined ids: {MitigationId} : {ExMessage}", 
                id,  ex.Message);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error deleting mitigation´s teams: {Message}", ex.Message);
            return StatusCode(500);
        }

    }

    /// <summary>
    /// List mitigation strategies
    /// </summary>
    /// <returns>List of mitigation strategies </returns>
    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("Strategies")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<PlanningStrategy>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<PlanningStrategy>> ListMitigationStrategies()
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} listed mitigation strategies", user.Value);

        List<PlanningStrategy> strategies;
        
        try
        {
            strategies = _mitigations.ListStrategies();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting strategies: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(strategies);

    }
    
    /// <summary>
    /// List mitigation efforts
    /// </summary>
    /// <returns>List of mitigation efforts</returns>
    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("Efforts")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MitigationEffort>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<MitigationEffort>> ListMitigationEffort()
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} listed mitigation efforts", user.Value);

        List<MitigationEffort> efforts;
        
        try
        {
            efforts = _mitigations.ListEfforts();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting strategies: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(efforts);

    }
    
    /// <summary>
    /// Lists Mitigation costs
    /// </summary>
    /// <returns>List of mitigation costs</returns>
    [HttpGet]
    [Authorize(Policy = "RequireValidUser")]
    [Route("Costs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MitigationCost>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<MitigationCost>> ListMitigationCosts()
    {

        var user = GetUser();

        Logger.Information("User:{UserValue} listed mitigation costs", user.Value);

        List<MitigationCost> costs;
        
        try
        {
            costs = _mitigations.ListCosts();
        }
        catch (Exception ex)
        {
            Logger.Error("Internal error getting costs: {Message}", ex.Message);
            return StatusCode(500);
        }

        return Ok(costs);

    }
    #endregion
}