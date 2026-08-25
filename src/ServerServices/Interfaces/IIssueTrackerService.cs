using DAL.Entities;
using DAL.Enums;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// Issue-tracker connections, finding↔issue links, and bi-directional synchronization
/// (Track 4 milestone 4.2).
///
/// All the policy lives here rather than in the providers: severity→priority mapping, title and
/// description templates, the auto-create rule, status mapping in both directions, loop protection
/// and the conflict queue. A provider knows how to talk to one API; this knows what the customer
/// configured.
/// </summary>
public interface IIssueTrackerService
{
    // --- connections ------------------------------------------------------------------------

    /// <summary>Connections with credentials replaced by has-a-token flags.</summary>
    Task<List<IssueTrackerConnectionView>> GetConnectionsAsync(bool includeDisabled = true);

    Task<IssueTrackerConnectionView> GetConnectionAsync(int id);

    Task<IssueTrackerConnectionView> CreateConnectionAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret, int? userId);

    /// <summary>
    /// Updates a connection. A null token or webhook secret leaves the stored one alone, which is how
    /// the admin form round-trips without ever holding the real credential.
    /// </summary>
    Task<IssueTrackerConnectionView> UpdateConnectionAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret, int? userId);

    Task DeleteConnectionAsync(int id);

    Task<ConnectionTestResult> TestConnectionAsync(int id);

    /// <summary>The registered providers and what each can do, for the connection form.</summary>
    IReadOnlyList<(IssueTrackerProviderKind Kind, string Name, IssueTrackerCapabilities Capabilities)> GetProviders();

    // --- status mappings --------------------------------------------------------------------

    Task<List<IssueStatusMappingView>> GetStatusMappingsAsync(int connectionId);

    /// <summary>
    /// Replaces a connection's mappings wholesale. Wholesale rather than per-row because the mapping
    /// is edited as a table, and a partial save leaves a half-configured mapping applying to live
    /// findings.
    /// </summary>
    Task<List<IssueStatusMappingView>> SetStatusMappingsAsync(int connectionId,
        IReadOnlyList<IssueStatusMapping> mappings);

    // --- links ------------------------------------------------------------------------------

    Task<List<FindingIssueLinkView>> GetLinksForFindingAsync(int findingId);

    /// <summary>
    /// The rendered title and body for a finding on a connection, without creating anything — the
    /// preview step, which is what makes a template editable with confidence.
    /// </summary>
    Task<IssueDraft> PreviewAsync(int connectionId, int findingId);

    /// <summary>
    /// Creates an issue for a finding and links it. Idempotent per (connection, finding): a second
    /// call returns the existing link instead of filing a duplicate ticket.
    /// </summary>
    Task<FindingIssueLinkView> CreateIssueAsync(int connectionId, int findingId, int? userId);

    /// <summary>Creates one issue per finding, reporting per-finding failures rather than aborting.</summary>
    Task<List<FindingIssueLinkView>> CreateIssuesAsync(int connectionId, IReadOnlyList<int> findingIds,
        int? userId);

    /// <summary>Links a finding to an issue that already exists, by key or URL.</summary>
    Task<FindingIssueLinkView> LinkExistingAsync(int connectionId, int findingId, string issueKeyOrUrl,
        int? userId);

    /// <summary>Removes a link. The external issue is left alone — NetRisk does not delete other people's tickets.</summary>
    Task UnlinkAsync(int linkId);

    // --- synchronization --------------------------------------------------------------------

    /// <summary>
    /// The polling fallback (4.2.3): reads every link of a connection and applies whatever the status
    /// mapping says. For instances that cannot deliver a webhook to NetRisk.
    /// </summary>
    Task<IssueSyncResult> PollConnectionAsync(int connectionId, int? userId = null);

    /// <summary>Polls every enabled connection whose interval has elapsed. The job's entry point.</summary>
    Task<IssueSyncResult> PollDueConnectionsAsync(DateTime nowUtc);

    /// <summary>
    /// Applies one inbound webhook. Returns the result of the single link it touched; a body that does
    /// not verify, or names an issue NetRisk does not track, is reported as zero examined rather than
    /// as an error — an untracked issue changing state is not a problem.
    /// </summary>
    Task<IssueSyncResult> ApplyWebhookAsync(int connectionId, string rawBody,
        IReadOnlyDictionary<string, string> headers, string? presentedSecret);

    /// <summary>
    /// The outbound half (4.2.3): pushes a NetRisk finding transition onto every linked issue whose
    /// connection asks for it. Skips links whose last change came from the tracker, which is the loop
    /// protection.
    /// </summary>
    Task<int> PushFindingTransitionAsync(int findingId, FindingStatus to, string? note = null);

    /// <summary>
    /// Applies the auto-create policy to a newly imported finding (4.2.2). Returns the links created —
    /// empty when no connection has a policy that covers it, which is the default.
    /// </summary>
    Task<List<FindingIssueLinkView>> ApplyAutoCreatePolicyAsync(int findingId, int? userId = null);

    /// <summary>Links flagged as conflicted, for the review queue.</summary>
    Task<List<FindingIssueLinkView>> GetConflictsAsync();

    /// <summary>Clears a link's conflict flag once a human has looked at it.</summary>
    Task<FindingIssueLinkView> ResolveConflictAsync(int linkId);
}
