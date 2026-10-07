using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.TailRisk;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Tail statistics and portfolio — the loss magnitude by form of loss, the tail of a risk with Gate B and the flag 8
/// criterion, the declared correlations between scenarios and the portfolio aggregation (Stage 9.7, S48 §6). The
/// appetite's monetary tolerances are on <see cref="RiskAppetitesController"/>, admin-only like the rest of the appetite.
///
/// No new permission or policy, one attribute per action, pinned by <c>TailRiskAuthorizationTest</c>:
/// <list type="bullet">
/// <item>the reads and the portfolio aggregation (computed on request, never stored) —
/// <c>[Authorize(Policy = "RequireRiskmanagement")]</c>, the audience of the register;</item>
/// <item>declaring or removing loss components and correlations — <c>[Authorize(Policy = "RequireSubmitRisk")]</c>, the
/// audience that edits a risk.</item>
/// </list>
/// Risks and correlations outside the caller's entity scope are 404, the same as missing ones.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class TailRiskController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ITailRiskService tailRisk)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string WritePolicy = "RequireSubmitRisk";

    /// <summary>The tail of a risk: its runs, declared components, Gate B on the tail and the flag 8 criterion.</summary>
    [HttpGet]
    [Route("Risks/{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskTailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskTailDto>> GetRisk(int id)
    {
        GetUser();

        try
        {
            return Ok(await tailRisk.GetRiskAsync(id));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the tail of risk {id}");
        }
    }

    /// <summary>Replaces a risk's loss components and recomputes its analysis when it has one.</summary>
    [HttpPut]
    [Route("Risks/{id:int}/LossComponents")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskTailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskTailDto>> SaveLossComponents(int id, [FromBody] LossComponentsRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await tailRisk.SaveLossComponentsAsync(id, request ?? new LossComponentsRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"saving the loss components of risk {id}");
        }
    }

    /// <summary>Removes a risk's loss components (404 when it has none) and recomputes its analysis when it has one.</summary>
    [HttpDelete]
    [Route("Risks/{id:int}/LossComponents")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskTailDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskTailDto>> DeleteLossComponents(int id)
    {
        var user = GetUser();

        try
        {
            return Ok(await tailRisk.DeleteLossComponentsAsync(id, user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"removing the loss components of risk {id}");
        }
    }

    /// <summary>The declared correlations whose two risks the caller can see, optionally those of one risk.</summary>
    [HttpGet]
    [Route("Correlations")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskCorrelationDto>))]
    public async Task<ActionResult<List<RiskCorrelationDto>>> GetCorrelations([FromQuery] int? riskId)
    {
        GetUser();

        try
        {
            return Ok(await tailRisk.GetCorrelationsAsync(riskId));
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing the risk correlations");
        }
    }

    /// <summary>
    /// Declares or updates the correlation of a pair, in either order. A matrix that is not positive semidefinite, or a
    /// correlated group that is too large, is refused with 422.
    /// </summary>
    [HttpPut]
    [Route("Correlations")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskCorrelationDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskCorrelationDto>> SaveCorrelation([FromBody] RiskCorrelationRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await tailRisk.SaveCorrelationAsync(request ?? new RiskCorrelationRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return Fail(ex, "saving a risk correlation");
        }
    }

    /// <summary>Removes a declared correlation.</summary>
    [HttpDelete]
    [Route("Correlations/{id:int}")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteCorrelation(int id)
    {
        var user = GetUser();

        try
        {
            await tailRisk.DeleteCorrelationAsync(id, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"removing risk correlation {id}");
        }
    }

    /// <summary>
    /// Aggregates a portfolio's annual losses under the declared correlation, with Gate B on the portfolio. Computed on
    /// request and never stored — POST only because the risks and the basis are a body.
    /// </summary>
    [HttpPost]
    [Route("Portfolio")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PortfolioTailDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioTailDto>> AggregatePortfolio([FromBody] PortfolioTailRequest? request)
    {
        GetUser();

        try
        {
            return Ok(await tailRisk.AggregatePortfolioAsync(request ?? new PortfolioTailRequest()));
        }
        catch (Exception ex)
        {
            return Fail(ex, "aggregating the portfolio tail");
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
