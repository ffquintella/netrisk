using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Secrets;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// External secret vaults: the connections, and the metadata an administrator picks a secret from.
///
/// Note what this controller does <b>not</b> have: an endpoint that returns a secret value. That is
/// the security property the whole feature rests on. Values are resolved on the server, at the moment
/// an integration needs one, and never travel to a client — so a compromised desktop session, or a
/// stolen administrator token, buys the ability to see which secrets exist and to re-point a field at
/// one, not the ability to read the estate's credentials out of NetRisk.
///
/// Everything here is <c>configuration</c>-gated, which is the same permission that already governs
/// the integration connections these secrets feed.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class SecretVaultsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    ISecretVaultService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>
    /// Whether the vault feature is usable: at least one enabled secret-vault plugin and one enabled
    /// connection. The desktop client asks once and hides the picker buttons when it is false, so an
    /// installation with no vault sees no new UI at all.
    /// </summary>
    [HttpGet]
    [Route("available")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(bool))]
    public Task<ActionResult<bool>> IsAvailable()
    {
        GetUser();
        return RunAsync(service.IsAvailableAsync, "checking whether a secret vault is available");
    }

    /// <summary>The installed and enabled secret-vault plugins, for the connection editor's picker.</summary>
    [HttpGet]
    [Route("plugins")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SecretVaultPluginInfo>))]
    public Task<ActionResult<List<SecretVaultPluginInfo>>> GetPlugins()
    {
        GetUser();
        return RunAsync(service.GetAvailablePluginsAsync, "listing the installed secret-vault plugins");
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SecretVaultConnectionView>))]
    public Task<ActionResult<List<SecretVaultConnectionView>>> GetAll([FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionsAsync(includeDisabled), "listing vault connections");
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecretVaultConnectionView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SecretVaultConnectionView>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetConnectionAsync(id), $"reading vault connection {id}");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(SecretVaultConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<SecretVaultConnectionView>> Create([FromBody] SecretVaultConnectionRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} created vault connection {Name}", user.Value,
            request?.Connection?.Name);

        return CreatedAsync(
            () => service.CreateConnectionAsync(request!.Connection, request.ApiKey, user.Value),
            created => $"SecretVaults/{created.Id}", "creating a vault connection");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecretVaultConnectionView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SecretVaultConnectionView>> Update(int id,
        [FromBody] SecretVaultConnectionRequest request)
    {
        var user = GetUser();

        Logger.Information("User:{User} updated vault connection {Id}", user.Value, id);

        // The route id wins over the body's. A payload that disagrees with its own URL is either a
        // client bug or an attempt to edit a different row than the one the request authorized.
        request!.Connection.Id = id;

        return RunAsync(() => service.UpdateConnectionAsync(request.Connection, request.ApiKey),
            $"updating vault connection {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} deleted vault connection {Id}", user.Value, id);

        return RunAsync(() => service.DeleteConnectionAsync(id), $"deleting vault connection {id}");
    }

    /// <summary>
    /// Verifies the connection against the vault and records the outcome. A failed test is a 200 with
    /// <c>Success = false</c>, not an error status: "your API key is wrong" is an answer, and turning
    /// it into a 502 makes the client report a transport problem instead of showing the message.
    /// </summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecretVaultTestResultView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<SecretVaultTestResultView>> Test(int id)
    {
        GetUser();
        return RunAsync(() => service.TestConnectionAsync(id), $"testing vault connection {id}");
    }

    /// <summary>
    /// The secrets this connection's credential can see — names and paths, plus field names for the
    /// vaults that report them. Never values: there is no endpoint that returns one.
    /// This is what the picker beside a secret field lists.
    /// </summary>
    [HttpGet]
    [Route("{id:int}/secrets")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<VaultSecretSummary>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public Task<ActionResult<List<VaultSecretSummary>>> ListSecrets(int id)
    {
        var user = GetUser();

        // Logged at Information, and deliberately: enumerating an estate's secret names is a
        // reasonable administrative act and also exactly what reconnaissance looks like. It should be
        // in the record either way.
        Logger.Information("User:{User} listed the secrets visible to vault connection {Id}", user.Value, id);

        return RunAsync(() => service.ListSecretsAsync(id), $"listing the secrets of vault connection {id}");
    }

    /// <summary>
    /// Describes what a stored credential value points at, so a form can show "Prod vault: db-main /
    /// password" beside a field instead of the raw reference.
    ///
    /// A POST with a body rather than a query parameter: the reference is not a secret, but it names
    /// one, and names in URLs end up in access logs, proxy logs and browser history.
    /// </summary>
    [HttpPost]
    [Route("describe")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SecretReferenceView))]
    public Task<ActionResult<SecretReferenceView>> Describe([FromBody] SecretReferenceDescribeRequest request)
    {
        GetUser();
        return RunAsync(() => service.DescribeAsync(request?.Value), "describing a stored secret reference");
    }

    /// <summary>
    /// How many credential fields resolve through this connection. Shown before a delete, so the
    /// refusal is not the first time an operator learns the connection is in use.
    /// </summary>
    [HttpGet]
    [Route("{id:int}/usage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    public Task<ActionResult<int>> Usage(int id)
    {
        GetUser();
        return RunAsync(() => service.CountReferencesAsync(id), $"counting references to vault connection {id}");
    }
}
