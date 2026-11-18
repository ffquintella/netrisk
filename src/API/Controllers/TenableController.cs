using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Integrations.Tenable;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Tenable Vulnerability Management (M56, T265; S40 §8.7), under <c>configuration</c> like every integration. A manual sync
/// is only queued here — the job host runs it under the global background principal, so the identity chain sees every
/// host whatever the caller's scope. Refusals are <c>{ error, message }</c>; no response ever carries a key.
/// </summary>
[ApiController]
[PermissionAuthorize("configuration")]
[Route("[controller]")]
public class TenableController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ITenableService service)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("products")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TenableProductDto>))]
    public ActionResult<IReadOnlyList<TenableProductDto>> GetProducts()
    {
        GetUser();
        return Ok(service.GetProducts());
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<TenableConnectionView>))]
    public Task<ActionResult<List<TenableConnectionView>>> GetAll(CancellationToken ct = default) =>
        Run("listing Tenable connections", () => service.GetConnectionsAsync(ct));

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TenableConnectionView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TenableConnectionView>> Get(int id, CancellationToken ct = default) =>
        Run($"reading Tenable connection {id}", () => service.GetConnectionAsync(id, ct));

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TenableConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TenableConnectionView>> Create([FromBody] TenableConnectionUpsert? request,
        CancellationToken ct = default)
    {
        var result = await Run("creating a Tenable connection", () => service.CreateConnectionAsync(Required(request), ct));
        if (result.Result is not OkObjectResult { Value: TenableConnectionView created }) return result;

        Logger.Information("User:{User} created Tenable connection {Id}", GetUser().Value, created.Id);
        return Created($"Tenable/{created.Id}", created);
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TenableConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TenableConnectionView>> Update(int id, [FromBody] TenableConnectionUpsert? request,
        CancellationToken ct = default) =>
        Run($"updating Tenable connection {id}", () => service.UpdateConnectionAsync(id, Required(request), ct));

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var result = await Run($"deleting Tenable connection {id}", async () =>
        {
            await service.DeleteConnectionAsync(id, ct);
            return true;
        });
        return result.Result is OkObjectResult ? NoContent() : result.Result!;
    }

    /// <summary>The probe of S40 §8.7: authentication, then the vulnerability and asset export privileges.</summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TenableTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<TenableTestResult>> Test(int id, CancellationToken ct = default) =>
        Run($"testing Tenable connection {id}", () => service.TestConnectionAsync(id, ct));

    /// <summary>Queues a manual sync (202); nothing runs in the API (S40 §8.7).</summary>
    [HttpPost]
    [Route("{id:int}/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(TenableSyncQueuedDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TenableSyncQueuedDto>> Sync(int id, CancellationToken ct = default)
    {
        var result = await Run($"queuing a sync of Tenable connection {id}", () => service.QueueSyncAsync(id, ct));
        if (result.Result is not OkObjectResult { Value: TenableSyncQueuedDto queued }) return result;

        Logger.Information("User:{User} queued a Tenable sync of connection {Connection} (log {Log})", GetUser().Value, id,
            queued.LogId);
        return Accepted(queued);
    }

    [HttpGet]
    [Route("log")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IntegrationSyncLog>))]
    public Task<ActionResult<List<IntegrationSyncLog>>> GetLog([FromQuery] int limit = 50, CancellationToken ct = default) =>
        Run("reading the Tenable sync log", () => service.GetSyncLogAsync(limit, ct));

    private async Task<ActionResult<T>> Run<T>(string operation, Func<Task<T>> action)
    {
        GetUser();
        try
        {
            return Ok(await action());
        }
        catch (TenableException ex)
        {
            Logger.Warning("{Operation} was refused: {Code}", operation, ex.Code);
            return StatusCode(ex.Status, new { error = ex.Code, message = ex.Message });
        }
        catch (DAL.Exceptions.EntityScopeViolationException)
        {
            throw; // the middleware answers 403
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error {Operation}", operation);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static TenableConnectionUpsert Required(TenableConnectionUpsert? request) =>
        request ?? throw TenableException.BadRequest(TenableErrors.InvalidParameter, "A body is required.");
}
