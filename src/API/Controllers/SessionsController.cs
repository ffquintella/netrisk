using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Per-session sign-out (security finding NR-2026-028).
///
/// Track 7 delivered <em>mass</em> revocation — a password change invalidates every outstanding token
/// for the account, and disabling a user takes effect on the next request — but there was no way to
/// end one session. <c>SAMLLogout</c> returned the string "Teste". Tokens have carried a <c>jti</c>
/// since that track specifically so this could be added without another token-format change.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class SessionsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ITokenRevocationService revocation)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// Revokes the token this request was made with.
    ///
    /// Deliberately takes no token in the body. A caller who could name an arbitrary <c>jti</c> could
    /// sign out another user's session, and "log me out" needs no parameter — the token is already in
    /// the Authorization header.
    /// </summary>
    [HttpPost]
    [Route("Logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Logout()
    {
        var user = GetUser();

        var (jti, expiresAt) = ReadCurrentToken();

        if (jti is null)
            return BadRequest(new
            {
                error = "no_revocable_token",
                message = "This request did not present a bearer token carrying a jti claim, so there " +
                          "is no single session to revoke. Change the account's password to invalidate " +
                          "every session it has."
            });

        await revocation.RevokeAsync(jti, user.Value, expiresAt, "user signed out");

        Logger.Information("User:{User} signed out session {Jti}", user.Value, jti);

        return Ok();
    }

    /// <summary>
    /// Whether the token this request carries has been revoked. Exists so a client can verify a
    /// sign-out actually took effect rather than assuming it did.
    /// </summary>
    [HttpGet]
    [Route("Current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> Current()
    {
        GetUser();

        var (jti, expiresAt) = ReadCurrentToken();

        return Ok(new
        {
            jti,
            expiresAt = jti is null ? (DateTime?)null : expiresAt,
            revoked = jti is not null && await revocation.IsRevokedAsync(jti)
        });
    }

    /// <summary>
    /// The <c>jti</c> and expiry of the bearer token on this request.
    ///
    /// Read from the raw header rather than from <c>User.Claims</c>: the authentication handler
    /// builds its identity from a small set of claims and does not carry <c>jti</c> across, and
    /// re-parsing the token here is both cheaper and less coupled than changing what it projects.
    /// The token's signature was already validated to get this far, so reading it unvalidated is safe.
    /// </summary>
    private (string? Jti, DateTime ExpiresAt) ReadCurrentToken()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            return (null, default);

        try
        {
            var raw = header["Bearer ".Length..].Trim();
            var token = new JwtSecurityTokenHandler().ReadJwtToken(raw);

            var jti = token.Id;
            if (string.IsNullOrWhiteSpace(jti))
                jti = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

            return (string.IsNullOrWhiteSpace(jti) ? null : jti, token.ValidTo);
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not read the presented token to revoke it: {Message}", ex.Message);
            return (null, default);
        }
    }
}
