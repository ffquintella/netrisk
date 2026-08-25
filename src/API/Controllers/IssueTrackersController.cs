using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Issue-tracker connections, field mapping and the sync-conflict queue
/// (Track 4 milestones 4.2.1 and 4.2.3).
///
/// Connection administration needs the configuration permission because a connection holds a token
/// that can file issues in someone else's project. Reads of the connection list are open to anyone who
/// can see the vulnerability register, because the "create issue" dialog needs to offer them.
/// </summary>
[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class IssueTrackersController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIssueTrackerService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>The tracker providers this build supports and what each can do.</summary>
    [HttpGet]
    [Route("providers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IssueTrackerProviderView>))]
    public ActionResult<List<IssueTrackerProviderView>> GetProviders()
    {
        GetUser();

        return Ok(service.GetProviders()
            .Select(p => new IssueTrackerProviderView
            {
                Provider = p.Kind,
                Name = p.Name,
                Capabilities = p.Capabilities
            })
            .ToList());
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IssueTrackerConnectionView>))]
    public Task<ActionResult<List<IssueTrackerConnectionView>>> GetAll(
        [FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionsAsync(includeDisabled),
            "listing issue-tracker connections");
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IssueTrackerConnectionView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IssueTrackerConnectionView>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionAsync(id), $"reading issue-tracker connection {id}");
    }

    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IssueTrackerConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<IssueTrackerConnectionView>> Create(
        [FromBody] IssueTrackerConnectionRequest request)
    {
        var user = GetUser();

        return CreatedAsync(
            () => service.CreateConnectionAsync(request.Connection, request.Token, request.WebhookSecret,
                user.Value),
            created => $"IssueTrackers/{created.Id}", "creating an issue-tracker connection");
    }

    [PermissionAuthorize("configuration")]
    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IssueTrackerConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IssueTrackerConnectionView>> Update(int id,
        [FromBody] IssueTrackerConnectionRequest request)
    {
        var user = GetUser();

        return RunAsync(() =>
        {
            request.Connection.Id = id;
            // A null token means "unchanged" all the way down: the client never receives the stored one,
            // so it has nothing to send back.
            return service.UpdateConnectionAsync(request.Connection, request.Token, request.WebhookSecret,
                user.Value);
        }, $"updating issue-tracker connection {id}");
    }

    [PermissionAuthorize("configuration")]
    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteConnectionAsync(id),
            $"deleting issue-tracker connection {id}");
    }

    /// <summary>Verifies credentials and that the configured project is readable (4.2.1).</summary>
    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConnectionTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ConnectionTestResult>> Test(int id)
    {
        GetUser();
        return RunAsync(() => service.TestConnectionAsync(id), $"testing issue-tracker connection {id}");
    }

    [HttpGet]
    [Route("{id:int}/status-mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IssueStatusMappingView>))]
    public Task<ActionResult<List<IssueStatusMappingView>>> GetStatusMappings(int id)
    {
        GetUser();
        return RunAsync(() => service.GetStatusMappingsAsync(id),
            $"reading status mappings for connection {id}");
    }

    /// <summary>
    /// Replaces the connection's status mappings. Wholesale rather than per row, because the mapping is
    /// edited as a table and a partial save leaves a half-configured mapping applying to live findings.
    /// </summary>
    [PermissionAuthorize("configuration")]
    [HttpPut]
    [Route("{id:int}/status-mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IssueStatusMappingView>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<List<IssueStatusMappingView>>> SetStatusMappings(int id,
        [FromBody] List<IssueStatusMapping> mappings)
    {
        var user = GetUser();

        Logger.Information("User:{User} set {Count} status mapping(s) on connection {Connection}",
            user.Value, mappings?.Count ?? 0, id);

        return RunAsync(() => service.SetStatusMappingsAsync(id, mappings ?? []),
            $"setting status mappings for connection {id}");
    }

    /// <summary>Polls one connection now (4.2.3) — the manual equivalent of the polling job.</summary>
    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("{id:int}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IssueSyncResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IssueSyncResult>> Sync(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} polled issue-tracker connection {Connection}", user.Value, id);

        return RunAsync(() => service.PollConnectionAsync(id, user.Value),
            $"polling issue-tracker connection {id}");
    }

    /// <summary>
    /// Links where NetRisk and the tracker moved in incompatible directions — the conflict review queue
    /// (4.2.3). Last-writer-wins was already applied; this is the record that it happened.
    /// </summary>
    [HttpGet]
    [Route("conflicts")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingIssueLinkView>))]
    public Task<ActionResult<List<FindingIssueLinkView>>> GetConflicts()
    {
        GetUser();
        return RunAsync(service.GetConflictsAsync, "listing issue-sync conflicts");
    }

    [PermissionAuthorize("vulnerabilities_update")]
    [HttpPost]
    [Route("conflicts/{linkId:int}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FindingIssueLinkView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FindingIssueLinkView>> ResolveConflict(int linkId)
    {
        var user = GetUser();

        Logger.Information("User:{User} resolved the sync conflict on link {Link}", user.Value, linkId);

        return RunAsync(() => service.ResolveConflictAsync(linkId), $"resolving conflict on link {linkId}");
    }
}

/// <summary>
/// A connection plus its credentials, which are write-only.
///
/// The credentials are separate from the entity rather than fields on it, so there is no shape in which
/// a response could accidentally carry them: the view type the service returns has no room for them.
/// </summary>
public class IssueTrackerConnectionRequest
{
    public IssueTrackerConnection Connection { get; set; } = new();

    /// <summary>API token or PAT. Null on update means "leave the stored one alone".</summary>
    public string? Token { get; set; }

    /// <summary>Shared secret for inbound webhooks. Null on update means unchanged.</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>One tracker provider and its capability flags.</summary>
public class IssueTrackerProviderView
{
    public IssueTrackerProviderKind Provider { get; set; }

    public string Name { get; set; } = string.Empty;

    public IssueTrackerCapabilities? Capabilities { get; set; }
}
