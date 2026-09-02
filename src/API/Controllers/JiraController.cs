using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The Jira Service Management and Assets surface (Track 4 milestone 4.6): the connection's Jira
/// facet, the metadata behind the mapping editors' pickers, the two configurable mappings, the request
/// mirror, and the Assets register import.
///
/// A controller of its own rather than more actions on <see cref="IssueTrackersController"/>, which is
/// already the largest of the Track 4 controllers: these twenty-odd actions are all Jira-specific and
/// mixing them in would make it hard to see, in review, that nothing generic had changed.
///
/// Permissions follow what each action actually does rather than the controller's subject. Anything
/// that reads or writes configuration — including the live metadata reads, which spend the connection's
/// credential against a third party — needs <c>configuration</c>. Reading the mirror needs
/// <c>vulnerabilities</c>, the same permission that already gets somebody the connection list. Reading
/// the imported register needs <c>hosts</c>, because that is what it describes.
///
/// Every action is annotated: an unannotated one falls through to the deny-all fallback policy, but
/// <c>API.Tests/Security/ControllerAuthorizationInventoryTest</c> fails the build on it anyway.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class JiraController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IJiraIntegrationService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    // --- connection facet -------------------------------------------------------------------

    /// <summary>A connection's Jira facet, created with defaults on first read.</summary>
    [HttpGet]
    [Route("{connectionId:int}/settings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JiraConnectionSettingsView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<JiraConnectionSettingsView>> GetSettings(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetSettingsAsync(connectionId),
            $"reading the Jira settings of connection {connectionId}");
    }

    [HttpPut]
    [Route("{connectionId:int}/settings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JiraConnectionSettingsView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<JiraConnectionSettingsView>> SaveSettings(int connectionId,
        [FromBody] JiraConnectionSettingsView settings)
    {
        var user = GetUser();

        Logger.Information(
            "User:{User} saved the Jira settings of connection {Connection} (JSM {Jsm}, Assets {Assets})",
            user.Value, connectionId, settings?.JsmEnabled, settings?.AssetsEnabled);

        return RunAsync(() => service.SaveSettingsAsync(connectionId, settings ?? new()),
            $"saving the Jira settings of connection {connectionId}");
    }

    // --- metadata for the editors -----------------------------------------------------------

    [HttpGet]
    [Route("{connectionId:int}/service-desks")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraServiceDeskView>))]
    public Task<ActionResult<List<JiraServiceDeskView>>> GetServiceDesks(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetServiceDesksAsync(connectionId),
            $"listing the service desks of connection {connectionId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/service-desks/{serviceDeskId:int}/request-types")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraRequestTypeView>))]
    public Task<ActionResult<List<JiraRequestTypeView>>> GetRequestTypes(int connectionId,
        int serviceDeskId)
    {
        GetUser();
        return RunAsync(() => service.GetRequestTypesAsync(connectionId, serviceDeskId),
            $"listing the request types of service desk {serviceDeskId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/service-desks/{serviceDeskId:int}/queues")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraQueueView>))]
    public Task<ActionResult<List<JiraQueueView>>> GetQueues(int connectionId, int serviceDeskId)
    {
        GetUser();
        return RunAsync(() => service.GetQueuesAsync(connectionId, serviceDeskId),
            $"listing the queues of service desk {serviceDeskId}");
    }

    /// <summary>The site's fields, including custom fields, for the field-mapping picker.</summary>
    [HttpGet]
    [Route("{connectionId:int}/fields")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraFieldView>))]
    public Task<ActionResult<List<JiraFieldView>>> GetFields(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetJiraFieldsAsync(connectionId),
            $"listing the Jira fields of connection {connectionId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/priorities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public Task<ActionResult<List<string>>> GetPriorities(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetJiraPrioritiesAsync(connectionId),
            $"listing the Jira priorities of connection {connectionId}");
    }

    /// <summary>The configured project's statuses, for the status-mapping editor.</summary>
    [HttpGet]
    [Route("{connectionId:int}/statuses")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public Task<ActionResult<List<string>>> GetStatuses(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetJiraStatusesAsync(connectionId),
            $"listing the Jira statuses of connection {connectionId}");
    }

    /// <summary>
    /// The NetRisk fields a mapping may write. Served by the server so the editor cannot offer a
    /// target the mapping engine does not implement.
    /// </summary>
    [HttpGet]
    [Route("mappable-fields")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<MappableFieldView>))]
    public ActionResult<List<MappableFieldView>> GetMappableFields(
        [FromQuery] JiraAssetTargetKind? targetKind = null)
    {
        GetUser();
        return Ok(service.GetMappableFields(targetKind));
    }

    // --- field mapping ----------------------------------------------------------------------

    [HttpGet]
    [Route("{connectionId:int}/field-mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraFieldMappingView>))]
    public Task<ActionResult<List<JiraFieldMappingView>>> GetFieldMappings(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetFieldMappingsAsync(connectionId),
            $"reading the field mappings of connection {connectionId}");
    }

    /// <summary>
    /// Replaces the field mappings. Wholesale rather than per row, because the mapping is edited as a
    /// grid and a partial save leaves a half-configured mapping writing to live tickets.
    /// </summary>
    [HttpPut]
    [Route("{connectionId:int}/field-mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraFieldMappingView>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<List<JiraFieldMappingView>>> SetFieldMappings(int connectionId,
        [FromBody] List<JiraFieldMappingView> mappings)
    {
        var user = GetUser();

        Logger.Information("User:{User} set {Count} Jira field mapping(s) on connection {Connection}",
            user.Value, mappings?.Count ?? 0, connectionId);

        return RunAsync(() => service.SetFieldMappingsAsync(connectionId, mappings ?? []),
            $"setting the field mappings of connection {connectionId}");
    }

    // --- Assets object mapping --------------------------------------------------------------

    [HttpGet]
    [Route("{connectionId:int}/assets/schemas")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraObjectSchemaView>))]
    public Task<ActionResult<List<JiraObjectSchemaView>>> GetAssetSchemas(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetAssetSchemasAsync(connectionId),
            $"listing the Assets schemas of connection {connectionId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/assets/schemas/{schemaId:int}/object-types")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraObjectTypeView>))]
    public Task<ActionResult<List<JiraObjectTypeView>>> GetAssetObjectTypes(int connectionId, int schemaId)
    {
        GetUser();
        return RunAsync(() => service.GetAssetObjectTypesAsync(connectionId, schemaId),
            $"listing the object types of Assets schema {schemaId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/assets/object-types/{objectTypeId:int}/attributes")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraObjectTypeAttributeView>))]
    public Task<ActionResult<List<JiraObjectTypeAttributeView>>> GetAssetAttributes(int connectionId,
        int objectTypeId)
    {
        GetUser();
        return RunAsync(() => service.GetAssetAttributesAsync(connectionId, objectTypeId),
            $"listing the attributes of Assets object type {objectTypeId}");
    }

    [HttpGet]
    [Route("{connectionId:int}/assets/mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraObjectMappingView>))]
    public Task<ActionResult<List<JiraObjectMappingView>>> GetObjectMappings(int connectionId)
    {
        GetUser();
        return RunAsync(() => service.GetObjectMappingsAsync(connectionId),
            $"reading the Assets object mappings of connection {connectionId}");
    }

    [HttpPut]
    [Route("{connectionId:int}/assets/mappings")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraObjectMappingView>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<List<JiraObjectMappingView>>> SetObjectMappings(int connectionId,
        [FromBody] List<JiraObjectMappingView> mappings)
    {
        var user = GetUser();

        Logger.Information("User:{User} set {Count} Assets object mapping(s) on connection {Connection}",
            user.Value, mappings?.Count ?? 0, connectionId);

        return RunAsync(() => service.SetObjectMappingsAsync(connectionId, mappings ?? [], user.Value),
            $"setting the Assets object mappings of connection {connectionId}");
    }

    /// <summary>
    /// Runs the object mappings without writing anything, returning the counts and the first rows as
    /// they would be written — the preview before trusting a mapping against a whole register.
    /// </summary>
    [HttpPost]
    [Route("{connectionId:int}/assets/preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AssetImportResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<AssetImportResult>> PreviewAssetImport(int connectionId)
    {
        var user = GetUser();
        return RunAsync(() => service.ImportAssetsAsync(connectionId, dryRun: true, user.Value),
            $"previewing the Assets import of connection {connectionId}");
    }

    /// <summary>
    /// Imports the register. Separate from the preview by verb and route rather than by a query
    /// parameter, so a client cannot turn a preview into a write by flipping a flag.
    /// </summary>
    [HttpPost]
    [Route("{connectionId:int}/assets/import")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AssetImportResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<AssetImportResult>> ImportAssets(int connectionId)
    {
        var user = GetUser();

        Logger.Information("User:{User} started the Assets import of connection {Connection}",
            user.Value, connectionId);

        return RunAsync(() => service.ImportAssetsAsync(connectionId, dryRun: false, user.Value),
            $"importing the Assets register of connection {connectionId}");
    }

    /// <summary>
    /// The imported register, including the objects that resolved to nothing. Needs the hosts
    /// permission rather than the configuration one: it is asset inventory, and it is what answers
    /// "why is that server not in NetRisk".
    /// </summary>
    [PermissionAuthorize("hosts")]
    [HttpGet]
    [Route("{connectionId:int}/assets/objects")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraAssetObjectView>))]
    public Task<ActionResult<List<JiraAssetObjectView>>> GetAssetObjects(int connectionId,
        [FromQuery] int limit = 500)
    {
        GetUser();
        return RunAsync(() => service.GetAssetObjectsAsync(connectionId, limit),
            $"reading the imported Assets objects of connection {connectionId}");
    }

    // --- Service Management mirror ----------------------------------------------------------

    /// <summary>
    /// The mirrored requests. Readable by anyone who can see the vulnerability register, which is the
    /// same permission that already gets them the connection list.
    /// </summary>
    [PermissionAuthorize("vulnerabilities")]
    [HttpGet]
    [Route("{connectionId:int}/requests")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<JiraServiceRequestView>))]
    public Task<ActionResult<List<JiraServiceRequestView>>> GetRequests(int connectionId,
        [FromQuery] bool breachedOnly = false, [FromQuery] int limit = 200)
    {
        GetUser();
        return RunAsync(() => service.GetMirroredRequestsAsync(connectionId, breachedOnly, limit),
            $"reading the mirrored requests of connection {connectionId}");
    }

    [PermissionAuthorize("vulnerabilities")]
    [HttpGet]
    [Route("{connectionId:int}/requests/{issueKey}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JiraServiceRequestView))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<JiraServiceRequestView>> GetRequest(int connectionId, string issueKey)
    {
        GetUser();
        return RunAsync(() => service.GetMirroredRequestAsync(connectionId, issueKey),
            $"reading mirrored request {issueKey}");
    }

    /// <summary>Mirrors the configured queues now — the manual equivalent of the polling pass.</summary>
    [HttpPost]
    [Route("{connectionId:int}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JsmSyncResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<JsmSyncResult>> Sync(int connectionId)
    {
        var user = GetUser();

        Logger.Information("User:{User} mirrored the service desk of connection {Connection}",
            user.Value, connectionId);

        return RunAsync(() => service.SyncServiceManagementAsync(connectionId, user.Value),
            $"mirroring the service desk of connection {connectionId}");
    }
}
