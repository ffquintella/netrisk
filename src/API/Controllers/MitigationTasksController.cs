using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// POA&amp;M-style treatment task line items (Track 8 milestone 8.5.3).
///
/// Gated on <c>RequirePlanMitigations</c> rather than on risk management generally: creating a task
/// is planning treatment, and the permission that already means that is the right one to reuse.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class MitigationTasksController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IMitigationTasksService tasks)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("ByMitigation/{mitigationId}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MitigationTask>))]
    public async Task<ActionResult<List<MitigationTask>>> GetByMitigation(int mitigationId)
    {
        GetUser();
        return Ok(await tasks.GetByMitigationAsync(mitigationId));
    }

    [HttpGet]
    [Route("{id}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MitigationTask))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MitigationTask>> Get(int id)
    {
        GetUser();

        try
        {
            return Ok(await tasks.GetAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Tasks due or overdue, for the treatment dashboard and the notification job.</summary>
    [HttpGet]
    [Route("Due")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MitigationTask>))]
    public async Task<ActionResult<List<MitigationTask>>> GetDue([FromQuery] int withinDays = 0)
    {
        GetUser();
        return Ok(await tasks.GetDueOrOverdueAsync(DateTime.UtcNow, withinDays));
    }

    [HttpPost]
    [Route("")]
    [Authorize(Policy = "RequirePlanMitigations")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(MitigationTask))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MitigationTask>> Create([FromBody] MitigationTaskRequest request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("A task is required.");

        try
        {
            var created = await tasks.CreateAsync(request, user.Value);
            return Created($"MitigationTasks/{created.Id}", created);
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
            Logger.Error(ex, "Unknown error creating a mitigation task");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut]
    [Route("{id}")]
    [Authorize(Policy = "RequirePlanMitigations")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MitigationTask))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MitigationTask>> Update(int id,
        [FromBody] MitigationTaskRequest request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("A task is required.");
        if (request.Id != id) return BadRequest("Id mismatch.");

        try
        {
            return Ok(await tasks.UpdateAsync(request, user.Value));
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
            Logger.Error(ex, "Unknown error updating mitigation task {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete]
    [Route("{id}")]
    [Authorize(Policy = "RequirePlanMitigations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(int id)
    {
        GetUser();

        try
        {
            await tasks.DeleteAsync(id);
            return Ok();
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }
}
