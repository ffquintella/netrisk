using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.RiskFlags;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The eleven mandatory flags, Gate A, the Phase 4 decision and the Top Risks list (Stage 9.5, S46 §6).
///
/// No new permission or policy (S46 D15), one attribute per action, pinned by
/// <c>RiskFlagsAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reading, declaring a flag and re-deriving — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>,
/// the audience of the risk register;</item>
/// <item>withdrawing a declaration and recording a decision — <c>[Authorize(Policy = "RequireMgmtReviewAccess")]</c>,
/// the audience that accepts risk, because both change what may be accepted.</item>
/// </list>
/// Risks outside the caller's entity scope are 404, the same as missing ones. Gate A itself is enforced in the
/// workflow (acceptance, renewal, closure, deletion), not here: this controller only reads and records.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class RiskFlagsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskFlagsService flags)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string DecisionPolicy = "RequireMgmtReviewAccess";

    /// <summary>The eleven flags and the Gate A condition, with the origin of each.</summary>
    [HttpGet]
    [Route("Catalogue")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<RiskFlagDescriptor>))]
    public ActionResult<IReadOnlyList<RiskFlagDescriptor>> GetCatalogue()
    {
        GetUser();
        return Ok(flags.GetCatalogue());
    }

    /// <summary>The persisted flags, Gate A and the decision in force on a risk.</summary>
    [HttpGet]
    [Route("Risks/{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskFlagsStateDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskFlagsStateDto>> GetRiskFlags(int id)
    {
        GetUser();

        try
        {
            return Ok(await flags.GetAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the flags of risk {id}");
        }
    }

    /// <summary>Re-derives flags 3, 4 and 5 of a risk now, instead of waiting for the nightly pass.</summary>
    [HttpPost]
    [Route("Risks/{id:int}/Refresh")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskFlagsStateDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskFlagsStateDto>> Refresh(int id)
    {
        var user = GetUser();

        try
        {
            var state = await flags.RefreshAsync(id);
            Logger.Information("User:{User} re-derived the flags of risk {Id}", user.Value, id);
            return Ok(state);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"re-deriving the flags of risk {id}");
        }
    }

    /// <summary>Declares a flag with a written reason.</summary>
    [HttpPut]
    [Route("Risks/{id:int}/Flags/{code}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskFlagsStateDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskFlagsStateDto>> Declare(int id, RiskFlagCode code,
        [FromBody] RiskFlagDeclarationRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await flags.DeclareAsync(id, code, request ?? new RiskFlagDeclarationRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"declaring flag {code} on risk {id}");
        }
    }

    /// <summary>
    /// Withdraws a declaration with a written reason. For a Gate A condition the risk's submitter, owner and
    /// manager are refused (segregation of duties, no break-glass).
    /// </summary>
    [HttpPost]
    [Route("Risks/{id:int}/Flags/{code}/Withdraw")]
    [Authorize(Policy = DecisionPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskFlagsStateDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskFlagsStateDto>> Withdraw(int id, RiskFlagCode code,
        [FromBody] RiskFlagWithdrawalRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await flags.WithdrawAsync(id, code, request ?? new RiskFlagWithdrawalRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"withdrawing flag {code} on risk {id}");
        }
    }

    /// <summary>The Phase 4 decision log of a risk, most recent first.</summary>
    [HttpGet]
    [Route("Risks/{id:int}/Decisions")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskDecisionDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RiskDecisionDto>>> GetDecisions(int id)
    {
        GetUser();

        try
        {
            return Ok(await flags.GetDecisionsAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the decisions of risk {id}");
        }
    }

    /// <summary>Records a Phase 4 decision; "act immediately" is escalated and notified.</summary>
    [HttpPost]
    [Route("Risks/{id:int}/Decisions")]
    [Authorize(Policy = DecisionPolicy)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RiskDecisionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskDecisionDto>> RecordDecision(int id, [FromBody] RiskDecisionRequest? request)
    {
        var user = GetUser();

        try
        {
            var decision = await flags.RecordDecisionAsync(id, request ?? new RiskDecisionRequest(), user.Value);
            return CreatedAtAction(nameof(GetDecisions), new { id }, decision);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"recording a decision on risk {id}");
        }
    }

    /// <summary>Risks in scope carrying a flag and/or whose Gate A holds.</summary>
    [HttpGet]
    [Route("Flagged")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FlaggedRiskDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<FlaggedRiskDto>>> GetFlagged([FromQuery] RiskFlagCode? flag = null,
        [FromQuery] bool? gateA = null)
    {
        GetUser();

        try
        {
            return Ok(await flags.GetFlaggedAsync(flag, gateA));
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing flagged risks");
        }
    }

    /// <summary>The executive Top Risks list: trend, confidence, exposure, owner and next decision.</summary>
    [HttpGet]
    [Route("TopRisks")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TopRisksDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TopRisksDto>> GetTopRisks([FromQuery] int limit = 10)
    {
        GetUser();

        try
        {
            return Ok(await flags.GetTopRisksAsync(limit));
        }
        catch (Exception ex)
        {
            return Fail(ex, "building the Top Risks list");
        }
    }

    /// <summary>
    /// The domain exceptions onto the status codes the other controllers use for them; anything else is
    /// logged and answered 500 with no detail.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
    {
        switch (exception)
        {
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case InvalidStateTransitionException ex:
                return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
            case RuleBrokenException ex:
                return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
