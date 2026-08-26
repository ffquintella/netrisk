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
/// Administration of the risk appetite thresholds (Track 8 milestone 8.3.3).
///
/// Admin-only, and deliberately so: the ceiling is what refuses an acceptance, so raising it is how
/// an organization makes a previously unacceptable risk acceptable. That is a governance decision,
/// not a triage one, and the audit trail records every change to these rows.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireAdminOnly")]
[Route("[controller]")]
public class RiskAppetitesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskAppetitesService appetites)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>Every appetite: the organization-wide default first, then the entity overrides.</summary>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskAppetite>))]
    public async Task<ActionResult<List<RiskAppetite>>> GetAll()
    {
        GetUser();
        return Ok(await appetites.GetAllAsync());
    }

    /// <summary>
    /// The organization-wide appetite, or 204 when none is configured — which is the seeded state,
    /// and means nothing is gated. The admin screen renders that as an explicit "not configured"
    /// rather than as a permissive one, because the two are not the same thing.
    /// </summary>
    [HttpGet]
    [Route("Global")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAppetite))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<RiskAppetite>> GetGlobal()
    {
        GetUser();

        var global = await appetites.GetGlobalAsync();
        return global is null ? NoContent() : Ok(global);
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAppetite))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RiskAppetite>> Save([FromBody] RiskAppetite appetite)
    {
        var user = GetUser();

        if (appetite is null) return BadRequest("A risk appetite is required.");

        try
        {
            var saved = await appetites.SaveAsync(appetite, user.Value);

            Logger.Information(
                "User:{User} saved the risk appetite for entity {Entity}: ceiling {Ceiling}, dual " +
                "approval above {Dual}", user.Value, saved.EntityId?.ToString() ?? "(global)",
                saved.MaxAcceptableResidual, saved.DualApprovalThreshold);

            return Ok(saved);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataAlreadyExistsException ex)
        {
            return Conflict(new { error = "already_exists", ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error saving a risk appetite");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(int id)
    {
        var user = GetUser();

        try
        {
            await appetites.DeleteAsync(id);
            Logger.Warning("User:{User} deleted risk appetite {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error deleting risk appetite {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
