using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Risk-scoped governance endpoints (Track 8).
///
/// A controller of its own rather than another two hundred lines on <c>RisksController</c>, but
/// deliberately on the same <c>Risks</c> route prefix: an acceptance of a risk is a sub-resource of
/// that risk, and putting it at <c>/RiskAcceptances?riskId=</c> would have made the authority checks
/// read as an afterthought rather than as part of the risk's own surface.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("Risks")]
public class RiskGovernanceController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRisksService risksService,
    IRiskAcceptancesService acceptances,
    IRiskWorkflowService workflow,
    IMgmtReviewsService mgmtReviews,
    IAuditTrailService auditTrail,
    IMitigationTasksService mitigationTasks,
    IQuantitativeRiskService quantitative)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    // --- 8.1 acceptance ------------------------------------------------------------------------

    /// <summary>Every acceptance recorded against a risk, newest first.</summary>
    [HttpGet]
    [Route("{id}/Acceptances")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskAcceptance>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RiskAcceptance>>> GetAcceptances(int id)
    {
        GetUser();

        try
        {
            return Ok(await acceptances.GetByRiskAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing acceptances of risk {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The acceptance in force, if any. Null when the risk is not accepted.</summary>
    [HttpGet]
    [Route("{id}/Acceptances/Active")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<RiskAcceptance>> GetActiveAcceptance(int id)
    {
        GetUser();

        var active = await acceptances.GetActiveAsync(id);
        return active is null ? NoContent() : Ok(active);
    }

    /// <summary>
    /// Records a formal acceptance. Gated on the management-review permissions plus the band check,
    /// segregation of duties and the appetite ceiling — all inside the service, so the same rules
    /// apply from the portal.
    /// </summary>
    [HttpPost]
    [Route("{id}/Acceptances")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> CreateAcceptance(int id,
        [FromBody] RiskAcceptanceRequest request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("An acceptance request is required.");

        try
        {
            var created = await acceptances.CreateAsync(id, request, user.Value);

            Logger.Information("User:{User} accepted risk {Id} until {Expiry:yyyy-MM-dd}", user.Value, id,
                created.ExpiresAt);

            return Created($"Risks/{id}/Acceptances/{created.Id}", created);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataAlreadyExistsException ex)
        {
            return Conflict(new { error = "already_accepted", ex.Message });
        }
        catch (RuleBrokenException ex)
        {
            // 422 rather than 403: the caller is allowed to accept risks, and it is this risk's
            // score or their relationship to it that makes this request impossible.
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error accepting risk {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Renews an acceptance as a new row linked to its predecessor.</summary>
    [HttpPost]
    [Route("{id}/Acceptances/{acceptanceId}/Renew")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> RenewAcceptance(int id, int acceptanceId,
        [FromBody] RiskAcceptanceRequest request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("A renewal request is required.");

        try
        {
            return Ok(await acceptances.RenewAsync(acceptanceId, request, user.Value));
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (RuleBrokenException ex)
        {
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error renewing acceptance {Id}", acceptanceId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Withdraws an acceptance. The reason is mandatory.</summary>
    [HttpPut]
    [Route("{id}/Acceptances/{acceptanceId}/Revoke")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAcceptance))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAcceptance>> RevokeAcceptance(int id, int acceptanceId,
        [FromBody] RiskAcceptanceRevocation request)
    {
        var user = GetUser();

        try
        {
            return Ok(await acceptances.RevokeAsync(acceptanceId, request?.Reason ?? string.Empty,
                user.Value));
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error revoking acceptance {Id}", acceptanceId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 8.3 appetite and counter-signature ----------------------------------------------------

    /// <summary>What the appetite in force says about this risk, and why.</summary>
    [HttpGet]
    [Route("{id}/Appetite")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AppetiteEvaluation))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppetiteEvaluation>> GetAppetite(int id)
    {
        GetUser();

        try
        {
            return Ok(await workflow.EvaluateAppetiteAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Open risks above the appetite in force, per entity — the dashboard count (8.3.3).</summary>
    [HttpGet]
    [Route("AboveAppetite")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AppetiteBreachCount>))]
    public async Task<ActionResult<List<AppetiteBreachCount>>> GetAboveAppetite()
    {
        GetUser();
        return Ok(await workflow.CountRisksAboveAppetiteAsync());
    }

    /// <summary>
    /// Stored risks whose state the machine would not have allowed (8.3.1). Reported, never
    /// auto-mutated: silently rewriting a legacy status would destroy the evidence of how the risk
    /// got there.
    /// </summary>
    [HttpGet]
    [Route("WorkflowViolations")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<WorkflowViolation>))]
    public async Task<ActionResult<List<WorkflowViolation>>> GetWorkflowViolations()
    {
        GetUser();
        return Ok(await workflow.FindLegacyViolationsAsync());
    }

    /// <summary>The second signature on a review that crossed the dual-approval threshold (8.3.4).</summary>
    [HttpPost]
    [Route("{id}/MgmtReviews/{reviewId}/Countersign")]
    [Authorize(Policy = "RequireMgmtReviewAccess")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MgmtReview))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MgmtReview>> Countersign(int id, int reviewId,
        [FromBody] CountersignRequest? request = null)
    {
        var user = GetUser();

        try
        {
            var review = await mgmtReviews.CountersignAsync(reviewId, user.Value,
                request?.SegregationOverrideReason);

            Logger.Information("User:{User} counter-signed review {Review} of risk {Risk}", user.Value,
                reviewId, id);

            return Ok(review);
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (RuleBrokenException ex)
        {
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error counter-signing review {Id}", reviewId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 8.2 both scores -----------------------------------------------------------------------

    /// <summary>Inherent and residual with the delta, for the lists and the editors (8.2.2).</summary>
    [HttpGet]
    [Route("Scores")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskScorePair>))]
    public async Task<ActionResult<List<RiskScorePair>>> GetScorePairs([FromQuery] List<int>? ids = null)
    {
        GetUser();
        return Ok(await risksService.GetScorePairsAsync(ids));
    }

    // --- 8.4 audit trail ----------------------------------------------------------------------

    /// <summary>Who changed what, when, across the whole risk aggregate (8.4.2).</summary>
    [HttpGet]
    [Route("{id}/AuditTrail")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    public async Task<ActionResult<List<AuditLog>>> GetAuditTrail(int id, [FromQuery] int limit = 1000)
    {
        GetUser();
        return Ok(await auditTrail.GetForRiskAsync(id, limit));
    }

    // --- 8.5 review requests and treatment tasks ----------------------------------------------

    /// <summary>Flags a risk for an out-of-cadence review (8.5.1).</summary>
    [HttpPost]
    [Route("{id}/RequestReview")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RequestReview(int id, [FromBody] ReviewRequestReason? request = null)
    {
        var user = GetUser();

        try
        {
            var flagged = await risksService.RequestReviewAsync(id,
                request?.Reason ?? $"A review was requested by user {user.Value}.");

            // 200 either way: "already flagged" is the state the caller asked for, and a 409 would
            // make a harmless repeat look like an error.
            return Ok(new { flagged });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Risks currently flagged for an out-of-cadence review.</summary>
    [HttpGet]
    [Route("ReviewRequested")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Risk>))]
    public async Task<ActionResult<List<Risk>>> GetReviewRequested()
    {
        GetUser();
        return Ok(await risksService.GetReviewRequestedAsync());
    }

    /// <summary>The treatment tasks of a risk, across its mitigations (8.5.3).</summary>
    [HttpGet]
    [Route("{id}/MitigationTasks")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MitigationTask>))]
    public async Task<ActionResult<List<MitigationTask>>> GetMitigationTasks(int id)
    {
        GetUser();
        return Ok(await mitigationTasks.GetByRiskAsync(id));
    }

    // --- 8.5.2 pending-risk triage -------------------------------------------------------------

    /// <summary>
    /// The assessment intake queue. Nothing read this table before Track 8 — rows accumulated and no
    /// code path promoted one, so the assessment-to-register pipeline was dead.
    /// </summary>
    [HttpGet]
    [Route("Pending")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<PendingRiskListing>))]
    public async Task<ActionResult<List<PendingRiskListing>>> GetPending(
        [FromQuery] PendingRiskStatus? status = PendingRiskStatus.Pending)
    {
        GetUser();
        return Ok(await risksService.GetPendingRisksAsync(status));
    }

    /// <summary>Promotes a pending risk into the register.</summary>
    [HttpPost]
    [Route("Pending/{pendingId}/Promote")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Risk))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Risk>> PromotePending(int pendingId,
        [FromBody] PendingRiskPromotion? request = null)
    {
        var user = GetUser();

        try
        {
            var risk = await risksService.PromotePendingRiskAsync(pendingId,
                request ?? new PendingRiskPromotion(), user.Value);

            Logger.Information("User:{User} promoted pending risk {Pending} to risk {Risk}", user.Value,
                pendingId, risk.Id);

            return Created($"Risks/{risk.Id}", risk);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error promoting pending risk {Id}", pendingId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Drops a pending risk with a stated reason.</summary>
    [HttpPost]
    [Route("Pending/{pendingId}/Dismiss")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> DismissPending(int pendingId, [FromBody] PendingRiskDismissal request)
    {
        var user = GetUser();

        try
        {
            await risksService.DismissPendingRiskAsync(pendingId, request?.Reason ?? string.Empty,
                user.Value);

            return Ok();
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (InvalidStateTransitionException ex)
        {
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error dismissing pending risk {Id}", pendingId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 8.7 quantitative scoring --------------------------------------------------------------

    /// <summary>The cached FAIR-lite result, or 204 when the risk has never been scored that way.</summary>
    [HttpGet]
    [Route("{id}/Quantitative")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(QuantitativeRiskResult))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<QuantitativeRiskResult>> GetQuantitative(int id)
    {
        GetUser();

        var result = await quantitative.GetAsync(id);
        return result is null ? NoContent() : Ok(result);
    }

    /// <summary>Stores calibrated ranges and runs the simulation (8.7.2).</summary>
    [HttpPost]
    [Route("{id}/Quantitative")]
    [Authorize(Policy = "RequireSubmitRisk")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(QuantitativeRiskResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuantitativeRiskResult>> ComputeQuantitative(int id,
        [FromBody] QuantitativeRiskInput input)
    {
        var user = GetUser();

        if (input is null) return BadRequest("Quantitative inputs are required.");

        try
        {
            var result = await quantitative.ComputeAndSaveAsync(id, input);

            Logger.Information("User:{User} scored risk {Id} quantitatively: ALE P50 {P50:N0}", user.Value,
                id, result.InherentP50);

            return Ok(result);
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
            Logger.Error(ex, "Unknown error scoring risk {Id} quantitatively", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>The optional break-glass reason on a counter-signature.</summary>
public class CountersignRequest
{
    public string? SegregationOverrideReason { get; set; }
}

/// <summary>Why an out-of-cadence review is being requested.</summary>
public class ReviewRequestReason
{
    public string? Reason { get; set; }
}
