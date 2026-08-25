using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Shared exception mapping for the Track 4 integration controllers.
///
/// The Track 4 services throw a small, well-defined set of exceptions, and every one of the ten
/// controllers below would otherwise repeat the same five-arm try/catch on every action. Factoring it
/// out is not only shorter: it is what guarantees that a
/// <see cref="Model.Exceptions.IntegrationRequestException"/> is a 502 everywhere rather than a 500 in
/// whichever controller was written last.
///
/// The mapping itself:
///  * <see cref="InvalidParameterException"/> → 400, with the parameter named;
///  * <see cref="DataNotFoundException"/> → 404;
///  * <see cref="SecretProtectionException"/> → 409 — the stored credential cannot be decrypted, which
///    is a state the operator has to fix by re-entering it, not a bad request;
///  * <see cref="IntegrationRequestException"/> → 502 — the failure is upstream, and saying 500 would
///    point the operator at NetRisk;
///  * <see cref="WebhookAuthenticationException"/> → 401.
/// </summary>
public abstract class IntegrationsControllerBase(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>Runs an action that returns a value, mapping domain exceptions onto status codes.</summary>
    protected async Task<ActionResult<T>> RunAsync<T>(Func<Task<T>> action, string description)
    {
        try
        {
            return Ok(await action());
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (DataNotFoundException ex)
        {
            return NotFound(ex.InnerException?.Message ?? ex.Message);
        }
        catch (SecretProtectionException ex)
        {
            return Conflict(new { error = "secret_undecryptable", ex.Message });
        }
        catch (IntegrationRequestException ex)
        {
            Logger.Warning("{Description} failed upstream at {Provider}: {Message}",
                description, ex.Provider, ex.Message);

            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "upstream_failure", ex.Provider, ex.Message });
        }
        catch (WebhookAuthenticationException ex)
        {
            return Unauthorized(new { error = "webhook_unauthenticated", ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error: {Description}", description);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The same mapping for an action with no return value; success is 204.</summary>
    protected async Task<IActionResult> RunAsync(Func<Task> action, string description)
    {
        var result = await RunAsync<bool>(async () =>
        {
            await action();
            return true;
        }, description);

        // Unwrapped rather than returned as Ok(true): a delete that answers "true" is a body no client
        // wants to parse, and 204 is what the verb means.
        return result.Result is OkObjectResult ? NoContent() : result.Result!;
    }

    /// <summary>
    /// The same mapping, but 201 with a location on success — for the create actions.
    /// </summary>
    protected async Task<ActionResult<T>> CreatedAsync<T>(Func<Task<T>> action, Func<T, string> location,
        string description)
    {
        var result = await RunAsync(action, description);

        if (result.Result is not OkObjectResult ok || ok.Value is not T value) return result;

        return Created(location(value), value);
    }
}
