using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.TreatmentEconomics;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Treatment economics — the four treatment options, the monetary cost, Gate C, the target risk level and Gate D
/// (Stage 9.6, S47 §6).
///
/// No new permission or policy (S47 D10), one attribute per action, pinned by
/// <c>TreatmentEconomicsAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reading a mitigation's economics — <c>[Authorize(Policy = "RequireMitigation")]</c>, the audience of
/// <c>/Mitigations</c>;</item>
/// <item>declaring the option, the cost, the prerequisites and the target — <c>[Authorize(Policy = "RequirePlanMitigations")]</c>,
/// the audience that plans treatment and files its tasks;</item>
/// <item>the risk view and the portfolio selection, both reads — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>.</item>
/// </list>
/// Mitigations and risks outside the caller's entity scope are 404, the same as missing ones.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class TreatmentEconomicsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ITreatmentEconomicsService economics)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string MitigationReadPolicy = "RequireMitigation";
    public const string PlanPolicy = "RequirePlanMitigations";
    public const string RegisterPolicy = "RequireRiskmanagement";

    /// <summary>The treatment option, the monetary cost, the prerequisites, Gate C and the action plan of a mitigation.</summary>
    [HttpGet]
    [Route("Mitigations/{id:int}")]
    [Authorize(Policy = MitigationReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MitigationEconomicsDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MitigationEconomicsDto>> GetMitigation(int id)
    {
        GetUser();

        try
        {
            return Ok(await economics.GetMitigationAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the economics of mitigation {id}");
        }
    }

    /// <summary>
    /// Replaces the treatment option, the monetary cost, the estimates and the prerequisites of a mitigation. "Accept"
    /// is refused while Gate A holds; a prerequisite that closes a cycle is refused.
    /// </summary>
    [HttpPut]
    [Route("Mitigations/{id:int}")]
    [Authorize(Policy = PlanPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MitigationEconomicsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MitigationEconomicsDto>> SaveMitigation(int id,
        [FromBody] MitigationEconomicsRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await economics.SaveMitigationAsync(id, request ?? new MitigationEconomicsRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"saving the economics of mitigation {id}");
        }
    }

    /// <summary>Gate A, the protected flags, the appetite, the target and every mitigation of a risk with its Gate C.</summary>
    [HttpGet]
    [Route("Risks/{id:int}")]
    [Authorize(Policy = RegisterPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskTreatmentEconomicsDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskTreatmentEconomicsDto>> GetRisk(int id)
    {
        GetUser();

        try
        {
            return Ok(await economics.GetRiskAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the treatment economics of risk {id}");
        }
    }

    /// <summary>Sets the target risk level — a score, an expected loss or both, with a rationale.</summary>
    [HttpPut]
    [Route("Risks/{id:int}/Target")]
    [Authorize(Policy = PlanPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskTargetDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskTargetDto>> SaveTarget(int id, [FromBody] RiskTargetRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await economics.SaveTargetAsync(id, request ?? new RiskTargetRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"setting the target level of risk {id}");
        }
    }

    /// <summary>Removes the target risk level.</summary>
    [HttpDelete]
    [Route("Risks/{id:int}/Target")]
    [Authorize(Policy = PlanPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteTarget(int id)
    {
        var user = GetUser();

        try
        {
            await economics.DeleteTargetAsync(id, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"removing the target level of risk {id}");
        }
    }

    /// <summary>
    /// Gate D: the portfolio selection under budget, people capacity, dependencies and deadline, preserving tail and
    /// systemic risks. Computed on request and never stored — POST only because the constraints are a body.
    /// </summary>
    [HttpPost]
    [Route("Portfolio")]
    [Authorize(Policy = RegisterPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PortfolioSelectionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioSelectionDto>> SelectPortfolio([FromBody] PortfolioSelectionRequest? request)
    {
        GetUser();

        try
        {
            return Ok(await economics.SelectPortfolioAsync(request ?? new PortfolioSelectionRequest()));
        }
        catch (Exception ex)
        {
            return Fail(ex, "selecting the treatment portfolio");
        }
    }

    /// <summary>
    /// The domain exceptions onto the status codes the other controllers use for them; anything else is logged and
    /// answered 500 with no detail.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
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
