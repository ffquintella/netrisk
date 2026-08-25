using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// SecurityScorecard connection management, posture synchronization and factor history
/// (Track 4 milestone 4.5).
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class SecurityScorecardController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ISecurityScorecardService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// The ten risk factors, so the trend chart can render a row per factor before any sync has produced
    /// one — a chart that only shows what happened to come back looks like data loss.
    /// </summary>
    [HttpGet]
    [Route("factors")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<string>))]
    public ActionResult<IReadOnlyList<string>> GetFactorNames()
    {
        GetUser();
        return Ok(SecurityScorecardFactors.All);
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SecurityScorecardConnectionView>))]
    public Task<ActionResult<List<SecurityScorecardConnectionView>>> GetAll(
        [FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionsAsync(includeDisabled),
            "listing SecurityScorecard connections");
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecurityScorecardConnectionView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SecurityScorecardConnectionView>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionAsync(id),
            $"reading SecurityScorecard connection {id}");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(SecurityScorecardConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<SecurityScorecardConnectionView>> Create(
        [FromBody] SecurityScorecardConnectionRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created SecurityScorecard connection {Name} for {Domain}",
            user.Value, request?.Connection?.Name, request?.Connection?.Domain);

        return CreatedAsync(() => service.CreateConnectionAsync(request!.Connection, request.ApiToken),
            created => $"SecurityScorecard/{created.Id}", "creating a SecurityScorecard connection");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecurityScorecardConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SecurityScorecardConnectionView>> Update(int id,
        [FromBody] SecurityScorecardConnectionRequest request)
    {
        GetUser();

        return RunAsync(() =>
        {
            request.Connection.Id = id;
            return service.UpdateConnectionAsync(request.Connection, request.ApiToken);
        }, $"updating SecurityScorecard connection {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteConnectionAsync(id),
            $"deleting SecurityScorecard connection {id}");
    }

    /// <summary>Token-and-domain test (4.5.1).</summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConnectionTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ConnectionTestResult>> Test(int id)
    {
        GetUser();
        return RunAsync(() => service.TestConnectionAsync(id),
            $"testing SecurityScorecard connection {id}");
    }

    [HttpPost]
    [Route("{id:int}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PostureSyncResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult<PostureSyncResult>> Sync(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} started a SecurityScorecard sync for connection {Connection}",
            user.Value, id);

        return RunAsync(() => service.SyncAsync(id), $"syncing SecurityScorecard connection {id}");
    }

    /// <summary>
    /// The stored factor history for the trend chart, newest first. Includes the synthetic overall row,
    /// flagged, so the whole posture history is one query.
    /// </summary>
    [HttpGet]
    [Route("{id:int}/history")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SecurityScorecardFactor>))]
    public Task<ActionResult<List<SecurityScorecardFactor>>> GetHistory(int id,
        [FromQuery] int limit = 500)
    {
        GetUser();
        return RunAsync(() => service.GetFactorHistoryAsync(id, limit),
            $"reading the factor history of connection {id}");
    }

    [HttpGet]
    [Route("log")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IntegrationSyncLog>))]
    public Task<ActionResult<List<IntegrationSyncLog>>> GetLog([FromQuery] int limit = 50)
    {
        GetUser();
        return RunAsync(() => service.GetSyncLogAsync(limit), "reading the SecurityScorecard sync log");
    }
}

/// <summary>A connection plus its write-only API token.</summary>
public class SecurityScorecardConnectionRequest
{
    public SecurityScorecardConnection Connection { get; set; } = new();

    /// <summary>Null on update means "leave the stored token alone".</summary>
    public string? ApiToken { get; set; }
}
