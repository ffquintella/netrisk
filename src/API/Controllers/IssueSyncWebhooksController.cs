using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Inbound issue-tracker webhooks (Track 4 milestone 4.2.3).
///
/// <see cref="AllowAnonymous"/> because the caller is GitHub or Jira, which cannot hold a NetRisk
/// session. Authenticity therefore rests entirely on the per-connection shared secret: a signature
/// GitHub and GitLab compute over the body, or a secret in the query string for Jira and Azure DevOps,
/// which do not sign. The verification is the provider's and it happens before the payload is acted on;
/// a body that does not verify is answered 401 and never reaches the sync logic.
///
/// The raw body is read as a string rather than model-bound: an HMAC is computed over the exact bytes
/// that arrived, and re-serializing a bound model produces different bytes and a signature that never
/// matches.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("[controller]")]
public class IssueSyncWebhooksController(
    ILogger logger,
    IIssueTrackerService service)
    : ControllerBase
{
    /// <summary>
    /// Receives one delivery for a connection.
    ///
    /// The response is deliberately uninformative on success: a tracker only needs 2xx, and telling an
    /// unauthenticated caller which finding changed would leak the register.
    /// </summary>
    [HttpPost]
    [Route("{connectionId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive(int connectionId, [FromQuery] string? secret = null)
    {
        string body;

        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in Request.Headers)
            headers[header.Key.ToLowerInvariant()] = header.Value.ToString();

        try
        {
            var result = await service.ApplyWebhookAsync(connectionId, body, headers, secret);

            logger.Information(
                "Issue-sync webhook for connection {Connection}: {Examined} examined, {Applied} applied",
                connectionId, result.Examined, result.Applied);

            return NoContent();
        }
        catch (WebhookAuthenticationException)
        {
            // Logged inside the service with the connection name; the response says nothing about why.
            return Unauthorized();
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.Message });
        }
        catch (Exception ex)
        {
            // 500 rather than swallowing: a tracker that retries a failed delivery is a feature, and
            // answering 204 to a request NetRisk could not process would discard the change silently.
            logger.Error(ex, "Issue-sync webhook for connection {Connection} failed", connectionId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
