using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// SLA policy administration (Track 3 milestone 3.4.1).
///
/// Writes require the configuration permission because an SLA policy governs every finding's
/// deadline; reads are open to anyone who can see the register, since the deadlines are shown there.
/// </summary>
[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class SlaConfigurationsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ISlaService slaService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// The CISA-aligned benchmark values the admin UI shows as guidance, so the recommendation lives
    /// in one place rather than being retyped into the client.
    /// </summary>
    public static readonly IReadOnlyList<SlaBenchmark> Benchmarks =
    [
        new(4, "Critical", 2, 15, "CISA: internet-facing criticals remediated within ~15 days."),
        new(3, "High", 5, 30, "CISA: highs remediated within ~30 days."),
        new(2, "Medium", 10, 60, "Common industry ladder: 60-90 days."),
        new(1, "Low", 15, 90, "Common industry ladder: 90-180 days.")
    ];

    /// <summary>Current policy rows; <c>includeSuperseded</c> adds the historical ones.</summary>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SlaConfiguration>))]
    public async Task<ActionResult<List<SlaConfiguration>>> GetAll([FromQuery] bool includeSuperseded = false)
    {
        GetUser();

        try
        {
            return Ok(await slaService.GetConfigurationsAsync(includeSuperseded));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing SLA configurations");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The benchmark guidance, for the admin form.</summary>
    [HttpGet]
    [Route("benchmarks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<SlaBenchmark>))]
    public ActionResult<IReadOnlyList<SlaBenchmark>> GetBenchmarks()
    {
        GetUser();
        return Ok(Benchmarks);
    }

    /// <summary>
    /// Sets the policy for a severity (and optionally one entity). Supersedes the previous row
    /// rather than editing it, so past compliance numbers stay reproducible.
    /// </summary>
    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(SlaConfiguration))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SlaConfiguration>> Set([FromBody] SlaConfiguration configuration)
    {
        var user = GetUser();

        if (configuration == null) return BadRequest("An SLA configuration is required.");

        try
        {
            var saved = await slaService.SetConfigurationAsync(configuration, user.Value);
            Logger.Information("User:{User} set the SLA policy for severity {Severity}", user.Value,
                configuration.Severity);
            return Created($"SlaConfigurations/{saved.Id}", saved);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error setting an SLA configuration");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Recomputes one finding's due date. Exposed because a severity edited by hand (rather than by
    /// an import) also moves the deadline, and the register's edit path should be able to say so.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("recompute/{findingId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DateTime?))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DateTime?>> Recompute(int findingId)
    {
        var user = GetUser();

        try
        {
            return Ok(await slaService.RecomputeDueDateAsync(findingId, user.Value));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error recomputing the SLA due date of finding {Finding}", findingId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>One row of benchmark guidance shown beside the SLA form.</summary>
/// <param name="Severity">The <c>NormalizedSeverity</c> value.</param>
/// <param name="SeverityName">Its display name.</param>
/// <param name="TriageDays">Recommended triage allowance.</param>
/// <param name="RemediationDays">Recommended remediation allowance.</param>
/// <param name="Source">Where the recommendation comes from.</param>
public record SlaBenchmark(int Severity, string SeverityName, int TriageDays, int RemediationDays, string Source);
