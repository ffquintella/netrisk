using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
using Contracts.Importers;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Importers.Dedup;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Per-scanner deduplication configuration and its preview panel (Track 3 milestone 3.3.3).
///
/// The preview endpoint is the point of this controller: a dedup heuristic change silently alters
/// what counts as "the same finding" from that moment on, and being able to try two findings against
/// a proposed configuration before saving it is what makes the change reviewable instead of a
/// leap of faith.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class DedupConfigurationsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IDeduplicationService dedupService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ScannerDedupConfiguration>))]
    public async Task<ActionResult<List<ScannerDedupConfiguration>>> GetAll()
    {
        GetUser();

        try
        {
            return Ok(await dedupService.GetConfigurationsAsync());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing dedup configurations");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The configuration for one importer, synthesising the default when none is saved — so the
    /// admin form always has something to show.
    /// </summary>
    [HttpGet]
    [Route("{importer}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScannerDedupConfiguration))]
    public async Task<ActionResult<ScannerDedupConfiguration>> Get(string importer)
    {
        GetUser();

        try
        {
            return Ok(await dedupService.GetConfigurationAsync(importer));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading the dedup configuration for {Importer}", importer);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The strategies and hash fields a configuration may name, for the admin form's checkbox list.
    /// Includes any strategy contributed by an enabled plugin.
    /// </summary>
    [HttpGet]
    [Route("options")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DedupOptionsResponse))]
    public async Task<ActionResult<DedupOptionsResponse>> GetOptions()
    {
        GetUser();

        try
        {
            return Ok(new DedupOptionsResponse
            {
                Strategies = await dedupService.KnownStrategyNamesAsync(),
                HashFields = DedupFieldSet.Available.ToList(),
                DefaultHashFields = DedupFieldSet.Default.ToList()
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing dedup options");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut]
    [Route("{importer}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScannerDedupConfiguration))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScannerDedupConfiguration>> Save(string importer,
        [FromBody] ScannerDedupConfiguration configuration)
    {
        var user = GetUser();

        if (configuration == null) return BadRequest("A dedup configuration is required.");

        configuration.Importer = importer;

        try
        {
            var saved = await dedupService.SaveConfigurationAsync(configuration, user.Value);
            Logger.Information("User:{User} saved the dedup configuration for {Importer}", user.Value, importer);
            return Ok(saved);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error saving the dedup configuration for {Importer}", importer);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The change history for one importer's configuration.</summary>
    [HttpGet]
    [Route("{importer}/history")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ScannerDedupConfigurationHistory>))]
    public async Task<ActionResult<List<ScannerDedupConfigurationHistory>>> GetHistory(string importer)
    {
        GetUser();

        try
        {
            return Ok(await dedupService.GetConfigurationHistoryAsync(importer));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading the dedup history for {Importer}", importer);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Computes the keys two findings would get under an importer's current configuration and reports
    /// whether they would merge. Read-only, so a heuristic can be validated before it is saved.
    /// </summary>
    [HttpPost]
    [Route("{importer}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DedupPreviewResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DedupPreviewResponse>> Preview(string importer,
        [FromBody] DedupPreviewRequest request)
    {
        GetUser();

        if (request?.Left == null || request.Right == null)
            return BadRequest("Two findings are required to preview a deduplication decision.");

        try
        {
            var preview = await dedupService.PreviewAsync(
                new DedupContext
                {
                    Finding = request.Left,
                    HostId = request.LeftHostId,
                    HostServiceId = request.LeftHostServiceId
                },
                new DedupContext
                {
                    Finding = request.Right,
                    HostId = request.RightHostId,
                    HostServiceId = request.RightHostServiceId
                },
                importer);

            return Ok(new DedupPreviewResponse
            {
                StrategyChain = preview.Configuration.StrategyChain,
                HashFields = preview.Configuration.HashFields,
                WouldMerge = preview.WouldMerge,
                LeftKeys = preview.Left.Candidates.Select(c => new DedupKeyView(c.Strategy, c.Key)).ToList(),
                RightKeys = preview.Right.Candidates.Select(c => new DedupKeyView(c.Strategy, c.Key)).ToList(),
                SharedKeys = preview.SharedKeys.ToList()
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error previewing deduplication for {Importer}", importer);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>What a dedup configuration may be built from.</summary>
public class DedupOptionsResponse
{
    public List<string> Strategies { get; set; } = new();

    public List<string> HashFields { get; set; } = new();

    public List<string> DefaultHashFields { get; set; } = new();
}

/// <summary>Two findings to compare, with the asset ids the legacy strategy needs.</summary>
public class DedupPreviewRequest
{
    public NormalizedFinding? Left { get; set; }

    public NormalizedFinding? Right { get; set; }

    public int? LeftHostId { get; set; }

    public int? LeftHostServiceId { get; set; }

    public int? RightHostId { get; set; }

    public int? RightHostServiceId { get; set; }
}

/// <summary>The preview verdict, with every candidate key so a surprising merge can be explained.</summary>
public class DedupPreviewResponse
{
    public string? StrategyChain { get; set; }

    public string? HashFields { get; set; }

    public bool WouldMerge { get; set; }

    public List<DedupKeyView> LeftKeys { get; set; } = new();

    public List<DedupKeyView> RightKeys { get; set; } = new();

    public List<string> SharedKeys { get; set; } = new();
}

/// <summary>One strategy's key.</summary>
public record DedupKeyView(string Strategy, string Key);
