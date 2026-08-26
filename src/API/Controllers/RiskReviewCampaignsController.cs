using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
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
/// The periodic business review campaigns the portal is a view over (Track 8 milestone 8.6).
///
/// Gated on the new <c>business_risk_review</c> permission rather than on risk management. That is
/// the point of the permission: an appointed business reviewer should be able to decide their own
/// entity's risks and nothing else, and granting them <c>riskmanagement</c> to reach the portal would
/// hand them the whole register.
///
/// Entity scoping is not enforced here — it is enforced by the Track 2.3 query filters on
/// <c>risk_review_campaigns</c>, so a reviewer's context cannot see another entity's campaign even if
/// this controller asked for it by id. The <see cref="MineAsync"/> route narrows further to the
/// entities the caller is actually appointed to.
/// </summary>
[ApiController]
[PermissionAuthorize("business_risk_review")]
[Route("[controller]")]
public class RiskReviewCampaignsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskReviewCampaignsService campaigns,
    IEntityRiskReviewersService reviewers)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>The campaigns the caller is appointed to review. The portal's landing query.</summary>
    [HttpGet]
    [Route("Mine")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskReviewCampaign>))]
    public async Task<ActionResult<List<RiskReviewCampaign>>> MineAsync([FromQuery] bool openOnly = true)
    {
        var user = GetUser();
        return Ok(await campaigns.GetForReviewerAsync(user.Value, openOnly));
    }

    /// <summary>One campaign with its items, risks and decisions.</summary>
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskReviewCampaign))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskReviewCampaign>> Get(int id)
    {
        var user = GetUser();

        try
        {
            var campaign = await campaigns.GetAsync(id);

            if (!await IsAppointedAsync(user.Value, campaign.EntityId, user.Admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "not_appointed", message = "You are not an appointed risk reviewer for this entity." });

            return Ok(campaign);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// The campaign's risks with their scores, appetite verdict, live acceptance and treatment tasks
    /// — everything the review screen renders, in one call (8.6.4).
    ///
    /// A campaign sub-resource rather than a set of register-wide reads, because a business reviewer
    /// deliberately does not hold <c>riskmanagement</c>: <c>/Risks/Scores</c> and
    /// <c>/Risks/{id}/Appetite</c> are correctly closed to them. Behind this permission and the
    /// appointment check, the same information is scoped to the campaign they were appointed to.
    /// </summary>
    [HttpGet]
    [Route("{id}/Items")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<CampaignReviewItem>))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CampaignReviewItem>>> GetItems(int id)
    {
        var user = GetUser();

        try
        {
            var campaign = await campaigns.GetAsync(id);

            if (!await IsAppointedAsync(user.Value, campaign.EntityId, user.Admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_appointed" });

            return Ok(await campaigns.GetReviewItemsAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Persists a drag-to-rank ordering (8.6.4) and mirrors it onto the risks (8.6.5).</summary>
    [HttpPut]
    [Route("{id}/Ranking")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SaveRanking(int id, [FromBody] CampaignRankingRequest request)
    {
        var user = GetUser();

        if (request is null || request.OrderedItemIds.Count == 0)
            return BadRequest("An ordered list of campaign item ids is required.");

        try
        {
            var campaign = await campaigns.GetAsync(id);

            if (!await IsAppointedAsync(user.Value, campaign.EntityId, user.Admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_appointed" });

            await campaigns.SaveRankingAsync(id, request.OrderedItemIds, user.Value);
            return Ok();
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Records one decision: accept (creating an appetite-gated acceptance), request mitigation
    /// (creating treatment tasks) or escalate.
    /// </summary>
    [HttpPost]
    [Route("{id}/Items/{itemId}/Decision")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskReviewCampaignItem))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskReviewCampaignItem>> Decide(int id, int itemId,
        [FromBody] CampaignDecisionRequest request)
    {
        var user = GetUser();

        if (request is null) return BadRequest("A decision is required.");

        try
        {
            var campaign = await campaigns.GetAsync(id);

            if (!await IsAppointedAsync(user.Value, campaign.EntityId, user.Admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_appointed" });

            var item = await campaigns.DecideAsync(id, itemId, request, user.Value);

            Logger.Information("User:{User} decided {Decision} on campaign {Campaign} item {Item}",
                user.Value, request.Decision, id, itemId);

            return Ok(item);
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
            // The appetite ceiling and segregation of duties both land here. The message is written
            // to be shown to a business reviewer verbatim.
            return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
        }
        catch (PermissionInvalidException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "insufficient_authority", ex.Permission, ex.Message });
        }
        catch (DataAlreadyExistsException ex)
        {
            return Conflict(new { error = "already_accepted", ex.Message });
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error recording a campaign decision");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Completion rates, decision mix and time-to-decide (8.6.5).</summary>
    [HttpGet]
    [Route("Statistics")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<CampaignStatistics>))]
    public async Task<ActionResult<List<CampaignStatistics>>> Statistics([FromQuery] int? entityId = null)
    {
        GetUser();
        return Ok(await campaigns.GetStatisticsAsync(entityId));
    }

    /// <summary>
    /// Generates the campaigns due now. Exposed for administrators so a first campaign can be
    /// created without waiting a day for the job — the job and this call share one idempotent
    /// implementation, so running both is harmless.
    /// </summary>
    [HttpPost]
    [Route("Generate")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskReviewCampaign>))]
    public async Task<ActionResult<List<RiskReviewCampaign>>> Generate()
    {
        var user = GetUser();

        var created = await campaigns.GenerateDueCampaignsAsync(DateTime.UtcNow);

        Logger.Information("User:{User} generated {Count} risk-review campaigns", user.Value, created.Count);

        return Ok(created);
    }

    /// <summary>
    /// Whether the caller may act on an entity's campaign. An administrator may, because somebody has
    /// to be able to unblock a campaign whose reviewer has left; the segregation-of-duties rule still
    /// applies to the decision itself, so this is not a way to approve your own risk.
    /// </summary>
    private async Task<bool> IsAppointedAsync(int userId, int entityId, bool isAdmin)
    {
        if (isAdmin) return true;

        var entities = await reviewers.GetEntitiesForReviewerAsync(userId);
        return entities.Contains(entityId);
    }
}
