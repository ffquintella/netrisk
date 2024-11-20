﻿using API.Security;
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
    
}