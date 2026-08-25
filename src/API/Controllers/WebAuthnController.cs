using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Authentication.WebAuthn;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// WebAuthn registration and authentication, the hardware-factor policy, and recovery codes
/// (Track 4 milestone 4.3.3).
///
/// The assertion endpoints are anonymous because a hardware factor is presented *during* sign-in, when
/// there is no session yet. That is safe: an assertion is worthless without the challenge NetRisk
/// issued, the challenge is single-use, and the credential's signature is verified against a stored
/// public key. The registration endpoints are authenticated, because enrolling a key for someone else's
/// account is exactly what an attacker would want.
/// </summary>
[ApiController]
[Route("[controller]")]
public class WebAuthnController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IWebAuthnService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>The caller's own registered authenticators.</summary>
    [HttpGet]
    [Route("credentials")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<WebAuthnCredentialView>))]
    public Task<ActionResult<List<WebAuthnCredentialView>>> GetMine(
        [FromQuery] bool includeRevoked = false)
    {
        var user = GetUser();
        return RunAsync(() => service.GetCredentialsAsync(user.Value, includeRevoked),
            "listing WebAuthn credentials");
    }

    /// <summary>
    /// Another user's authenticators. Administrator-only, because it is how an administrator sees whether
    /// a colleague satisfies the hardware-factor policy.
    /// </summary>
    [PermissionAuthorize("configuration")]
    [HttpGet]
    [Route("credentials/user/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<WebAuthnCredentialView>))]
    public Task<ActionResult<List<WebAuthnCredentialView>>> GetForUser(int userId,
        [FromQuery] bool includeRevoked = false)
    {
        GetUser();
        return RunAsync(() => service.GetCredentialsAsync(userId, includeRevoked),
            $"listing WebAuthn credentials for user {userId}");
    }

    /// <summary>Starts a registration ceremony for the calling user.</summary>
    [HttpPost]
    [Route("register/begin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WebAuthnCeremonyOptions))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<WebAuthnCeremonyOptions>> BeginRegistration(
        [FromBody] BeginRegistrationRequest? request)
    {
        var user = GetUser();

        return RunAsync(() => service.BeginRegistrationAsync(user.Value, request?.Name),
            "beginning a WebAuthn registration");
    }

    /// <summary>Completes a registration ceremony and stores the credential.</summary>
    [HttpPost]
    [Route("register/complete")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WebAuthnRegistrationResult))]
    public Task<ActionResult<WebAuthnRegistrationResult>> CompleteRegistration(
        [FromBody] CompleteCeremonyRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} completed a WebAuthn registration ceremony", user.Value);

        return RunAsync(
            () => service.CompleteRegistrationAsync(request?.CeremonyId ?? "", request?.Response ?? ""),
            "completing a WebAuthn registration");
    }

    /// <summary>
    /// Starts an authentication ceremony. Anonymous: this runs during sign-in.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("assert/begin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WebAuthnCeremonyOptions))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<WebAuthnCeremonyOptions>> BeginAssertion([FromBody] BeginAssertionRequest? request)
    {
        return RunAsync(() => service.BeginAssertionAsync(request?.UserId),
            "beginning a WebAuthn assertion");
    }

    /// <summary>
    /// Completes an authentication ceremony. Anonymous for the same reason, and safe because the
    /// challenge is single-use and the signature is verified against a stored key.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("assert/complete")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WebAuthnAssertionResult))]
    public Task<ActionResult<WebAuthnAssertionResult>> CompleteAssertion(
        [FromBody] CompleteCeremonyRequest request)
    {
        return RunAsync(
            () => service.CompleteAssertionAsync(request?.CeremonyId ?? "", request?.Response ?? ""),
            "completing a WebAuthn assertion");
    }

    /// <summary>
    /// Withdraws an authenticator. An administrator may revoke anyone's; a user may revoke their own,
    /// which the service enforces by id ownership.
    /// </summary>
    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("credentials/{credentialId:int}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WebAuthnCredentialView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<WebAuthnCredentialView>> Revoke(int credentialId)
    {
        var user = GetUser();

        return RunAsync(() => service.RevokeCredentialAsync(credentialId, user.Value),
            $"revoking WebAuthn credential {credentialId}");
    }

    /// <summary>
    /// Generates recovery codes for a user, invalidating any unused ones. Administrator-only and audited:
    /// a recovery code is a way past the hardware factor.
    /// </summary>
    [PermissionAuthorize("configuration")]
    [HttpPost]
    [Route("recovery-codes/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RecoveryCodeBatch))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RecoveryCodeBatch>> GenerateRecoveryCodes(int userId,
        [FromQuery] int count = 10)
    {
        var user = GetUser();

        Logger.Warning("User:{Actor} generated MFA recovery codes for user {Target}", user.Value, userId);

        return RunAsync(() => service.GenerateRecoveryCodesAsync(userId, user.Value, count),
            $"generating recovery codes for user {userId}");
    }

    /// <summary>
    /// Redeems a recovery code during sign-in. Anonymous, single-use, and rate-limited by the fact that a
    /// wrong code simply returns false without saying which part was wrong.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("recovery-codes/redeem")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(bool))]
    public Task<ActionResult<bool>> RedeemRecoveryCode([FromBody] RedeemRecoveryCodeRequest request)
    {
        return RunAsync(() => service.RedeemRecoveryCodeAsync(request?.UserId ?? 0, request?.Code ?? ""),
            "redeeming an MFA recovery code");
    }

    /// <summary>Whether the hardware-factor policy applies to the calling user and is satisfied.</summary>
    [HttpGet]
    [Route("status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HardwareFactorStatus))]
    public Task<ActionResult<HardwareFactorStatus>> GetStatus()
    {
        var user = GetUser();
        return RunAsync(() => service.GetHardwareFactorStatusAsync(user.Value),
            "reading the hardware-factor status");
    }

    /// <summary>The same for another user. Administrator-only.</summary>
    [PermissionAuthorize("configuration")]
    [HttpGet]
    [Route("status/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HardwareFactorStatus))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<HardwareFactorStatus>> GetStatusFor(int userId)
    {
        GetUser();
        return RunAsync(() => service.GetHardwareFactorStatusAsync(userId),
            $"reading the hardware-factor status of user {userId}");
    }
}

/// <summary>An optional label for the authenticator being enrolled.</summary>
public class BeginRegistrationRequest
{
    public string? Name { get; set; }
}

/// <summary>Which account the assertion is for; null starts a discoverable-credential ceremony.</summary>
public class BeginAssertionRequest
{
    public int? UserId { get; set; }
}

/// <summary>The browser's ceremony response, passed through verbatim.</summary>
public class CompleteCeremonyRequest
{
    public string? CeremonyId { get; set; }

    /// <summary>The raw WebAuthn JSON from the browser. Not re-modelled here — the library parses it.</summary>
    public string? Response { get; set; }
}

/// <summary>"Redeem this recovery code for this user."</summary>
public class RedeemRecoveryCodeRequest
{
    public int UserId { get; set; }

    public string? Code { get; set; }
}
