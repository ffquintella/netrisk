using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.DecisionCycle;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The archive — MIGR-TI/IA Phase 4's fourth decision (Stage 9.9, S50 §6): a risk archived with a justification, the
/// Phase 7 triggers that reopen it, and a quarterly review. The reopening itself is not an endpoint: a reassessment event
/// of a watched type that reaches the archived risk reopens it (<c>/Monitoring/Reassessment/Events</c>, KRI breaches).
///
/// No new permission (S50 D11), one attribute per action, pinned by <c>DecisionCycleAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>, the audience of the register;</item>
/// <item>archiving and reopening — <c>[Authorize(Policy = "RequireCloseRisk")]</c>: archiving closes the risk, and the
/// legacy reopen route has the same audience;</item>
/// <item>the quarterly review — <c>[Authorize(Policy = "RequireMgmtReviewAccess")]</c>, the audience that reviews risks.</item>
/// </list>
/// Errors are those of <see cref="IRiskArchiveService"/>; a risk outside the caller's scope is 404.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class RiskArchiveController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskArchiveService archives)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string ArchivePolicy = "RequireCloseRisk";
    public const string ReviewPolicy = "RequireMgmtReviewAccess";

    /// <summary>The live archives the caller can see — or every archive, or only those due for their quarterly review.</summary>
    [HttpGet]
    [Route("Risks")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskArchiveDto>))]
    public async Task<ActionResult<List<RiskArchiveDto>>> GetArchives([FromQuery] bool dueOnly = false,
        [FromQuery] bool includeEnded = false)
    {
        GetUser();

        try
        {
            return Ok(await archives.GetArchivesAsync(dueOnly, includeEnded));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the archives");
        }
    }

    /// <summary>Every archive of one risk, newest first.</summary>
    [HttpGet]
    [Route("Risks/{riskId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskArchiveDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RiskArchiveDto>>> GetRiskArchives(int riskId)
    {
        GetUser();

        try
        {
            return Ok(await archives.GetRiskArchivesAsync(riskId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the archives of risk {riskId}");
        }
    }

    /// <summary>Archives an open risk with a justification, its reopening conditions and a quarterly review.</summary>
    [HttpPost]
    [Route("Risks/{riskId:int}")]
    [Authorize(Policy = ArchivePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskArchiveDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskArchiveDto>> Archive(int riskId, [FromBody] RiskArchiveRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await archives.ArchiveAsync(riskId, request ?? new RiskArchiveRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"archiving risk {riskId}");
        }
    }

    /// <summary>Reopens the risk's live archive by hand, with a reason.</summary>
    [HttpPost]
    [Route("Risks/{riskId:int}/Reopen")]
    [Authorize(Policy = ArchivePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskArchiveDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskArchiveDto>> Reopen(int riskId, [FromBody] RiskArchiveReopenRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await archives.ReopenAsync(riskId, request ?? new RiskArchiveReopenRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reopening the archive of risk {riskId}");
        }
    }

    /// <summary>The quarterly review of the risk's live archive: keep it, or reopen it.</summary>
    [HttpPost]
    [Route("Risks/{riskId:int}/Reviews")]
    [Authorize(Policy = ReviewPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskArchiveDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskArchiveDto>> Review(int riskId, [FromBody] RiskArchiveReviewRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await archives.ReviewAsync(riskId, request ?? new RiskArchiveReviewRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reviewing the archive of risk {riskId}");
        }
    }
}
