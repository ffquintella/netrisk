using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("incident_management")]
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
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
    
    [HttpGet]
    [Route("NextSequence")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<int>> GetNextSequenceAsync([FromQuery] int year = -1)
    {

        var user = await GetUserAsync();

        try
        {
            var seq = await IncidentsService.GetNextSequenceAsync(year);
            Logger.Information("User:{User} get next incident sequence", user.Value);
            return Ok(seq);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting next incident sequence: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Incident>> GetByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var inc = await IncidentsService.GetByIdAsync(id);
            Logger.Information("User:{User} got one incident {id}", user.Value, id);
            return Ok(inc);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting incident: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Attachments")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FileListing>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FileListing>>> GetAttachmentsByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var inc = await IncidentsService.GetAttachmentsByIdAsync(id);
            Logger.Information("User:{User} got one incident {id} attachments", user.Value, id);
            return Ok(inc);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting incident attachments: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
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
    
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Incident>> UpdateAsync(int id, [FromBody] Incident incident)
    {

        var user = await GetUserAsync();
        incident.Id = id;

        try
        {
            var updated = await IncidentsService.UpdateAsync(incident, user);
            Logger.Information("User:{User} updated a incident {id}", user.Value, id);
            return Ok(updated);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating a incident: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Incident>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            await IncidentsService.DeleteByIdAsync(id);
            Logger.Information("User:{User} deleted a incident {id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting a incident: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}