using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using API.Controllers.RiskIndex;
using API.Security;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Read;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The Cyber Risk Overview's read API (S40 §8.2; M53, T249). Every action needs <c>risk_index_view</c>; the one write,
/// the publication, adds <c>risk_index_approve</c> (the two attributes combine). The service applies what an attribute
/// cannot: the shadow pilot, the node's scope rule, the derived-computation budget and the per-module redaction.
/// The actions are pinned by <c>RiskIndexEndpointPermissionMapTest</c>.
/// </summary>
[ApiController]
[PermissionAuthorize(RiskIndexPermissions.View)]
[Route("[controller]")]
public class RiskIndexController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskIndexReadService readService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private RiskIndexCaller Caller() => RiskIndexCaller.FromPrincipal(GetUser().Value, _httpContextAccessor.HttpContext?.User);

    private async Task<ActionResult<T>> Run<T>(string operation, Func<RiskIndexCaller, Task<T>> action)
    {
        var caller = Caller();
        try
        {
            return Ok(await action(caller));
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, operation);
        }
    }

    [HttpGet]
    [Route("Snapshots/Latest")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexLatestDto))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<RiskIndexLatestDto>> GetLatest(CancellationToken ct = default)
    {
        GetUser();
        try
        {
            var latest = await readService.GetLatestAsync(ct);
            return latest is null ? NoContent() : Ok(latest);
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, "reading the latest snapshot");
        }
    }

    [HttpGet]
    [Route("Snapshots/Dates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DateOnly>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<IReadOnlyList<DateOnly>>> GetSnapshotDates([FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, CancellationToken ct = default) =>
        Run("listing the snapshot dates", _ => readService.GetSnapshotDatesAsync(from, to, ct));

    [HttpGet]
    [Route("Overview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexOverviewDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexOverviewDto>> GetOverview([FromQuery] string? node = null, [FromQuery] DateOnly? date = null,
        CancellationToken ct = default) =>
        Run("reading the overview", caller => readService.GetOverviewAsync(node, date, caller, ct));

    [HttpGet]
    [Route("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskIndexCategoryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<RiskIndexCategoryDto>>> GetCategories([FromQuery] string? node = null,
        [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the categories", caller => readService.GetCategoriesAsync(node, date, caller, ct));

    [HttpGet]
    [Route("Trend")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexTrendDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexTrendDto>> GetTrend([FromQuery] string? node = null, [FromQuery] int window = 90,
        [FromQuery] RiskIndexCategory series = RiskIndexCategory.Icr, [FromQuery] DateOnly? date = null,
        CancellationToken ct = default) =>
        Run("reading the trend", caller => readService.GetTrendAsync(node, window, series, date, caller, ct));

    [HttpGet]
    [Route("Nodes")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexNodeChildrenDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexNodeChildrenDto>> GetNodes([FromQuery] string? parent = null,
        [FromQuery] RiskIndexDimension dimension = RiskIndexDimension.Unit, [FromQuery] string? filters = null,
        [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the drill-down", caller => readService.GetNodesAsync(parent, dimension, filters, date, caller, ct));

    [HttpGet]
    [Route("Objects")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskIndexObjectRowDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<RiskIndexObjectRowDto>>> GetObjects([FromQuery] string? node = null,
        [FromQuery] DateOnly? date = null, [FromQuery] string? filters = null, [FromQuery] string? sorts = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            var (rows, total) = await readService.GetObjectsAsync(node, date, new RiskIndexObjectQuery(filters, sorts, page, pageSize),
                caller, ct);
            if (HttpContext is { } http) http.Response.Headers["X-Total-Count"] = total.ToString(CultureInfo.InvariantCulture);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, "listing the objects");
        }
    }

    [HttpGet]
    [Route("Objects/{kind}/{reference}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexObjectDetailDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexObjectDetailDto>> GetObject(RiskIndexObjectKind kind, string reference,
        [FromQuery] DateOnly? date = null, [FromQuery] string? node = null, CancellationToken ct = default) =>
        Run("reading an object", caller => readService.GetObjectAsync(kind, reference, node, date, caller, ct));

    [HttpGet]
    [Route("Inventory")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexInventoryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexInventoryDto>> GetInventory([FromQuery] string? node = null,
        [FromQuery] RiskIndexTabGroup? tab = null, [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the inventory", caller => readService.GetInventoryAsync(node, tab, date, caller, ct));

    /// <summary>
    /// The monthly mean of the daily <c>C_k</c> (S40 §5.5.1, §8.2; T253): of the node, or of the node ∩ <paramref name="tab"/>,
    /// over 6 or 12 months. A pre-computed node reads its stored days; any other is recomposed under the derived budget.
    /// </summary>
    [HttpGet]
    [Route("MonthlySummary")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexMonthlySummaryDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexMonthlySummaryDto>> GetMonthlySummary([FromQuery] string? node = null,
        [FromQuery] RiskIndexTabGroup? tab = null, [FromQuery] int months = 6, [FromQuery] DateOnly? date = null,
        CancellationToken ct = default) =>
        Run("reading the monthly summary", caller => readService.GetMonthlySummaryAsync(node, tab, months, date, caller, ct));

    /// <summary>The risk factors of the node ∩ an asset-class tab: volume and Euler points per dominant factor (S40 §5.5.1; T253).</summary>
    [HttpGet]
    [Route("RiskFactors")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskIndexRiskFactorDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<IReadOnlyList<RiskIndexRiskFactorDto>>> GetRiskFactors([FromQuery] string? node = null,
        [FromQuery] RiskIndexTabGroup? tab = null, [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the risk factors", caller => readService.GetRiskFactorsAsync(node, tab, date, caller, ct));

    /// <summary>
    /// The classes, sources, criticalities and environments present in the node, counted over the caller's visible objects
    /// only (S40 §8.2, §11.8; T254): what the Explore panel's "+ Filtro" offers.
    /// </summary>
    [HttpGet]
    [Route("Dimensions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexDimensionsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexDimensionsDto>> GetDimensions([FromQuery] string? node = null,
        [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the dimensions", caller => readService.GetDimensionsAsync(node, date, caller, ct));

    [HttpGet]
    [Route("Changes")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexChangesDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexChangesDto>> GetChanges([FromQuery] string? node = null, [FromQuery] DateOnly? to = null,
        [FromQuery] DateOnly? from = null, CancellationToken ct = default) =>
        Run("reading what moved", caller => readService.GetChangesAsync(node, from, to, caller, ct));

    [HttpGet]
    [Route("Contributions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexContributionsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<RiskIndexContributionsDto>> GetContributions([FromQuery] string? node = null,
        [FromQuery] DateOnly? date = null, [FromQuery] RiskIndexCategory? category = null, CancellationToken ct = default) =>
        Run("reading the contributions", caller => readService.GetContributionsAsync(node, date, category, caller, ct));

    [HttpGet]
    [Route("GovernanceIndicators")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexGovernanceIndicatorsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<RiskIndexGovernanceIndicatorsDto>> GetGovernanceIndicators([FromQuery] string? node = null,
        [FromQuery] DateOnly? date = null, CancellationToken ct = default) =>
        Run("reading the governance indicators", caller => readService.GetGovernanceIndicatorsAsync(node, date, caller, ct));

    /// <summary>
    /// The node's data quality (S40 §5.12, §8.2; T256): the always-visible strip and its detail. Fractions by weight and
    /// counts over the caller's visible objects; a value of the index, so the shadow rule applies.
    /// </summary>
    [HttpGet]
    [Route("Quality")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexQualityDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<RiskIndexQualityDto>> GetQuality([FromQuery] string? node = null, [FromQuery] DateOnly? date = null,
        CancellationToken ct = default) =>
        Run("reading the data quality", caller => readService.GetQualityAsync(node, date, caller, ct));

    [HttpGet]
    [Route("Publication")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexPublicationDto))]
    public Task<ActionResult<RiskIndexPublicationDto>> GetPublication(CancellationToken ct = default) =>
        Run("reading the publication", _ => readService.GetPublicationAsync(ct));

    /// <summary>Shadow ↔ published (S40 §6.4): <c>published</c> needs an active profile with the committee's decision.</summary>
    [HttpPut]
    [Route("Publication")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexPublicationDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexPublicationDto>> SetPublication([FromBody] RiskIndexPublicationRequest? request,
        CancellationToken ct = default) =>
        Run("changing the publication", caller => request is null
            ? throw RiskIndexException.BadRequest("invalid_state", "The body is { \"state\": 1 | 2 }.")
            : readService.SetPublicationAsync(request.State, caller, ct));
}
