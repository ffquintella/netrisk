using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Monitoring;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Monitoring — MIGR-TI/IA Phase 7 (Stage 9.8, S49 §6): the key risk indicators with their tolerance and history, the
/// risks each governs, the mandatory reassessment triggers, and the methodology's metrics panel. Gate B by indicator is
/// read through <c>GET /Risks/{id}/Appetite</c> and enforced on acceptance, like the ceiling and the tail.
///
/// No new permission or policy (S49 D11), one attribute per action, pinned by <c>MonitoringAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads and the metrics panel — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>, the audience of the
/// register;</item>
/// <item>defining, changing or retiring a KRI — its tolerance is the Phase 0 limit Gate B enforces —
/// <c>[Authorize(Policy = "RequireAdminOnly")]</c>, as the appetite's tail tolerances are;</item>
/// <item>recording and voiding readings, linking risks and declaring events — <c>[Authorize(Policy =
/// "RequireSubmitRisk")]</c>, the audience that edits a risk.</item>
/// </list>
/// KRIs, risks, incidents and events outside the caller's entity scope are 404, the same as missing ones; a KRI written
/// outside it is the <c>EntityScopeViolationMiddleware</c>'s 403.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class MonitoringController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IMonitoringService monitoring,
    IMethodologyMetricsService metrics)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string DefinePolicy = "RequireAdminOnly";
    public const string WritePolicy = "RequireSubmitRisk";

    // --- KRIs --------------------------------------------------------------------------------------

    /// <summary>The KRIs the caller can see, with their state; retired ones only when asked.</summary>
    [HttpGet]
    [Route("Kris")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<KriDto>))]
    public async Task<ActionResult<List<KriDto>>> GetKris([FromQuery] bool includeRetired = false)
    {
        GetUser();

        try
        {
            return Ok(await monitoring.GetKrisAsync(includeRetired));
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing the KRIs");
        }
    }

    /// <summary>A KRI with its state, its readings and the risks it governs.</summary>
    [HttpGet]
    [Route("Kris/{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDetailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KriDetailDto>> GetKri(int id)
    {
        GetUser();

        try
        {
            return Ok(await monitoring.GetKriAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading KRI {id}");
        }
    }

    /// <summary>Defines a KRI: what it measures, its source, its tolerance with the decision that set it.</summary>
    [HttpPost]
    [Route("Kris")]
    [Authorize(Policy = DefinePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KriDto>> CreateKri([FromBody] KriRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.CreateKriAsync(request ?? new KriRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, "defining a KRI");
        }
    }

    /// <summary>Changes a KRI's definition and re-evaluates it.</summary>
    [HttpPut]
    [Route("Kris/{id:int}")]
    [Authorize(Policy = DefinePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KriDto>> UpdateKri(int id, [FromBody] KriRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.UpdateKriAsync(id, request ?? new KriRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"updating KRI {id}");
        }
    }

    /// <summary>Retires a KRI: it stops being evaluated and gating; its history stays.</summary>
    [HttpPost]
    [Route("Kris/{id:int}/Retire")]
    [Authorize(Policy = DefinePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KriDto>> RetireKri(int id)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.RetireKriAsync(id, user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"retiring KRI {id}");
        }
    }

    /// <summary>Records a reading and evaluates the KRI at once.</summary>
    [HttpPost]
    [Route("Kris/{id:int}/Readings")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KriDetailDto>> RecordReading(int id, [FromBody] KriReadingRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.RecordReadingAsync(id, request ?? new KriReadingRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"recording a reading of KRI {id}");
        }
    }

    /// <summary>Voids a reading with a reason; it stays in the history.</summary>
    [HttpPost]
    [Route("Kris/{id:int}/Readings/{readingId:int}/Void")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDetailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KriDetailDto>> VoidReading(int id, int readingId,
        [FromBody] KriReadingVoidRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.VoidReadingAsync(id, readingId, request ?? new KriReadingVoidRequest(),
                user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"voiding reading {readingId} of KRI {id}");
        }
    }

    /// <summary>Links a risk to a KRI (idempotent); a breach in progress triggers the risk's reassessment now.</summary>
    [HttpPut]
    [Route("Kris/{id:int}/Risks/{riskId:int}")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(KriDetailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<KriDetailDto>> LinkRisk(int id, int riskId)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.LinkRiskAsync(id, riskId, user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"linking risk {riskId} to KRI {id}");
        }
    }

    /// <summary>Unlinks a risk from a KRI — the KRI stops gating it.</summary>
    [HttpDelete]
    [Route("Kris/{id:int}/Risks/{riskId:int}")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnlinkRisk(int id, int riskId)
    {
        var user = GetUser();

        try
        {
            await monitoring.UnlinkRiskAsync(id, riskId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"unlinking risk {riskId} from KRI {id}");
        }
    }

    // --- reassessment triggers ---------------------------------------------------------------------

    /// <summary>The reassessment events the caller can see, newest first, with their visible triggers.</summary>
    [HttpGet]
    [Route("Reassessment/Events")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ReassessmentEventDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<ReassessmentEventDto>>> GetEvents([FromQuery] ReassessmentTriggerType? type,
        [FromQuery] int? limit)
    {
        GetUser();

        try
        {
            return Ok(await monitoring.GetEventsAsync(type, limit));
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing the reassessment events");
        }
    }

    /// <summary>Declares one of the Phase 7 trigger events and raises it on each open risk named.</summary>
    [HttpPost]
    [Route("Reassessment/Events")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReassessmentEventDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReassessmentEventDto>> DeclareEvent([FromBody] ReassessmentEventRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.DeclareEventAsync(request ?? new ReassessmentEventRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, "declaring a reassessment event");
        }
    }

    /// <summary>Applies a declared event to more risks; one it already triggered is not triggered again.</summary>
    [HttpPost]
    [Route("Reassessment/Events/{id:int}/Risks")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReassessmentEventDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReassessmentEventDto>> AddEventRisks(int id,
        [FromBody] ReassessmentRisksRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await monitoring.AddEventRisksAsync(id, request ?? new ReassessmentRisksRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"applying reassessment event {id} to more risks");
        }
    }

    /// <summary>The reassessment triggers the caller can see, optionally of one risk, optionally only pending ones.</summary>
    [HttpGet]
    [Route("Reassessment/Triggers")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ReassessmentTriggerDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ReassessmentTriggerDto>>> GetTriggers([FromQuery] int? riskId,
        [FromQuery] bool pendingOnly = false)
    {
        GetUser();

        try
        {
            return Ok(await monitoring.GetTriggersAsync(riskId, pendingOnly));
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing the reassessment triggers");
        }
    }

    // --- metrics -----------------------------------------------------------------------------------

    /// <summary>The methodology's metrics panel: the ten Phase 7 metrics and the health of KRIs and triggers.</summary>
    [HttpGet]
    [Route("Metrics")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MethodologyMetricsDto))]
    public async Task<ActionResult<MethodologyMetricsDto>> GetMetrics()
    {
        GetUser();

        try
        {
            return Ok(await metrics.GetAsync());
        }
        catch (Exception ex)
        {
            return Fail(ex, "computing the methodology metrics");
        }
    }

    /// <summary>
    /// The domain exceptions onto the status codes the other controllers use for them; a cross-entity write goes up to
    /// the <c>EntityScopeViolationMiddleware</c> (403); anything else is logged and answered 500 with no detail.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
    {
        switch (exception)
        {
            case DAL.Exceptions.EntityScopeViolationException:
                // Swallowed here it would be a 500; the middleware answers 403 (S42 §6 precedent). Rethrown with its
                // original stack trace.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
                return null!;
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case DataAlreadyExistsException ex:
                return Conflict(new { error = "already_exists", ex.Identification, ex.Message });
            case RuleBrokenException ex:
                return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
