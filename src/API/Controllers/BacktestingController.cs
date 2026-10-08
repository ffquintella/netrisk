using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.DecisionCycle;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Incident backtesting — the calibration of MIGR-TI/IA Phase 4's cut (Stage 9.9, S50 §6): incidents and near misses
/// confronted with the register. A person matches an incident to the registered risks that describe it; the dates decide
/// whether they foresaw it, so a risk registered after the incident is never counted as foreseen.
///
/// No new permission (S50 D11), one attribute per action, pinned by <c>DecisionCycleAuthorizationTest</c>: reads under
/// <c>RequireRiskmanagement</c>, the assessment under <c>RequireSubmitRisk</c> — the audience that declares reassessment
/// events in Stage 9.8. An incident or risk outside the caller's scope is 404.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class BacktestingController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IBacktestingService backtesting)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireRiskmanagement";
    public const string WritePolicy = "RequireSubmitRisk";

    /// <summary>The report of a period (UTC, default the last year): counts by outcome, the rates and the incidents.</summary>
    [HttpGet]
    [Route("")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BacktestReportDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BacktestReportDto>> GetReport([FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null, [FromQuery] int? entityId = null)
    {
        GetUser();

        try
        {
            return Ok(await backtesting.GetReportAsync(from, to, entityId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "computing the backtesting report");
        }
    }

    /// <summary>One incident's backtest.</summary>
    [HttpGet]
    [Route("Incidents/{incidentId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BacktestIncidentDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacktestIncidentDto>> GetIncident(int incidentId)
    {
        GetUser();

        try
        {
            return Ok(await backtesting.GetIncidentAsync(incidentId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the backtest of incident {incidentId}");
        }
    }

    /// <summary>Records (or replaces) the backtest of an incident.</summary>
    [HttpPut]
    [Route("Incidents/{incidentId:int}")]
    [Authorize(Policy = WritePolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BacktestIncidentDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacktestIncidentDto>> Assess(int incidentId,
        [FromBody] BacktestAssessmentRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await backtesting.AssessAsync(incidentId, request ?? new BacktestAssessmentRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"backtesting incident {incidentId}");
        }
    }
}
