using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Formal, expiring risk acceptances (Track 3 milestone 3.2.3).
///
/// Guarded by the vulnerability permissions rather than a new one: accepting a finding is a decision
/// about the vulnerability register, and inventing a separate permission would mean every existing
/// deployment's triage roles silently lose the ability to do it.
/// </summary>
[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class RiskAcceptancesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IFindingLifecycleService lifecycleService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// Acceptances, newest expiry first. <c>expiringWithinDays</c> is the filter the management view
    /// leads with — the spec's "expiring within 30 days".
    /// </summary>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskAcceptance>))]
    public async Task<ActionResult<List<RiskAcceptance>>> GetAll([FromQuery] int? expiringWithinDays = null)
    {
        var user = GetUser();

        try
        {
            var acceptances = await lifecycleService.GetAcceptancesAsync(expiringWithinDays);
            Logger.Information("User:{User} listed risk acceptances (expiring within {Days})", user.Value,
                expiringWithinDays);
            return Ok(acceptances);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing risk acceptances");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskAcceptance>> GetById(int id)
    {
        GetUser();

        try
        {
            return Ok(await lifecycleService.GetAcceptanceAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading risk acceptance {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Creates an acceptance and suppresses the findings it covers, recording an event on each.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> Create([FromBody] RiskAcceptanceCreationRequest request)
    {
        var user = GetUser();

        if (request?.Acceptance == null) return BadRequest("A risk acceptance is required.");

        try
        {
            var created = await lifecycleService.CreateAcceptanceAsync(request.Acceptance,
                request.FindingIds ?? [], user.Value);

            Logger.Information("User:{User} created risk acceptance {Id} covering {Count} findings",
                user.Value, created.Id, request.FindingIds?.Count ?? 0);

            return Created($"RiskAcceptances/{created.Id}", created);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException ex)
        {
            return NotFound(ex.InnerException?.Message ?? ex.Message);
        }
        catch (InvalidStateTransitionException ex)
        {
            // A finding that cannot legally be accepted is reported rather than quietly skipped: an
            // acceptance that covers less than the operator believes is worse than one that fails.
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error creating a risk acceptance");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> Update(int id, [FromBody] RiskAcceptance acceptance)
    {
        var user = GetUser();

        if (acceptance == null) return BadRequest("A risk acceptance is required.");
        if (acceptance.Id != id) return BadRequest("Id mismatch.");

        try
        {
            return Ok(await lifecycleService.UpdateAcceptanceAsync(acceptance, user.Value));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error updating risk acceptance {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Brings further findings under a live acceptance.</summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("{id}/findings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> AddFindings(int id, [FromBody] List<int> findingIds)
    {
        var user = GetUser();

        if (findingIds == null || findingIds.Count == 0) return BadRequest("At least one finding id is required.");

        try
        {
            var updated = await lifecycleService.AddFindingsToAcceptanceAsync(id, findingIds, user.Value);
            Logger.Information("User:{User} added {Count} findings to risk acceptance {Id}",
                user.Value, findingIds.Count, id);
            return Ok(updated);
        }
        catch (DataNotFoundException ex)
        {
            return NotFound(ex.InnerException?.Message ?? ex.Message);
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error adding findings to risk acceptance {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Withdraws an acceptance and reactivates everything it covered. The reason is mandatory —
    /// revoking is as consequential as accepting.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("{id}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> Revoke(int id, [FromBody] RevocationRequest request)
    {
        var user = GetUser();

        try
        {
            var revoked = await lifecycleService.RevokeAcceptanceAsync(id, request?.Reason ?? string.Empty,
                user.Value);

            Logger.Information("User:{User} revoked risk acceptance {Id}", user.Value, id);
            return Ok(revoked);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error revoking risk acceptance {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>An acceptance plus the findings it should cover, so both land in one request.</summary>
public class RiskAcceptanceCreationRequest
{
    public RiskAcceptance? Acceptance { get; set; }

    public List<int>? FindingIds { get; set; }
}

/// <summary>The mandatory reason for a revocation.</summary>
public class RevocationRequest
{
    public string? Reason { get; set; }
}
