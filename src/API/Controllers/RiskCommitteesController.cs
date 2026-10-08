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
/// The risk committee — MIGR-TI/IA Phase 0's collegiate approver (Stage 9.9, S50 §6), beside the individual authorizing
/// manager of <c>POST /Risks/{id}/Acceptances</c>, which is unchanged.
///
/// No new permission (S50 D11), one attribute per action, pinned by <c>DecisionCycleAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>RequireRiskmanagement</c>;</item>
/// <item>constituting a committee, its required approvals and its members — <c>RequireAdminOnly</c>, a Phase 0 act;</item>
/// <item>submitting an acceptance and withdrawing it — <c>RequireMgmtReviewAccess</c>, the audience that accepts risk
/// individually;</item>
/// <item>voting — <c>RequireValidUser</c>: the authority to vote is membership, which the service checks (403 for a
/// non-member), not a permission a role carries.</item>
/// </list>
/// Errors are those of <see cref="IRiskCommitteesService"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class RiskCommitteesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskCommitteesService committees)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string ConstitutePolicy = "RequireAdminOnly";
    public const string SubmitPolicy = "RequireMgmtReviewAccess";
    public const string VotePolicy = "RequireValidUser";

    // --- committees --------------------------------------------------------------------------------------------

    [HttpGet]
    [Route("")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskCommitteeDto>))]
    public async Task<ActionResult<List<RiskCommitteeDto>>> GetCommittees([FromQuery] bool includeRetired = false)
    {
        GetUser();

        try
        {
            return Ok(await committees.GetCommitteesAsync(includeRetired));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the risk committees");
        }
    }

    [HttpGet]
    [Route("{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskCommitteeDto>> GetCommittee(int id)
    {
        GetUser();

        try
        {
            return Ok(await committees.GetCommitteeAsync(id));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading risk committee {id}");
        }
    }

    [HttpPost]
    [Route("")]
    [Authorize(Policy = ConstitutePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskCommitteeDto>> CreateCommittee([FromBody] RiskCommitteeRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.CreateCommitteeAsync(request ?? new RiskCommitteeRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "constituting a risk committee");
        }
    }

    [HttpPut]
    [Route("{id:int}")]
    [Authorize(Policy = ConstitutePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCommitteeDto>> UpdateCommittee(int id, [FromBody] RiskCommitteeRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.UpdateCommitteeAsync(id, request ?? new RiskCommitteeRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"updating risk committee {id}");
        }
    }

    [HttpPost]
    [Route("{id:int}/Retire")]
    [Authorize(Policy = ConstitutePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskCommitteeDto>> RetireCommittee(int id)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.RetireCommitteeAsync(id, user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"retiring risk committee {id}");
        }
    }

    [HttpPut]
    [Route("{id:int}/Members/{userId:int}")]
    [Authorize(Policy = ConstitutePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCommitteeDto>> AddMember(int id, int userId)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.AddMemberAsync(id, userId, user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"adding user {userId} to risk committee {id}");
        }
    }

    [HttpDelete]
    [Route("{id:int}/Members/{userId:int}")]
    [Authorize(Policy = ConstitutePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveMember(int id, int userId)
    {
        var user = GetUser();

        try
        {
            await committees.RemoveMemberAsync(id, userId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"removing user {userId} from risk committee {id}");
        }
    }

    // --- decisions ---------------------------------------------------------------------------------------------

    [HttpGet]
    [Route("Decisions")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskCommitteeDecisionDto>))]
    public async Task<ActionResult<List<RiskCommitteeDecisionDto>>> GetDecisions([FromQuery] int? committeeId = null,
        [FromQuery] int? riskId = null, [FromQuery] bool openOnly = false)
    {
        GetUser();

        try
        {
            return Ok(await committees.GetDecisionsAsync(committeeId, riskId, openOnly));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing committee decisions");
        }
    }

    [HttpGet]
    [Route("Decisions/{decisionId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDecisionDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskCommitteeDecisionDto>> GetDecision(int decisionId)
    {
        GetUser();

        try
        {
            return Ok(await committees.GetDecisionAsync(decisionId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading committee decision {decisionId}");
        }
    }

    /// <summary>Submits a risk acceptance (or a renewal) to the committee for its vote.</summary>
    [HttpPost]
    [Route("{id:int}/Decisions")]
    [Authorize(Policy = SubmitPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDecisionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCommitteeDecisionDto>> OpenDecision(int id,
        [FromBody] RiskCommitteeDecisionRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.OpenDecisionAsync(id, request ?? new RiskCommitteeDecisionRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"submitting a decision to risk committee {id}");
        }
    }

    /// <summary>A member's vote, final; the vote that reaches the required approvals creates the acceptance.</summary>
    [HttpPost]
    [Route("Decisions/{decisionId:int}/Votes")]
    [Authorize(Policy = VotePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDecisionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCommitteeDecisionDto>> Vote(int decisionId,
        [FromBody] RiskCommitteeVoteRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.VoteAsync(decisionId, request ?? new RiskCommitteeVoteRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"voting on committee decision {decisionId}");
        }
    }

    /// <summary>Withdraws an open decision, with a reason — by whoever submitted it, or an administrator.</summary>
    [HttpPost]
    [Route("Decisions/{decisionId:int}/Withdraw")]
    [Authorize(Policy = SubmitPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCommitteeDecisionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCommitteeDecisionDto>> Withdraw(int decisionId,
        [FromBody] RiskCommitteeWithdrawRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await committees.WithdrawAsync(decisionId, request ?? new RiskCommitteeWithdrawRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"withdrawing committee decision {decisionId}");
        }
    }
}
