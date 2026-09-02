using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The Jira-specific facet of an issue-tracker connection (Track 4 milestone 4.6): which service desk
/// it reads, and which Assets workspace and schema it imports from.
///
/// A 1:1 extension table rather than more columns on <c>issue_tracker_connections</c>. GitHub, GitLab
/// and Azure DevOps have no service desk and no CMDB, so putting these here keeps fifteen
/// always-null columns off a table three other providers share — a generic table with one provider's
/// fields bolted on stops being generic.
///
/// And an extension rather than a second connection kind: a JSM service desk *is* a Jira project on
/// the same site, reached with the same credential. A separate <c>JiraServiceManagement</c> provider
/// would make an operator enter the same API token twice and would split one ticket's links across
/// two connections, giving the sync engine two tables to reconcile.
/// </summary>
public class JiraConnectionSettings
{
    /// <summary>Primary key *and* foreign key — the row exists only as an extension of its connection.</summary>
    public int ConnectionId { get; set; }

    /// <summary>
    /// Cloud or Data Center. Only <see cref="JiraDeployment.Cloud"/> is implemented; a Data Center
    /// connection is refused at save, because Assets on Data Center is the Insight API with a
    /// different root and a different object model, and pointing the Cloud client at it produces 404s
    /// that read as bad credentials.
    /// </summary>
    public JiraDeployment Deployment { get; set; } = JiraDeployment.Cloud;

    // --- Service Management -----------------------------------------------------------------

    public bool JsmEnabled { get; set; }

    /// <summary>The service desk's numeric id, as <c>/rest/servicedeskapi/servicedesk</c> reports it.</summary>
    public int? ServiceDeskId { get; set; }

    /// <summary>Cached for display, so the admin screen can name the desk without a round trip.</summary>
    public string? ServiceDeskName { get; set; }

    /// <summary>
    /// Comma-separated request-type ids the mirror keeps. Empty means every type. A filter rather
    /// than a per-type row because the only question asked of it today is "is this type in scope";
    /// per-type link targets would need a table, and nobody has asked for one.
    /// </summary>
    public string? RequestTypeFilter { get; set; }

    /// <summary>Fetch each mirrored request's SLA cycles. Separate from the mirror itself because SLA
    /// is one extra request per issue, which matters on a queue of thousands.</summary>
    public bool ImportSlas { get; set; } = true;

    /// <summary>Raise <c>JsmSlaBreached</c> through the 4.1 dispatcher when a cycle breaches.</summary>
    public bool SlaBreachNotifications { get; set; }

    /// <summary>What a request imported from a queue links to by default, when it links to anything.</summary>
    public IssueLinkTargetKind DefaultLinkTargetKind { get; set; } = IssueLinkTargetKind.Finding;

    public DateTime? LastJsmSyncAt { get; set; }

    // --- Assets -----------------------------------------------------------------------------

    public bool AssetsEnabled { get; set; }

    /// <summary>
    /// The Assets workspace id, discovered once from <c>/rest/servicedeskapi/assets/workspace</c> and
    /// cached. Not the Jira cloud id and not derivable from the site URL, which is why it is stored
    /// rather than computed.
    /// </summary>
    public string? AssetsWorkspaceId { get; set; }

    public int? AssetsSchemaId { get; set; }

    public string? AssetsSchemaName { get; set; }

    public DateTime? LastAssetsSyncAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual ICollection<JiraQueueImport> QueueImports { get; set; } = new List<JiraQueueImport>();
}
