using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Risks.Chain;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The risk linkage chain (Stage 9.1, S41 §6): objective → process → IT service → data → asset.
///
/// Every action carries <c>[Authorize(Policy = "RequireRiskmanagement")]</c> — the policy of the
/// legacy <c>/Risks/{id}/Entity</c> actions this coexists with, so exactly the people who can read and
/// write a risk's "Entity" field can read and write its chain. <c>[PermissionAuthorize("riskmanagement")]</c>
/// was rejected because it accepts a different role (<c>Admin</c>, not <c>Administrator</c>) and would
/// have narrowed the audience silently (S41 §11, D9). <c>RiskChainAuthorizationTest</c> holds both.
///
/// A host target additionally needs <c>hosts</c>; that is checked in the service, from the principal
/// passed here, because the controller cannot know a target is a host before it reads the link.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class RiskChainController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskChainService riskChain)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>The principal of this request, handed to the service for the <c>hosts</c> check.</summary>
    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <summary>The risk's chain: always five levels, empty ones included.</summary>
    [HttpGet]
    [Route("Risks/{riskId:int}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskChainDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskChainDto>> GetRiskChain(int riskId)
    {
        GetUser();

        try
        {
            return Ok(await riskChain.GetRiskChainAsync(riskId, Principal));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the chain of risk {riskId}");
        }
    }

    /// <summary>
    /// Links the risk to an entity of a chain type or to a host. 201 with the new link; 200 when an
    /// existing Legacy link to the same target was promoted to Declared.
    /// </summary>
    [HttpPost]
    [Route("Risks/{riskId:int}/Links")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RiskChainLinkDto))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskChainLinkDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskChainLinkDto>> AddLink(int riskId, [FromBody] RiskChainLinkCreateDto request)
    {
        var user = GetUser();

        try
        {
            var result = await riskChain.AddLinkAsync(riskId, request, user.Value, Principal);

            Logger.Information("User:{User} {Action} chain link {LinkId} on risk {RiskId}", user.Value,
                result.Created ? "created" : "promoted", result.Link.Id, riskId);

            return result.Created
                ? CreatedAtAction(nameof(GetRiskChain), new { riskId }, result.Link)
                : Ok(result.Link);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"linking risk {riskId}");
        }
    }

    /// <summary>
    /// Removes a Declared link: 204. When the same entity is still on the risk's legacy "Entity"
    /// field the link is demoted back to Legacy instead: 200 with the demoted link.
    /// </summary>
    [HttpDelete]
    [Route("Risks/{riskId:int}/Links/{linkId:int}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskChainLinkDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskChainLinkDto>> DeleteLink(int riskId, int linkId)
    {
        var user = GetUser();

        try
        {
            var result = await riskChain.DeleteLinkAsync(riskId, linkId, Principal);

            Logger.Information("User:{User} {Action} chain link {LinkId} on risk {RiskId}", user.Value,
                result.Deleted ? "deleted" : "demoted", linkId, riskId);

            return result.Deleted ? NoContent() : Ok(result.Demoted);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"deleting chain link {linkId} of risk {riskId}");
        }
    }

    /// <summary>The risks linked to a chain entity — directly, or with <paramref name="inferred"/> also
    /// through any node below it. Any status.</summary>
    [HttpGet]
    [Route("Entities/{entityId:int}/Risks")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskChainMatchDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<List<RiskChainMatchDto>>> GetRisksByEntity(int entityId,
        [FromQuery] bool inferred = false)
    {
        GetUser();

        try
        {
            return Ok(await riskChain.GetRisksByEntityAsync(entityId, inferred));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the risks of entity {entityId}");
        }
    }

    /// <summary>The risks linked directly to a host. Needs <c>hosts</c>, checked before the host is
    /// looked up.</summary>
    [HttpGet]
    [Route("Hosts/{hostId:int}/Risks")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskChainMatchDto>))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<RiskChainMatchDto>>> GetRisksByHost(int hostId)
    {
        GetUser();

        try
        {
            return Ok(await riskChain.GetRisksByHostAsync(hostId, Principal));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the risks of host {hostId}");
        }
    }

    /// <summary>The critical-process coverage metric, over what the caller can see.</summary>
    [HttpGet]
    [Route("Coverage/CriticalProcesses")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CriticalProcessCoverageDto))]
    public async Task<ActionResult<CriticalProcessCoverageDto>> GetCriticalProcessCoverage()
    {
        GetUser();

        try
        {
            return Ok(await riskChain.GetCriticalProcessCoverageAsync());
        }
        catch (Exception ex)
        {
            return Fail(ex, "computing critical-process coverage");
        }
    }

    /// <summary>
    /// The domain exceptions onto the status codes the other controllers use for them, so a client
    /// reads a refusal from this controller exactly as from any other.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
    {
        switch (exception)
        {
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case DataAlreadyExistsException ex:
                return Conflict(new { error = "already_exists", ex.Message });
            case RuleBrokenException ex:
                return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            case PermissionInvalidException ex:
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "insufficient_permission", ex.Permission, ex.Message });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
