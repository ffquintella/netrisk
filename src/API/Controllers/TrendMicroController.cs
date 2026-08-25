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
/// Trend Micro Vision One connection management and synchronization
/// (Track 4 milestone 4.4).
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class TrendMicroController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ITrendMicroService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// The Vision One regions and their API roots. Offered as a list because a key issued in one region
    /// is rejected by every other, and a free-text base URL is how that becomes a support call.
    /// </summary>
    [HttpGet]
    [Route("regions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyDictionary<string, string>))]
    public ActionResult<IReadOnlyDictionary<string, string>> GetRegions()
    {
        GetUser();
        return Ok(service.GetRegions());
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TrendMicroConnectionView>))]
    public Task<ActionResult<List<TrendMicroConnectionView>>> GetAll(
        [FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionsAsync(includeDisabled),
            "listing Vision One connections");
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TrendMicroConnectionView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TrendMicroConnectionView>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionAsync(id), $"reading Vision One connection {id}");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TrendMicroConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<TrendMicroConnectionView>> Create([FromBody] TrendMicroConnectionRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created Vision One connection {Name}", user.Value,
            request?.Connection?.Name);

        return CreatedAsync(() => service.CreateConnectionAsync(request!.Connection, request.ApiKey),
            created => $"TrendMicro/{created.Id}", "creating a Vision One connection");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TrendMicroConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TrendMicroConnectionView>> Update(int id,
        [FromBody] TrendMicroConnectionRequest request)
    {
        GetUser();

        return RunAsync(() =>
        {
            request.Connection.Id = id;
            return service.UpdateConnectionAsync(request.Connection, request.ApiKey);
        }, $"updating Vision One connection {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteConnectionAsync(id), $"deleting Vision One connection {id}");
    }

    /// <summary>The region-aware test connection utility (4.4.1).</summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConnectionTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ConnectionTestResult>> Test(int id)
    {
        GetUser();
        return RunAsync(() => service.TestConnectionAsync(id), $"testing Vision One connection {id}");
    }

    /// <summary>Runs a full sync now — the manual equivalent of the daily job.</summary>
    [HttpPost]
    [Route("{id:int}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PostureSyncResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult<PostureSyncResult>> Sync(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} started a Vision One sync for connection {Connection}",
            user.Value, id);

        return RunAsync(() => service.SyncAsync(id), $"syncing Vision One connection {id}");
    }

    [HttpGet]
    [Route("log")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IntegrationSyncLog>))]
    public Task<ActionResult<List<IntegrationSyncLog>>> GetLog([FromQuery] int limit = 50)
    {
        GetUser();
        return RunAsync(() => service.GetSyncLogAsync(limit), "reading the Vision One sync log");
    }
}

/// <summary>A connection plus its write-only API key.</summary>
public class TrendMicroConnectionRequest
{
    public TrendMicroConnection Connection { get; set; } = new();

    /// <summary>Null on update means "leave the stored key alone".</summary>
    public string? ApiKey { get; set; }
}
