using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Authentication.Federation;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Identity-provider configuration and the OIDC/SAML sign-in flows
/// (Track 4 milestone 4.3.1).
///
/// Three of these endpoints are anonymous by necessity — the caller has no session yet, which is the
/// point of signing in. They are the provider list a sign-in screen renders, the sign-in start, and the
/// completion. Each is safe to expose: the list carries only a name and a protocol, the start returns a
/// URL the IdP will validate anyway, and the completion is worthless without an authorization code the
/// IdP issued.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class IdentityProvidersController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IIdentityProvidersService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IdentityProviderView>))]
    public Task<ActionResult<List<IdentityProviderView>>> GetAll([FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetProvidersAsync(includeDisabled), "listing identity providers");
    }

    /// <summary>
    /// The providers a sign-in screen may offer. Anonymous, and deliberately reduced to a name and a
    /// protocol — the claim and group mappings are configuration, not something to enumerate publicly.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [Route("available")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<IdentityProviderView>))]
    public async Task<ActionResult<List<IdentityProviderView>>> GetAvailable()
    {
        return Ok(await service.GetEnabledForSignInAsync());
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IdentityProviderView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IdentityProviderView>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetProviderAsync(id), $"reading identity provider {id}");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(IdentityProviderView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<IdentityProviderView>> Create([FromBody] IdentityProviderRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created identity provider {Name}", user.Value,
            request?.Provider?.Name);

        return CreatedAsync(() => service.CreateProviderAsync(request!.Provider, request.ClientSecret),
            created => $"IdentityProviders/{created.Id}", "creating an identity provider");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IdentityProviderView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IdentityProviderView>> Update(int id,
        [FromBody] IdentityProviderRequest request)
    {
        GetUser();

        return RunAsync(() =>
        {
            request.Provider.Id = id;
            return service.UpdateProviderAsync(request.Provider, request.ClientSecret);
        }, $"updating identity provider {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteProviderAsync(id), $"deleting identity provider {id}");
    }

    /// <summary>Reads the discovery document or the SAML metadata and reports what it found.</summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConnectionTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ConnectionTestResult>> Test(int id)
    {
        GetUser();
        return RunAsync(() => service.TestProviderAsync(id), $"testing identity provider {id}");
    }

    /// <summary>
    /// The SP metadata to hand to the IdP, so nobody has to retype an entity id or an ACS URL.
    /// </summary>
    [HttpGet]
    [Route("{id:int}/saml/metadata")]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServiceProviderMetadata(int id)
    {
        GetUser();

        var result = await RunAsync(() => service.GetServiceProviderMetadataAsync(id),
            $"building SP metadata for provider {id}");

        if (result.Result is not OkObjectResult ok) return result.Result!;

        return Content((string)ok.Value!, "application/xml");
    }

    // --- sign-in flows ----------------------------------------------------------------------

    /// <summary>
    /// Starts an OIDC sign-in. Anonymous: the caller has no session, which is what it is asking for.
    ///
    /// The redirect URI must be a loopback address or one configured in <c>app:allowedRedirectUris</c>,
    /// so this cannot be used as an open redirector to collect authorization codes.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("{id:int}/oidc/signin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FederatedSignInRequest))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FederatedSignInRequest>> BeginOidc(int id,
        [FromBody] BeginSignInRequest request)
    {
        return RunAsync(() => service.BeginOidcSignInAsync(id, request?.RedirectUri ?? ""),
            $"beginning an OIDC sign-in with provider {id}");
    }

    /// <summary>
    /// Completes an OIDC sign-in by exchanging the authorization code. Returns the resolved account or a
    /// stated reason; issuing the NetRisk session token is <c>AuthenticationController</c>'s job.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("oidc/callback")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FederatedSignInResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<FederatedSignInResult>> CompleteOidc([FromBody] OidcCallbackRequest request)
    {
        return RunAsync(() => service.CompleteOidcSignInAsync(request?.State ?? "", request?.Code ?? ""),
            "completing an OIDC sign-in");
    }

    /// <summary>Starts a SAML SP-initiated sign-in and returns the redirect URL to open.</summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("{id:int}/saml/signin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FederatedSignInRequest))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FederatedSignInRequest>> BeginSaml(int id)
    {
        return RunAsync(() => service.BeginSamlSignInAsync(id),
            $"beginning a SAML sign-in with provider {id}");
    }

    /// <summary>
    /// The assertion consumer service. Accepts the form POST the IdP's browser redirect performs, which
    /// is why it takes form fields rather than a JSON body.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    [Route("{id:int}/saml/acs")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FederatedSignInResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<FederatedSignInResult>> AssertionConsumer(int id,
        [FromForm(Name = "SAMLResponse")] string samlResponse,
        [FromForm(Name = "RelayState")] string? relayState)
    {
        return RunAsync(() => service.CompleteSamlSignInAsync(samlResponse ?? "", relayState),
            "completing a SAML sign-in");
    }
}

/// <summary>A provider plus its write-only client secret.</summary>
public class IdentityProviderRequest
{
    public IdentityProvider Provider { get; set; } = new();

    /// <summary>Null on update means "leave the stored secret alone".</summary>
    public string? ClientSecret { get; set; }
}

/// <summary>Where the IdP should send the browser back to.</summary>
public class BeginSignInRequest
{
    public string? RedirectUri { get; set; }
}

/// <summary>What the loopback listener received from the IdP.</summary>
public class OidcCallbackRequest
{
    public string? State { get; set; }

    public string? Code { get; set; }
}
