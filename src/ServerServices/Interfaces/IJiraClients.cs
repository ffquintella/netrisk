using DAL.Entities;
using Model.Integrations;
using ServerServices.Integrations.IssueTrackers.Jira;

namespace ServerServices.Interfaces;

/// <summary>
/// The Jira Service Management read surface (Track 4 milestone 4.6).
///
/// Read-only on purpose: NetRisk writes to Jira through <c>IIssueTrackerProvider</c>, which already
/// carries the operator's status mapping and its loop protection. A second write path here would be a
/// second place for that policy to live and a second place for it to disagree with itself.
/// </summary>
public interface IJiraServiceManagementClient
{
    Task<List<JiraServiceDeskView>> GetServiceDesksAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);

    Task<List<JiraRequestTypeView>> GetRequestTypesAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, CancellationToken ct = default);

    /// <summary>Queues with the issue count Jira advertises, so a queue can be sized before it is imported.</summary>
    Task<List<JiraQueueView>> GetQueuesAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, CancellationToken ct = default);

    /// <summary>The issue keys in a queue, up to <paramref name="max"/>.</summary>
    Task<List<string>> GetQueueIssueKeysAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, int queueId, int max, CancellationToken ct = default);

    /// <summary>
    /// One customer request. Null when the key is not a customer request at all — a Jira Software issue
    /// on the same site is invisible to this API, and that is normal rather than an error.
    /// </summary>
    Task<JsmRequest?> GetRequestAsync(IssueTrackerConnection connection, string? token, string issueKey,
        CancellationToken ct = default);

    /// <summary>
    /// A request's SLA cycles. Empty rather than throwing when the read fails: SLA is an enrichment,
    /// and losing a whole sync's status changes over one metric would be the wrong trade.
    /// </summary>
    Task<List<JsmSlaCycle>> GetSlaAsync(IssueTrackerConnection connection, string? token, string issueKey,
        CancellationToken ct = default);

    /// <summary>
    /// The site's Assets workspace id, which the Assets API is keyed by and which cannot be derived
    /// from the site URL. Null when the site has no Assets workspace or the plan does not include it.
    /// </summary>
    Task<string?> GetAssetsWorkspaceIdAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);

    Task<ConnectionTestResult> TestServiceDeskAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, CancellationToken ct = default);
}

/// <summary>
/// Jira Assets — the Service Management CMDB (Track 4 milestone 4.6), Cloud only.
///
/// Reached at <c>api.atlassian.com</c> rather than on the connection's own site, keyed by the
/// workspace id from <see cref="IJiraServiceManagementClient.GetAssetsWorkspaceIdAsync"/>.
/// </summary>
public interface IJiraAssetsClient
{
    Task<List<JiraObjectSchemaView>> GetSchemasAsync(IssueTrackerConnection connection, string? token,
        string workspaceId, CancellationToken ct = default);

    Task<List<JiraObjectTypeView>> GetObjectTypesAsync(IssueTrackerConnection connection, string? token,
        string workspaceId, int schemaId, CancellationToken ct = default);

    /// <summary>
    /// An object type's attributes. Needed twice: to populate the mapping editor's source picker, and
    /// by the importer to resolve attribute ids to names, since the search payload does not reliably
    /// carry them.
    /// </summary>
    Task<List<JiraObjectTypeAttributeView>> GetAttributesAsync(IssueTrackerConnection connection,
        string? token, string workspaceId, int objectTypeId, CancellationToken ct = default);

    /// <summary>One page of an AQL search.</summary>
    Task<AssetSearchPage> SearchAsync(IssueTrackerConnection connection, string? token, string workspaceId,
        string aql, int startAt, int maxResults, CancellationToken ct = default);

    /// <summary>
    /// Proves the credential reaches <c>api.atlassian.com</c>, the workspace id resolves, and the
    /// site's plan includes Assets — three things that fail independently.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(IssueTrackerConnection connection, string? token,
        string workspaceId, CancellationToken ct = default);
}

/// <summary>
/// The Jira platform metadata behind the mapping editors' pickers (Track 4 milestone 4.6): fields
/// (including custom fields), priorities, and the configured project's statuses.
/// </summary>
public interface IJiraMetadataClient
{
    Task<List<JiraFieldView>> GetFieldsAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);

    Task<List<string>> GetPrioritiesAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);

    /// <summary>The statuses of the connection's project, deduplicated across issue types.</summary>
    Task<List<string>> GetProjectStatusesAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default);
}
