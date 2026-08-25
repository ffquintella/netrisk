using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Creating and linking developer tasks from a finding (Track 4 milestone 4.2.2).
///
/// A controller of its own rather than more actions on <c>VulnerabilitiesController</c>: these are the
/// operations a triager performs from the finding view, they all need the issue-tracker service, and
/// that controller is already the largest in the API.
/// </summary>
[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class FindingIssuesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIssueTrackerService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>The issues a finding is linked to, across every connection.</summary>
    [HttpGet]
    [Route("finding/{findingId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingIssueLinkView>))]
    public Task<ActionResult<List<FindingIssueLinkView>>> GetForFinding(int findingId)
    {
        GetUser();
        return RunAsync(() => service.GetLinksForFindingAsync(findingId),
            $"listing issue links for finding {findingId}");
    }

    /// <summary>
    /// The rendered title and body without creating anything — the preview step that makes a template
    /// editable with confidence.
    /// </summary>
    [HttpGet]
    [Route("preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IssueDraft))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IssueDraft>> Preview([FromQuery] int connectionId, [FromQuery] int findingId)
    {
        GetUser();
        return RunAsync(() => service.PreviewAsync(connectionId, findingId),
            $"previewing an issue for finding {findingId}");
    }

    /// <summary>
    /// Creates an issue for one finding and links it. Idempotent per (connection, finding): a repeated
    /// call returns the existing link rather than filing a duplicate ticket.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(FindingIssueLinkView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult<FindingIssueLinkView>> Create([FromBody] CreateIssueRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created an issue on connection {Connection} for finding {Finding}",
            user.Value, request?.ConnectionId, request?.FindingId);

        return CreatedAsync(
            () => service.CreateIssueAsync(request!.ConnectionId, request.FindingId, user.Value),
            created => $"FindingIssues/{created.Id}", "creating an issue from a finding");
    }

    /// <summary>
    /// Creates one issue per finding for a multi-selection. Per-finding failures are reported by absence
    /// from the result rather than by failing the request: filing thirty-nine of forty tickets is a
    /// better outcome than filing none.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("bulk")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingIssueLinkView>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<List<FindingIssueLinkView>>> CreateMany([FromBody] CreateIssuesRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created issues on connection {Connection} for {Count} finding(s)",
            user.Value, request?.ConnectionId, request?.FindingIds?.Count ?? 0);

        return RunAsync(
            () => service.CreateIssuesAsync(request!.ConnectionId, request.FindingIds ?? [], user.Value),
            "creating issues from a finding selection");
    }

    /// <summary>Links a finding to an issue that already exists, by key or URL.</summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("link")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(FindingIssueLinkView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FindingIssueLinkView>> LinkExisting([FromBody] LinkIssueRequest request)
    {
        var user = GetUser();

        return CreatedAsync(
            () => service.LinkExistingAsync(request!.ConnectionId, request.FindingId, request.IssueKey ?? "",
                user.Value),
            created => $"FindingIssues/{created.Id}", "linking an existing issue to a finding");
    }

    /// <summary>Removes a link. The external issue is left alone — NetRisk does not delete other people's tickets.</summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpDelete]
    [Route("{linkId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Unlink(int linkId)
    {
        var user = GetUser();

        Logger.Information("User:{User} unlinked issue link {Link}", user.Value, linkId);

        return RunAsync(() => service.UnlinkAsync(linkId), $"unlinking issue link {linkId}");
    }

    /// <summary>
    /// Pushes a finding's current lifecycle state onto its linked issues (4.2.3, outbound). Returns how
    /// many were updated; links whose last change came from the tracker are skipped, which is the loop
    /// protection.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("finding/{findingId:int}/push")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    public Task<ActionResult<int>> Push(int findingId, [FromQuery] FindingStatus status,
        [FromQuery] string? note = null)
    {
        var user = GetUser();

        Logger.Information("User:{User} pushed finding {Finding} state {Status} to its linked issues",
            user.Value, findingId, status);

        return RunAsync(() => service.PushFindingTransitionAsync(findingId, status, note),
            $"pushing finding {findingId} to its linked issues");
    }
}

/// <summary>"Create an issue for this finding on this connection."</summary>
public class CreateIssueRequest
{
    public int ConnectionId { get; set; }

    public int FindingId { get; set; }
}

/// <summary>The same for a multi-selection.</summary>
public class CreateIssuesRequest
{
    public int ConnectionId { get; set; }

    public List<int>? FindingIds { get; set; }
}

/// <summary>"Link this finding to an issue that already exists."</summary>
public class LinkIssueRequest
{
    public int ConnectionId { get; set; }

    public int FindingId { get; set; }

    /// <summary>Issue key or a full browser URL; both are what people paste.</summary>
    public string? IssueKey { get; set; }
}
