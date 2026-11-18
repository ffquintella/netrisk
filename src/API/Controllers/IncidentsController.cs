using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using Model.File;
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
    IIncidentsService incidentsService,
    IFilesService filesService,
    IAuditTrailService auditTrail)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IIncidentsService IncidentsService { get; } = incidentsService;
    private IFilesService FilesService { get; } = filesService;

    /// <summary>The most rows one history read returns, as for a host's.</summary>
    public const int MaxHistoryLimit = 5000;
    
    
    [HttpGet]
    [Route("")]
    [Authorize(Policy = "RequireValidFaceIdTransaction")]
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
    
    /// <summary>
    /// The field-by-field change history of one incident (M58, T279; S39 D-15). Served here, under the incident
    /// permission, rather than by /AuditTrail, whose risk-management policy is not the incident one. The incident is
    /// found through the scoped set first, so another entity's incident is a 404; rows go out without the user record.
    /// </summary>
    [HttpGet]
    [Route("{id}/History")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<AuditLog>>> GetHistoryAsync(int id, [FromQuery] int limit = 500)
    {
        var user = await GetUserAsync();

        if (limit is < 1 or > MaxHistoryLimit)
            return BadRequest($"limit must be between 1 and {MaxHistoryLimit}.");

        try
        {
            await IncidentsService.GetByIdAsync(id);

            var rows = await auditTrail.GetForRecordAsync(nameof(Incident), id, limit);
            Logger.Information("User:{User} read the change history of incident {Id}", user.Value, id);

            return Ok(rows.Select(WithoutUser).ToList());
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while reading incident {Id} history: {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static AuditLog WithoutUser(AuditLog row) => new()
    {
        Id = row.Id,
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        Field = row.Field,
        OldValue = row.OldValue,
        NewValue = row.NewValue,
        Action = row.Action,
        UserId = row.UserId,
        Actor = row.Actor,
        OccurredAt = row.OccurredAt,
        CorrelationId = row.CorrelationId
    };

    [HttpGet]
    [Route("{id}/Attachments")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FileListing>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<FileListing>>> GetAttachmentsByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            //var inc = await IncidentsService.GetAttachmentsByIdAsync(id);
            var inc = await FilesService.GetObjectFileListingsAsync(id, FileCollectionType.IncidentFile);
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
    
    [HttpGet]
    [Route("{id}/IncidentResponsePlans")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<int>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<int>>> GetIncidentResponsePlansIdsByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var ids = await IncidentsService.GetIncidentResponsPlanIdsByIdAsync(id);
            
            Logger.Information("User:{User} got the list of incident response plans associated to the incident: {id}", user.Value, id);
            return Ok(ids);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting the list of incident response plans associated to the incident: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("{id}/IncidentResponsePlans")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<int>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> AssociateIncidentResponsePlansIdsByIdAsync(int id, [FromBody] List<int> irpIds)
    {

        var user = await GetUserAsync();

        try
        {
            await IncidentsService.AssociateIncidentResponsPlanIdsByIdAsync(id, irpIds, user);
            
            Logger.Information("User:{User} associated incident response plans to a incident: {id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} or IncidentResponsePlan not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while associating incident response plans to a incident: {Message}", ex.Message);
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
        catch (InvalidParameterException ex)
        {
            // Stage 9.2 (S42 §6): an undefined incident kind is the caller's error, not a 500.
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
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
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
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
    
    
    /// <summary>
    /// Appends one dated, text-only entry to the incident's timeline (S55, T354). Authorised exactly as updating the
    /// incident (the class's <c>incident_management</c>); the server stamps the time and the author.
    /// </summary>
    [HttpPost]
    [Route("{id}/Timeline")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IncidentTimelineEntryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentTimelineEntryDto>> AddTimelineEntryAsync(int id,
        [FromBody] NewIncidentTimelineEntryDto? entry)
    {
        var user = await GetUserAsync();

        try
        {
            var added = await IncidentsService.AddTimelineEntryAsync(id, entry?.Text, user);
            Logger.Information("User:{User} added timeline entry {EntryId} to incident {Id}", user.Value, added.Id, id);
            return Created($"Incidents/{id}/Timeline/{added.Id}", added);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("Incident {id} not found", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while adding a timeline entry to an incident: {Message}", ex.Message);
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