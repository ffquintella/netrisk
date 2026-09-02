using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using DAL.Enums;
using Model.Integrations;
using Model.Notifications;

namespace ClientServices.Interfaces;

/// <summary>
/// The Track 4 administration surface the desktop client needs: notification channels and
/// subscriptions, issue-tracker connections and links, identity providers, SCIM tokens, and the two
/// posture providers.
///
/// One service rather than six because these screens live together under Administration → Integrations
/// and are opened by the same person doing the same job; six REST services with identical plumbing
/// would be more files saying less. The same reasoning as <see cref="IFindingsAdminService"/>.
/// </summary>
public interface IIntegrationsService
{
    // --- 4.1 notification channels ----------------------------------------------------------

    Task<List<NotificationChannel>> GetChannelsAsync(bool includeDisabled = true);

    /// <summary>The channel kinds this server can deliver through.</summary>
    Task<List<NotificationChannelProvider>> GetChannelProvidersAsync();

    /// <summary>The event catalog, for the subscription matrix's rows.</summary>
    Task<List<NotificationEventDescriptor>> GetNotificationEventsAsync();

    Task<NotificationChannel> CreateChannelAsync(NotificationChannel channel);

    Task<NotificationChannel> UpdateChannelAsync(NotificationChannel channel);

    Task DeleteChannelAsync(int id);

    /// <summary>Sends a real test message through the channel.</summary>
    Task<ChannelTestResult> TestChannelAsync(int id);

    Task<List<NotificationSubscription>> GetSubscriptionsAsync();

    Task<NotificationSubscription> CreateSubscriptionAsync(NotificationSubscription subscription);

    Task<NotificationSubscription> UpdateSubscriptionAsync(NotificationSubscription subscription);

    Task DeleteSubscriptionAsync(int id);

    /// <summary>The delivery log — the observability half of milestone 4.1.3.</summary>
    Task<List<NotificationDelivery>> GetDeliveriesAsync(int limit = 200);

    Task<NotificationDelivery> RequeueDeliveryAsync(int id);

    // --- 4.2 issue trackers -----------------------------------------------------------------

    Task<List<IssueTrackerConnectionView>> GetIssueTrackersAsync(bool includeDisabled = true);

    Task<List<IssueTrackerProviderInfo>> GetIssueTrackerProvidersAsync();

    /// <summary>Creates a connection. The token and webhook secret travel once and are never returned.</summary>
    Task<IssueTrackerConnectionView> CreateIssueTrackerAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret);

    /// <summary>Nulls for the credentials mean "leave the stored ones alone".</summary>
    Task<IssueTrackerConnectionView> UpdateIssueTrackerAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret);

    Task DeleteIssueTrackerAsync(int id);

    Task<ConnectionTestResult> TestIssueTrackerAsync(int id);

    Task<List<IssueStatusMappingView>> GetStatusMappingsAsync(int connectionId);

    Task<List<IssueStatusMappingView>> SetStatusMappingsAsync(int connectionId,
        List<IssueStatusMapping> mappings);

    Task<IssueSyncResult> SyncIssueTrackerAsync(int id);

    Task<List<FindingIssueLinkView>> GetIssueSyncConflictsAsync();

    Task<FindingIssueLinkView> ResolveIssueSyncConflictAsync(int linkId);

    // --- 4.6 Jira Service Management & Assets ------------------------------------------------

    Task<JiraConnectionSettingsView> GetJiraSettingsAsync(int connectionId);

    Task<JiraConnectionSettingsView> SaveJiraSettingsAsync(int connectionId,
        JiraConnectionSettingsView settings);

    Task<List<JiraServiceDeskView>> GetJiraServiceDesksAsync(int connectionId);

    Task<List<JiraRequestTypeView>> GetJiraRequestTypesAsync(int connectionId, int serviceDeskId);

    Task<List<JiraQueueView>> GetJiraQueuesAsync(int connectionId, int serviceDeskId);

    /// <summary>The site's fields, including custom fields, for the field-mapping picker.</summary>
    Task<List<JiraFieldView>> GetJiraFieldsAsync(int connectionId);

    Task<List<string>> GetJiraPrioritiesAsync(int connectionId);

    /// <summary>The configured project's statuses, for the status-mapping editor.</summary>
    Task<List<string>> GetJiraStatusesAsync(int connectionId);

    /// <summary>
    /// The NetRisk fields a mapping may write. Fetched from the server rather than hard-coded in the
    /// client, so the picker cannot offer a target the mapping engine does not implement.
    /// </summary>
    Task<List<MappableFieldView>> GetMappableFieldsAsync(JiraAssetTargetKind? targetKind = null);

    Task<List<JiraFieldMappingView>> GetJiraFieldMappingsAsync(int connectionId);

    Task<List<JiraFieldMappingView>> SetJiraFieldMappingsAsync(int connectionId,
        List<JiraFieldMappingView> mappings);

    Task<List<JiraObjectSchemaView>> GetAssetSchemasAsync(int connectionId);

    Task<List<JiraObjectTypeView>> GetAssetObjectTypesAsync(int connectionId, int schemaId);

    Task<List<JiraObjectTypeAttributeView>> GetAssetAttributesAsync(int connectionId, int objectTypeId);

    Task<List<JiraObjectMappingView>> GetAssetMappingsAsync(int connectionId);

    Task<List<JiraObjectMappingView>> SetAssetMappingsAsync(int connectionId,
        List<JiraObjectMappingView> mappings);

    /// <summary>Runs the mappings without writing anything — the preview before the first import.</summary>
    Task<AssetImportResult> PreviewAssetImportAsync(int connectionId);

    Task<AssetImportResult> ImportAssetsAsync(int connectionId);

    Task<List<JiraAssetObjectView>> GetAssetObjectsAsync(int connectionId, int limit = 500);

    Task<List<JiraServiceRequestView>> GetJiraRequestsAsync(int connectionId, bool breachedOnly = false,
        int limit = 200);

    Task<JsmSyncResult> SyncJiraServiceManagementAsync(int connectionId);

    // --- 4.6 links on records that are not findings ------------------------------------------

    Task<List<FindingIssueLinkView>> GetLinksForRecordAsync(IssueLinkTargetKind targetKind,
        int targetId);

    Task<FindingIssueLinkView> CreateIssueForRecordAsync(int connectionId,
        IssueLinkTargetKind targetKind, int targetId);

    Task<FindingIssueLinkView> LinkRecordAsync(int connectionId, IssueLinkTargetKind targetKind,
        int targetId, string issueKeyOrUrl);

    // --- 4.2.2 finding ↔ issue links ---------------------------------------------------------

    Task<List<FindingIssueLinkView>> GetLinksForFindingAsync(int findingId);

    /// <summary>The rendered title and body, without creating anything.</summary>
    Task<IssueDraft> PreviewIssueAsync(int connectionId, int findingId);

    Task<FindingIssueLinkView> CreateIssueAsync(int connectionId, int findingId);

    Task<List<FindingIssueLinkView>> CreateIssuesAsync(int connectionId, List<int> findingIds);

    Task<FindingIssueLinkView> LinkExistingIssueAsync(int connectionId, int findingId, string issueKeyOrUrl);

    Task UnlinkIssueAsync(int linkId);

    // --- 4.3 enterprise authentication -------------------------------------------------------

    Task<List<IdentityProviderView>> GetIdentityProvidersAsync(bool includeDisabled = true);

    Task<IdentityProviderView> CreateIdentityProviderAsync(IdentityProvider provider, string? clientSecret);

    Task<IdentityProviderView> UpdateIdentityProviderAsync(IdentityProvider provider, string? clientSecret);

    Task DeleteIdentityProviderAsync(int id);

    Task<ConnectionTestResult> TestIdentityProviderAsync(int id);

    Task<List<ScimTokenView>> GetScimTokensAsync(bool includeRevoked = false);

    /// <summary>Issues a provisioning token. The response is the only time the secret exists in clear.</summary>
    Task<ScimTokenView> IssueScimTokenAsync(string name, int? identityProviderId);

    Task<ScimTokenView> RevokeScimTokenAsync(int id);

    Task<List<ScimRequestLog>> GetScimLogAsync(int limit = 200);

    // --- 4.4 Trend Micro Vision One ----------------------------------------------------------

    Task<List<TrendMicroConnectionView>> GetTrendMicroConnectionsAsync(bool includeDisabled = true);

    /// <summary>The regions and their API roots, for the connection form's picker.</summary>
    Task<Dictionary<string, string>> GetTrendMicroRegionsAsync();

    Task<TrendMicroConnectionView> CreateTrendMicroConnectionAsync(TrendMicroConnection connection,
        string? apiKey);

    Task<TrendMicroConnectionView> UpdateTrendMicroConnectionAsync(TrendMicroConnection connection,
        string? apiKey);

    Task DeleteTrendMicroConnectionAsync(int id);

    Task<ConnectionTestResult> TestTrendMicroConnectionAsync(int id);

    Task<PostureSyncResult> SyncTrendMicroConnectionAsync(int id);

    Task<List<IntegrationSyncLog>> GetTrendMicroLogAsync(int limit = 50);

    // --- 4.5 SecurityScorecard ---------------------------------------------------------------

    Task<List<SecurityScorecardConnectionView>> GetSecurityScorecardConnectionsAsync(
        bool includeDisabled = true);

    Task<SecurityScorecardConnectionView> CreateSecurityScorecardConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken);

    Task<SecurityScorecardConnectionView> UpdateSecurityScorecardConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken);

    Task DeleteSecurityScorecardConnectionAsync(int id);

    Task<ConnectionTestResult> TestSecurityScorecardConnectionAsync(int id);

    Task<PostureSyncResult> SyncSecurityScorecardConnectionAsync(int id);

    /// <summary>The factor history the trend chart reads.</summary>
    Task<List<SecurityScorecardFactor>> GetSecurityScorecardHistoryAsync(int id, int limit = 500);

    Task<List<IntegrationSyncLog>> GetSecurityScorecardLogAsync(int limit = 50);
}

/// <summary>One registered notification provider, as the server reports it.</summary>
public class NotificationChannelProvider
{
    public DAL.Enums.NotificationChannelKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>One catalog event, as the server reports it.</summary>
public class NotificationEventDescriptor
{
    public DAL.Enums.NotificationEventType EventType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool SupportsSeverityFilter { get; set; }

    public bool DigestRecommended { get; set; }
}

/// <summary>One issue-tracker provider and its capability flags, as the server reports them.</summary>
public class IssueTrackerProviderInfo
{
    public DAL.Enums.IssueTrackerProviderKind Provider { get; set; }

    public string Name { get; set; } = string.Empty;

    public IssueTrackerCapabilities? Capabilities { get; set; }
}
