using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Authentication.Scim;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Administration of SCIM provisioning credentials and their request audit
/// (Track 4 milestone 4.3.2).
///
/// Separate from <c>ScimController</c> on purpose: these endpoints are for a NetRisk administrator and
/// require the configuration permission, while the SCIM endpoints are for an IdP and are reachable only
/// with a provisioning token. A provisioning token must never be able to mint another one.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class ScimTokensController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IScimService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ScimTokenView>))]
    public Task<ActionResult<List<ScimTokenView>>> GetAll([FromQuery] bool includeRevoked = false)
    {
        GetUser();
        return RunAsync(() => service.GetTokensAsync(includeRevoked), "listing SCIM tokens");
    }

    /// <summary>
    /// Issues a provisioning credential. The response carries the secret — the only time it exists in
    /// readable form, because it is stored hashed and there is deliberately no path that shows it again.
    /// </summary>
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ScimTokenView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ScimTokenView>> Issue([FromBody] IssueScimTokenRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} issued a SCIM provisioning token named {Name}",
            user.Value, request?.Name);

        return CreatedAsync(
            () => service.IssueTokenAsync(request?.Name ?? "", request?.IdentityProviderId, user.Value),
            created => $"ScimTokens/{created.Id}", "issuing a SCIM token");
    }

    [HttpPost]
    [Route("{id:int}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScimTokenView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ScimTokenView>> Revoke(int id)
    {
        var user = GetUser();

        Logger.Warning("User:{User} revoked SCIM provisioning token {Token}", user.Value, id);

        return RunAsync(() => service.RevokeTokenAsync(id, user.Value), $"revoking SCIM token {id}");
    }

    /// <summary>
    /// The SCIM request audit, newest first. Part of the milestone rather than telemetry: "when did the
    /// IdP disable this account" is a question asked during incidents.
    /// </summary>
    [HttpGet]
    [Route("log")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ScimRequestLog>))]
    public Task<ActionResult<List<ScimRequestLog>>> GetLog([FromQuery] int limit = 200)
    {
        GetUser();
        return RunAsync(() => service.GetRequestLogAsync(limit), "reading the SCIM request audit");
    }
}

/// <summary>"Issue a provisioning token with this name."</summary>
public class IssueScimTokenRequest
{
    public string? Name { get; set; }

    /// <summary>Ties the token to one identity provider's claim/group mapping. Null uses the global one.</summary>
    public int? IdentityProviderId { get; set; }
}
