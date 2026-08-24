using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using Model.Findings;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Issue, list and revoke scoped CI API tokens (Track 3 milestone 3.5.1).
///
/// Administration only, and closed to API tokens themselves: a pipeline credential that can mint
/// further credentials defeats the point of scoping it. The scope filter with no scopes is how that
/// is expressed.
/// </summary>
[PermissionAuthorize("configuration")]
[RequireApiScope]
[ApiController]
[Route("[controller]")]
public class ApiTokensController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IApiTokensService tokensService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// Tokens, newest first. No secret is included — none is stored, so there is nothing to leak
    /// here even by accident.
    /// </summary>
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ApiTokenView>))]
    public async Task<ActionResult<List<ApiTokenView>>> GetAll([FromQuery] bool includeRevoked = false)
    {
        var user = GetUser();

        try
        {
            var tokens = await tokensService.GetTokensAsync(includeRevoked);
            Logger.Information("User:{User} listed API tokens", user.Value);
            return Ok(tokens.Select(ApiTokenView.From).ToList());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing API tokens");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet]
    [Route("scopes")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string[]))]
    public ActionResult<string[]> GetScopes()
    {
        GetUser();
        return Ok(ApiTokenScopes.All);
    }

    /// <summary>
    /// Issues a token. The response carries the secret, once and only once: it is stored hashed and
    /// there is no endpoint that can show it again.
    /// </summary>
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IssuedApiToken))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IssuedApiToken>> Issue([FromBody] ApiTokenIssueRequest request)
    {
        var user = GetUser();

        if (request == null) return BadRequest("A token request is required.");

        try
        {
            // Defaults to acting as the requesting user. Naming another user is allowed — a shared
            // service account is the usual arrangement — but it is an explicit choice, not the
            // default, because a token silently acting as somebody else is a confusing audit trail.
            var actsAs = request.ActsAsUserId ?? user.Value;

            var issued = await tokensService.IssueAsync(request.Name ?? string.Empty,
                request.Scopes ?? string.Empty, actsAs, user.Value, request.ExpiresAt, request.EntityId,
                request.RateLimitPerMinute);

            Logger.Information("User:{User} issued API token {KeyId} acting as user {ActsAs}",
                user.Value, issued.KeyId, actsAs);

            return Created($"ApiTokens/{issued.Id}", issued);
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error issuing an API token");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Revokes a token, immediately and irreversibly.</summary>
    [HttpPost]
    [Route("{id}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ApiTokenView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiTokenView>> Revoke(int id)
    {
        var user = GetUser();

        try
        {
            var revoked = await tokensService.RevokeAsync(id, user.Value);
            Logger.Information("User:{User} revoked API token {KeyId}", user.Value, revoked.KeyId);
            return Ok(ApiTokenView.From(revoked));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error revoking API token {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>What the caller asks for when issuing a token.</summary>
public class ApiTokenIssueRequest
{
    public string? Name { get; set; }

    /// <summary>Comma-separated scopes. At least one is required.</summary>
    public string? Scopes { get; set; }

    /// <summary>The user the token acts as. Defaults to the caller.</summary>
    public int? ActsAsUserId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>Binds the token to one entity's data (Track 2.3).</summary>
    public int? EntityId { get; set; }

    public int? RateLimitPerMinute { get; set; }
}

/// <summary>
/// A token as shown in a list. A projection rather than the entity so the hash never leaves the
/// server — serialising <see cref="ApiToken"/> directly would put <c>secret_hash</c> on the wire.
/// </summary>
public class ApiTokenView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int? EntityId { get; set; }

    public int UserId { get; set; }

    public string? UserName { get; set; }

    public int? RateLimitPerMinute { get; set; }

    public bool IsUsable { get; set; }

    public static ApiTokenView From(ApiToken token) => new()
    {
        Id = token.Id,
        Name = token.Name,
        KeyId = token.KeyId,
        Scopes = ApiTokenScopes.Parse(token.Scopes),
        CreatedAt = token.CreatedAt,
        ExpiresAt = token.ExpiresAt,
        LastUsedAt = token.LastUsedAt,
        RevokedAt = token.RevokedAt,
        EntityId = token.EntityId,
        UserId = token.UserId,
        UserName = token.User?.Name,
        RateLimitPerMinute = token.RateLimitPerMinute,
        IsUsable = token.IsUsable(DateTime.UtcNow)
    };
}
