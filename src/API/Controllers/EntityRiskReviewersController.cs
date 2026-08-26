using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Appointing the business reviewers of an entity (Track 8 milestone 8.6.2).
///
/// Appointments are made from the desktop application by an entity administrator, never from the
/// portal. That separation is the design: the portal reads who is appointed and cannot grant itself
/// access.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class EntityRiskReviewersController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IEntityRiskReviewersService reviewers)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("ByEntity/{entityId}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<EntityRiskReviewer>))]
    public async Task<ActionResult<List<EntityRiskReviewer>>> GetByEntity(int entityId)
    {
        GetUser();
        return Ok(await reviewers.GetByEntityAsync(entityId));
    }

    /// <summary>The entities the caller is appointed to review — what the portal asks on sign-in.</summary>
    [HttpGet]
    [Route("Mine")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<int>))]
    public async Task<ActionResult<List<int>>> Mine()
    {
        var user = GetUser();
        return Ok(await reviewers.GetEntitiesForReviewerAsync(user.Value));
    }

    [HttpPost]
    [Route("")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(EntityRiskReviewer))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntityRiskReviewer>> Appoint([FromBody] ReviewerAppointment request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("An appointment is required.");

        try
        {
            var appointment = await reviewers.AppointAsync(request.EntityId, request.UserId,
                request.IsPrimary, user.Value);

            Logger.Information("User:{User} appointed user {Reviewer} to review entity {Entity}",
                user.Value, request.UserId, request.EntityId);

            return Created($"EntityRiskReviewers/{appointment.Id}", appointment);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error appointing a risk reviewer");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete]
    [Route("{id}")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Remove(int id)
    {
        var user = GetUser();

        try
        {
            await reviewers.RemoveAsync(id);
            Logger.Information("User:{User} removed risk-reviewer appointment {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }
}

/// <summary>Who is being appointed to review which entity.</summary>
public class ReviewerAppointment
{
    public int EntityId { get; set; }

    public int UserId { get; set; }

    /// <summary>The reviewer campaign notifications address first. At most one per entity.</summary>
    public bool IsPrimary { get; set; }
}
