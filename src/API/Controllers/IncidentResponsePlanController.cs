using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("incident-response-plans")]
[ApiController]
[Route("[controller]")]
public class IncidentResponsePlanController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIncidentResponsePlansService incidentResponsePlansService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = incidentResponsePlansService;

    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlan>>> GetAllAsync()
    {

        var user = await GetUserAsync();

        try
        {
            var irps = await IncidentResponsePlansService.GetAllAsync();
            Logger.Information("User:{User} listed all incidentResponsePlans", user.Value);
            return Ok(irps);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing incidentResponsePlans: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlan>>> GetByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var irp = await IncidentResponsePlansService.GetByIdAsync(id);
            Logger.Information("User:{User} got incident response plan {id}", user.Value, id);
            return Ok(irp);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id}: {Message}",  id,ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Tasks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IncidentResponsePlan>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<IncidentResponsePlanTask>>> GetTasksByIdAsync(int id)
    {

        var user = await GetUserAsync();

        try
        {
            var irp = await IncidentResponsePlansService.GetByIdAsync(id, true);
            Logger.Information("User:{User} got incident response plan {id} tasks", user.Value, id);
            return Ok(irp.Tasks);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting got incident response plan {id} tasks: {Message}",  id,ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlan>> CreateAsync([FromBody] IncidentResponsePlan incidentResponsePlan)
    {
        var user = await GetUserAsync();

        try
        {
            var result = await IncidentResponsePlansService.CreateAsync(incidentResponsePlan, user);
            Logger.Information("User:{User} created incidentResponsePlan:{IncidentResponsePlan}", user.Value, result.Id);
            return Created($"IncidentResponsePlan/{result.Id}", result);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IncidentResponsePlan>> UpdateAsync(int id, [FromBody] IncidentResponsePlan incidentResponsePlan)
    {
        var user = await GetUserAsync();

        incidentResponsePlan.Id = id;
        
        try
        {
            var result = await IncidentResponsePlansService.UpdateAsync(incidentResponsePlan, user);
            Logger.Information("User:{User} updated incidentResponsePlan:{IncidentResponsePlan}", user.Value, result.Id);
            return Ok(result);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IncidentResponsePlan))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteAsync(int id)
    {
        var user = await GetUserAsync();
        
        try
        {
            await IncidentResponsePlansService.DeleteAsync(id, user);
            //Logger.Information("User:{User} deleted incidentResponsePlan:{id}", user.Value, id);
            return Ok();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating incidentResponsePlan: {Message} inner: {inner}", ex.Message, ex.InnerException!.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
}