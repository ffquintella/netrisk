using DAL.Entities;
using DAL.Enums;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// The configuration and read surface behind the Jira administration screen and the two Jira jobs
/// (Track 4 milestone 4.6).
///
/// All the policy lives here, and the clients stay transport: which service desk, which queues, which
/// object types map onto what, and which attribute fills which field. The same split as milestone
/// 4.2 — a client knows how to talk to one API, this knows what the customer configured.
/// </summary>
public interface IJiraIntegrationService
{
    // --- settings ---------------------------------------------------------------------------

    /// <summary>
    /// A connection's Jira facet, creating the default row on first read rather than returning null —
    /// an administration screen that has to handle "not configured yet" as a separate state is a
    /// screen with a second empty layout nobody tested.
    /// </summary>
    Task<JiraConnectionSettingsView> GetSettingsAsync(int connectionId);

    /// <summary>
    /// Saves the facet, including the queue-import selection, which is replaced wholesale: it is
    /// edited as a checkbox list, and a partial save leaves a selection nobody chose.
    /// </summary>
    Task<JiraConnectionSettingsView> SaveSettingsAsync(int connectionId, JiraConnectionSettingsView settings);

    // --- discovery (live reads for the editors' pickers) ------------------------------------

    Task<List<JiraServiceDeskView>> GetServiceDesksAsync(int connectionId);

    Task<List<JiraRequestTypeView>> GetRequestTypesAsync(int connectionId, int serviceDeskId);

    Task<List<JiraQueueView>> GetQueuesAsync(int connectionId, int serviceDeskId);

    Task<List<JiraFieldView>> GetJiraFieldsAsync(int connectionId);

    Task<List<string>> GetJiraPrioritiesAsync(int connectionId);

    Task<List<string>> GetJiraStatusesAsync(int connectionId);

    Task<List<JiraObjectSchemaView>> GetAssetSchemasAsync(int connectionId);

    Task<List<JiraObjectTypeView>> GetAssetObjectTypesAsync(int connectionId, int schemaId);

    Task<List<JiraObjectTypeAttributeView>> GetAssetAttributesAsync(int connectionId, int objectTypeId);

    /// <summary>
    /// The NetRisk targets a mapping may write. Served by the server so the picker cannot offer a
    /// field the projector does not implement.
    /// </summary>
    List<MappableFieldView> GetMappableFields(JiraAssetTargetKind? targetKind);

    // --- field mapping ----------------------------------------------------------------------

    Task<List<JiraFieldMappingView>> GetFieldMappingsAsync(int connectionId);

    /// <summary>Replaces the connection's field mappings wholesale. Validated against the field catalog.</summary>
    Task<List<JiraFieldMappingView>> SetFieldMappingsAsync(int connectionId,
        IReadOnlyList<JiraFieldMappingView> mappings);

    // --- object mapping ---------------------------------------------------------------------

    Task<List<JiraObjectMappingView>> GetObjectMappingsAsync(int connectionId);

    /// <summary>
    /// Replaces the object mappings and their attribute rows wholesale, refusing a row whose target
    /// field does not exist for its target kind and a mapping with no name target — the two
    /// configurations that would be stored and then silently do nothing.
    /// </summary>
    Task<List<JiraObjectMappingView>> SetObjectMappingsAsync(int connectionId,
        IReadOnlyList<JiraObjectMappingView> mappings, int? userId);

    // --- Service Management -----------------------------------------------------------------

    /// <summary>Pulls the configured queues into the mirror. The job's entry point and the "sync now" button's.</summary>
    Task<JsmSyncResult> SyncServiceManagementAsync(int connectionId, int? userId = null);

    /// <summary>Every enabled Jira connection whose poll interval has elapsed.</summary>
    Task<JsmSyncResult> SyncDueServiceManagementAsync(DateTime nowUtc);

    /// <summary>The mirror, newest first, optionally only what is breaching.</summary>
    Task<List<JiraServiceRequestView>> GetMirroredRequestsAsync(int connectionId, bool breachedOnly = false,
        int limit = 200);

    /// <summary>One mirrored request with its SLA cycles.</summary>
    Task<JiraServiceRequestView> GetMirroredRequestAsync(int connectionId, string issueKey);

    // --- Assets -----------------------------------------------------------------------------

    /// <summary>
    /// Runs the object mappings. <paramref name="dryRun"/> writes nothing and returns the counts plus
    /// a sample of the rows as they would be written — the preview an operator reads before trusting a
    /// mapping against ten thousand objects.
    /// </summary>
    Task<AssetImportResult> ImportAssetsAsync(int connectionId, bool dryRun, int? userId = null);

    /// <summary>The imported register, including the objects that resolved to nothing.</summary>
    Task<List<JiraAssetObjectView>> GetAssetObjectsAsync(int connectionId, int limit = 500);

    // --- links beyond findings --------------------------------------------------------------

    /// <summary>Every issue linked to one record, whatever kind it is.</summary>
    Task<List<FindingIssueLinkView>> GetLinksForRecordAsync(IssueLinkTargetKind targetKind, int targetId);

    /// <summary>
    /// Creates an issue for an incident or a risk and links it. Idempotent per (connection, record),
    /// like 4.2's finding path.
    /// </summary>
    Task<FindingIssueLinkView> CreateIssueForRecordAsync(int connectionId, IssueLinkTargetKind targetKind,
        int targetId, int? userId);

    /// <summary>Links a record to an issue that already exists, by key or URL.</summary>
    Task<FindingIssueLinkView> LinkRecordAsync(int connectionId, IssueLinkTargetKind targetKind,
        int targetId, string issueKeyOrUrl, int? userId);
}
