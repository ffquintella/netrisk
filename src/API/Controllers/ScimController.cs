using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Authentication.Scim;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// SCIM 2.0 provisioning endpoints (Track 4 milestone 4.3.2).
///
/// Route is <c>/scim/v2/...</c> rather than the controller-name convention the rest of the API uses,
/// because SCIM clients construct paths from the RFC and cannot be told to use a different shape.
///
/// Authorization is the provisioning-token role and nothing else, so a SCIM credential reaches exactly
/// these endpoints. Every request — including a refused one — is written to the audit log before the
/// response goes out: the record of an IdP disabling an account is part of the feature, not telemetry.
/// </summary>
[Authorize(Roles = ScimAuthenticationHandler.ScimRole)]
[ApiController]
[Route("scim/v2")]
[Produces("application/scim+json", "application/json")]
public class ScimController(ILogger logger, IScimService service) : ControllerBase
{
    /// <summary>
    /// The SCIM service-provider configuration document. Provisioning clients read it to discover which
    /// operations are supported, and an IdP that cannot read it sometimes refuses to configure at all.
    /// </summary>
    [HttpGet]
    [Route("ServiceProviderConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetServiceProviderConfig()
    {
        return Ok(new
        {
            schemas = new[] { ScimSchemas.ServiceProviderConfig },
            patch = new { supported = true },
            bulk = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
            filter = new { supported = true, maxResults = 200 },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
            authenticationSchemes = new[]
            {
                new
                {
                    type = "oauthbearertoken",
                    name = "OAuth Bearer Token",
                    description = "A NetRisk SCIM provisioning token, presented as an HTTP bearer token.",
                    primary = true
                }
            }
        });
    }

    // --- users ------------------------------------------------------------------------------

    [HttpGet]
    [Route("Users")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimListResponse<ScimUser>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ListUsers([FromQuery] string? filter, [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100) =>
        RunAsync(() => service.ListUsersAsync(filter, startIndex, count), filter, "listed users");

    [HttpGet]
    [Route("Users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimUser))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetUser(string id) =>
        RunAsync(() => service.GetUserAsync(id), id, "read user");

    [HttpPost]
    [Route("Users")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ScimUser))]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateUser([FromBody] ScimUser user) =>
        RunAsync(async () =>
        {
            var created = await service.CreateUserAsync(user);
            // 201 with a Location, which is what an IdP stores as the resource's URL.
            Response.Headers.Location = $"/scim/v2/Users/{created.Id}";
            Response.StatusCode = StatusCodes.Status201Created;
            return created;
        }, user?.UserName, "created user");

    [HttpPut]
    [Route("Users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimUser))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ReplaceUser(string id, [FromBody] ScimUser user) =>
        RunAsync(() => service.ReplaceUserAsync(id, user), id,
            $"replaced user (active={user?.Active})");

    /// <summary>
    /// RFC 7644 PATCH. This is the operation that matters: both Entra ID and Okta deprovision by
    /// patching <c>active</c> to false, and an implementation that only supports PUT never disables
    /// anyone.
    /// </summary>
    [HttpPatch]
    [Route("Users/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimUser))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> PatchUser(string id, [FromBody] ScimPatchRequest patch) =>
        RunAsync(() => service.PatchUserAsync(id, patch), id,
            "patched user: " + string.Join(", ",
                (patch?.Operations ?? []).Select(o => $"{o.Op} {o.Path ?? "(no path)"}")));

    /// <summary>
    /// SCIM DELETE. Deactivates rather than deleting: a NetRisk user is referenced by risks, findings
    /// and audit history, and hard-deleting them would erase attribution. The IdP sees the resource
    /// gone, which is what it asked for.
    /// </summary>
    [HttpDelete]
    [Route("Users/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var result = await RunAsync<object?>(async () =>
        {
            await service.DeactivateUserAsync(id);
            return null;
        }, id, "deactivated user");

        // Ok() with no value is an OkResult, not an OkObjectResult — both mean the operation succeeded,
        // and the verb's answer is 204 either way.
        return result is OkResult or OkObjectResult ? NoContent() : result;
    }

    // --- groups -----------------------------------------------------------------------------

    [HttpGet]
    [Route("Groups")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimListResponse<ScimGroup>))]
    public Task<IActionResult> ListGroups([FromQuery] string? filter, [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100) =>
        RunAsync(() => service.ListGroupsAsync(filter, startIndex, count), filter, "listed groups");

    [HttpGet]
    [Route("Groups/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimGroup))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetGroup(string id) =>
        RunAsync(() => service.GetGroupAsync(id), id, "read group");

    [HttpPost]
    [Route("Groups")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ScimGroup))]
    public Task<IActionResult> CreateGroup([FromBody] ScimGroup group) =>
        RunAsync(async () =>
        {
            var created = await service.CreateGroupAsync(group);
            Response.Headers.Location = $"/scim/v2/Groups/{created.Id}";
            Response.StatusCode = StatusCodes.Status201Created;
            return created;
        }, group?.DisplayName, "created group");

    [HttpPut]
    [Route("Groups/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimGroup))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ReplaceGroup(string id, [FromBody] ScimGroup group) =>
        RunAsync(() => service.ReplaceGroupAsync(id, group), id, "replaced group");

    [HttpPatch]
    [Route("Groups/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimGroup))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> PatchGroup(string id, [FromBody] ScimPatchRequest patch) =>
        RunAsync(() => service.PatchGroupAsync(id, patch), id,
            "patched group: " + string.Join(", ",
                (patch?.Operations ?? []).Select(o => $"{o.Op} {o.Path ?? "(no path)"}")));

    [HttpDelete]
    [Route("Groups/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        var result = await RunAsync<object?>(async () =>
        {
            await service.DeleteGroupAsync(id);
            return null;
        }, id, "deleted group");

        // Ok() with no value is an OkResult, not an OkObjectResult — both mean the operation succeeded,
        // and the verb's answer is 204 either way.
        return result is OkResult or OkObjectResult ? NoContent() : result;
    }

    // --- plumbing ---------------------------------------------------------------------------

    /// <summary>
    /// Runs a SCIM operation, translating domain exceptions into SCIM error documents and writing the
    /// audit row.
    ///
    /// SCIM clients parse the error body, not just the status: a 409 without
    /// <c>scimType: uniqueness</c> is one an IdP retries forever instead of switching to PATCH.
    /// </summary>
    private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action, string? target, string outcome)
    {
        var tokenId = TokenId();

        try
        {
            var value = await action();

            var status = Response.StatusCode is StatusCodes.Status201Created
                ? StatusCodes.Status201Created
                : StatusCodes.Status200OK;

            await service.LogRequestAsync(tokenId, Request.Method, Request.Path, status, target, outcome);

            return value == null ? Ok() : Ok(value);
        }
        catch (DataNotFoundException ex)
        {
            await service.LogRequestAsync(tokenId, Request.Method, Request.Path,
                StatusCodes.Status404NotFound, target, "not found");

            return NotFound(ScimError.Create(StatusCodes.Status404NotFound,
                ex.InnerException?.Message ?? ex.Message));
        }
        catch (InvalidParameterException ex)
        {
            // A duplicate userName is the one case an IdP branches on, so it is answered 409 with the
            // scimType the RFC defines for it rather than a generic 400.
            var uniqueness = ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

            var status = uniqueness ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;

            var scimType = uniqueness
                ? "uniqueness"
                : ex.ParameterName == "filter" ? "invalidFilter" : "invalidValue";

            await service.LogRequestAsync(tokenId, Request.Method, Request.Path, status, target,
                $"rejected: {ex.Message}");

            return StatusCode(status, ScimError.Create(status, ex.Message, scimType));
        }
        catch (Exception ex)
        {
            logger.Error(ex, "SCIM {Method} {Path} failed", Request.Method, Request.Path);

            await service.LogRequestAsync(tokenId, Request.Method, Request.Path,
                StatusCodes.Status500InternalServerError, target, "internal error");

            return StatusCode(StatusCodes.Status500InternalServerError,
                ScimError.Create(StatusCodes.Status500InternalServerError,
                    "The request could not be processed."));
        }
    }

    /// <summary>
    /// The authenticating token's id, or null when there is no principal.
    ///
    /// Null-tolerant because this is an audit attribution, not authentication: a request that somehow
    /// arrives without a principal has already been refused by the scheme, and an unattributable audit
    /// row is far better than an exception thrown while writing one.
    /// </summary>
    private int? TokenId()
    {
        var claim = ControllerContext.HttpContext?.User?
            .FindFirst(ScimAuthenticationHandler.TokenIdClaimType)?.Value;

        return int.TryParse(claim, out var parsed) ? parsed : null;
    }
}
