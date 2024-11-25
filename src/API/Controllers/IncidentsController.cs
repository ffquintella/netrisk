using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("incident-response-plans")]
[ApiController]
[Route("[controller]")]
public class IncidentsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIncidentsService incidentsService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IIncidentsService IncidentsService { get; } = incidentsService;
    
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<Incident>>> GetAllAsync()
    {

        var user = await GetUserAsync();

        try
        {
            var incs = await IncidentsService.GetAllAsync();
            Logger.Information("User:{User} listed all incidents", user.Value);
            return Ok(incs);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing incidentes: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Incident>> CreateAsync([FromBody] Incident incident)
    {

        var user = await GetUserAsync();

        try
        {
            var inc = await IncidentsService.CreateAsync(incident, user);
            Logger.Information("User:{User} created new incident {id}", user.Value, inc.Id);
            return Created($"Incidents/{inc.Id}", inc);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating incident: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}