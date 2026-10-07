using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.TailRisk;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Administration of the risk appetite thresholds (Track 8 milestone 8.3.3).
///
/// Admin-only, and deliberately so: the ceiling is what refuses an acceptance, so raising it is how
/// an organization makes a previously unacceptable risk acceptable. That is a governance decision,
/// not a triage one, and the audit trail records every change to these rows. The monetary tail
/// tolerances (Stage 9.7, S48 §6) are the same decision and inherit the same policy: the three
/// <c>TailLimits</c> actions carry no attribute of their own, which <c>TailRiskAuthorizationTest</c> pins.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireAdminOnly")]
[Route("[controller]")]
public class RiskAppetitesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskAppetitesService appetites,
    ITailRiskService tailRisk)
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

    /// <summary>The monetary tolerances (E[L], P95, CVaR95) of an appetite; 404 when it has none.</summary>
    [HttpGet]
    [Route("{id:int}/TailLimits")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAppetiteTailLimitsDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskAppetiteTailLimitsDto>> GetTailLimits(int id)
    {
        GetUser();

        try
        {
            return Ok(await tailRisk.GetAppetiteLimitsAsync(id));
        }
        catch (Exception ex)
        {
            return FailTailLimits(ex, $"reading the tail limits of risk appetite {id}");
        }
    }

    /// <summary>Declares or replaces the monetary tolerances of an appetite: at least one, each 0–10¹², and a rationale.</summary>
    [HttpPut]
    [Route("{id:int}/TailLimits")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAppetiteTailLimitsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskAppetiteTailLimitsDto>> SaveTailLimits(int id,
        [FromBody] RiskAppetiteTailLimitsRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await tailRisk.SaveAppetiteLimitsAsync(id, request ?? new RiskAppetiteTailLimitsRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return FailTailLimits(ex, $"saving the tail limits of risk appetite {id}");
        }
    }

    /// <summary>Removes the monetary tolerances of an appetite; 404 when it has none.</summary>
    [HttpDelete]
    [Route("{id:int}/TailLimits")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteTailLimits(int id)
    {
        var user = GetUser();

        try
        {
            await tailRisk.DeleteAppetiteLimitsAsync(id, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return FailTailLimits(ex, $"removing the tail limits of risk appetite {id}");
        }
    }

    /// <summary>The same mapping as <c>TailRiskController</c>: 400, 404, 422, and a logged 500 with no detail.</summary>
    private ActionResult FailTailLimits(Exception exception, string operation)
    {
        switch (exception)
        {
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case RuleBrokenException ex:
                return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
