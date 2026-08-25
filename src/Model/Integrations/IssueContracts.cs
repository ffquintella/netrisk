using DAL.Enums;

namespace Model.Integrations;

/// <summary>
/// What NetRisk wants created in a tracker (Track 4 milestone 4.2.1).
///
/// Provider-neutral: a Jira issue, a GitHub issue and an Azure DevOps work item are all built from
/// this. Priority is carried as the *tracker's* vocabulary rather than a NetRisk severity because the
/// mapping is per connection and has already been applied by the time a provider sees the draft — a
/// provider that mapped severity itself would need to know every customer's priority scheme.
/// </summary>
public class IssueDraft
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown. Jira's provider converts it to ADF; the others post it as-is.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Already-mapped tracker priority ("Highest", "P1"). Null leaves the project default.</summary>
    public string? Priority { get; set; }

    public List<string> Labels { get; set; } = new();

    /// <summary>Jira/ADO issue type. Ignored where the concept does not exist.</summary>
    public string? IssueType { get; set; }

    /// <summary>The NetRisk finding this is for, echoed back on the created issue for traceability.</summary>
    public int FindingId { get; set; }
}

/// <summary>
/// An issue as the tracker reports it (Track 4 milestone 4.2.1).
///
/// <see cref="Status"/> is the tracker's own state name, not a NetRisk status: the translation is the
/// connection's status mapping, and doing it inside the provider would put customer policy in the
/// provider.
/// </summary>
public class ExternalIssue
{
    /// <summary>Human key: <c>SEC-1421</c>, <c>88</c>, <c>4712</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Internal id where it differs from the key.</summary>
    public string? Id { get; set; }

    public string? Url { get; set; }

    public string? Title { get; set; }

    /// <summary>The tracker's state name, verbatim.</summary>
    public string? Status { get; set; }

    /// <summary>
    /// Whether the tracker considers the issue resolved. Providers can answer this from a
    /// category/state-type field, which is more reliable than string-matching "Done" across
    /// workflows that rename it.
    /// </summary>
    public bool IsClosed { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>What a provider can and cannot do, so the UI does not offer what will not work.</summary>
public class IssueTrackerCapabilities
{
    /// <summary>The provider can receive validated inbound webhooks.</summary>
    public bool SupportsWebhooks { get; init; }

    /// <summary>The provider can post a comment on an existing issue.</summary>
    public bool SupportsComments { get; init; }

    /// <summary>The provider can move an issue to a named state.</summary>
    public bool SupportsTransitions { get; init; }

    /// <summary>The provider accepts labels/tags.</summary>
    public bool SupportsLabels { get; init; }

    /// <summary>The provider has a priority field.</summary>
    public bool SupportsPriority { get; init; }

    /// <summary>Human note shown beside the connection form — auth quirks, required scopes.</summary>
    public string? SetupHint { get; init; }
}

/// <summary>
/// Result of the connection form's "Test connection" (Track 4 milestone 4.2.1).
///
/// Reused by every Track 4 integration, not only issue trackers: every one of them has a credential
/// that is either right or wrong, and one shape means one piece of UI.
/// </summary>
public class ConnectionTestResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Extra detail worth showing — the project name resolved, the API version seen.</summary>
    public Dictionary<string, string> Details { get; init; } = new();

    public static ConnectionTestResult Ok(string message, Dictionary<string, string>? details = null) =>
        new() { Success = true, Message = message, Details = details ?? new Dictionary<string, string>() };

    public static ConnectionTestResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// The outcome of one inbound-sync pass over the links of one connection
/// (Track 4 milestone 4.2.3).
/// </summary>
public class IssueSyncResult
{
    public int Examined { get; set; }

    /// <summary>Links whose external status changed since the last sync.</summary>
    public int Changed { get; set; }

    /// <summary>Links whose change mapped to an action that was applied to the finding.</summary>
    public int Applied { get; set; }

    /// <summary>Links flagged for the conflict review queue.</summary>
    public int Conflicts { get; set; }

    public int Errors { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// One row of the finding's linked-issues panel, with only what the client should see. The
/// connection's credentials are deliberately absent.
/// </summary>
public class FindingIssueLinkView
{
    public int Id { get; set; }

    public int FindingId { get; set; }

    public int ConnectionId { get; set; }

    public string ConnectionName { get; set; } = string.Empty;

    public IssueTrackerProviderKind Provider { get; set; }

    public string IssueKey { get; set; } = string.Empty;

    public string? IssueUrl { get; set; }

    public string? LastSyncedStatus { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public string? SyncError { get; set; }

    public bool HasConflict { get; set; }

    public string? ConflictDetail { get; set; }
}

/// <summary>
/// A connection as the client sees it (Track 4 milestone 4.2.1) — no token, no webhook secret, only
/// flags saying whether they are set.
/// </summary>
public class IssueTrackerConnectionView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public IssueTrackerProviderKind Provider { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ProjectKey { get; set; } = string.Empty;

    public string? IssueType { get; set; }

    public string? AuthUser { get; set; }

    public bool HasToken { get; set; }

    public bool HasWebhookSecret { get; set; }

    public string? PriorityMappingJson { get; set; }

    public string? TitleTemplate { get; set; }

    public string? DescriptionTemplate { get; set; }

    public string? DefaultLabels { get; set; }

    public int? EntityId { get; set; }

    public bool Enabled { get; set; }

    public int? AutoCreateMinSeverity { get; set; }

    public bool PushFindingUpdates { get; set; }

    public int PollIntervalMinutes { get; set; }

    public List<IssueStatusMappingView> StatusMappings { get; set; } = new();

    public IssueTrackerCapabilities? Capabilities { get; set; }
}

/// <summary>One status-mapping row for the client.</summary>
public class IssueStatusMappingView
{
    public int Id { get; set; }

    public string ExternalStatus { get; set; } = string.Empty;

    public IssueSyncAction Action { get; set; }

    public string? OutboundTransition { get; set; }
}
