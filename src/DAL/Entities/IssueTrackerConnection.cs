using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One configured issue tracker (Track 4 milestone 4.2.1): where it is, how to authenticate, and how
/// a NetRisk finding is rendered into one of its issues.
///
/// The field mapping lives on the connection rather than being global because the same NetRisk
/// severity means different priorities in two teams' Jira projects, and forcing one mapping on both
/// is how integrations get turned off.
/// </summary>
public class IssueTrackerConnection
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public IssueTrackerProviderKind Provider { get; set; }

    /// <summary>
    /// Instance root — <c>https://acme.atlassian.net</c>, <c>https://api.github.com</c>,
    /// <c>https://gitlab.com</c>, <c>https://dev.azure.com/acme</c>. Stored per connection because
    /// self-hosted GitLab and Jira Data Center are the common cases, not the exception.
    /// </summary>
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Where issues land: a Jira project key, an <c>owner/repo</c>, a GitLab project path or id, an
    /// Azure DevOps project name.
    /// </summary>
    public string ProjectKey { get; set; } = null!;

    /// <summary>Jira/ADO issue type ("Bug", "Task"). Ignored by GitHub and GitLab, which have none.</summary>
    public string? IssueType { get; set; }

    /// <summary>The account the token belongs to. Jira Cloud and ADO need it for basic auth.</summary>
    public string? AuthUser { get; set; }

    /// <summary>Encrypted API token / PAT. Never returned to a client — the DTO carries a flag, not the value.</summary>
    public string? EncryptedToken { get; set; }

    /// <summary>
    /// Shared secret used to validate inbound webhooks (GitHub/GitLab signature, or a query token
    /// for the others). Encrypted at rest for the same reason as the API token.
    /// </summary>
    public string? EncryptedWebhookSecret { get; set; }

    /// <summary>
    /// NetRisk severity → tracker priority, as JSON (<c>{"4":"Highest","3":"High"}</c>). Free-form
    /// because the target vocabulary is the tracker's, and enumerating it here would mean shipping a
    /// new release whenever a customer renames a priority.
    /// </summary>
    public string? PriorityMappingJson { get; set; }

    /// <summary>Title template with <c>{{Field}}</c> placeholders. Null uses the built-in default.</summary>
    public string? TitleTemplate { get; set; }

    /// <summary>Description/body template with the same placeholders.</summary>
    public string? DescriptionTemplate { get; set; }

    /// <summary>Comma-separated labels applied to every issue this connection creates.</summary>
    public string? DefaultLabels { get; set; }

    /// <summary>Restricts the connection to one business entity's findings.</summary>
    public int? EntityId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Policy mode (4.2.2): auto-create an issue for a new finding at or above this severity. Null
    /// means manual-only, which is the default — ticket-per-finding noise is the failure mode this
    /// whole milestone is trying to avoid.
    /// </summary>
    public int? AutoCreateMinSeverity { get; set; }

    /// <summary>
    /// Post a comment (and transition, per mapping) on the linked issue when the NetRisk finding
    /// changes. The outbound half of bi-directional sync.
    /// </summary>
    public bool PushFindingUpdates { get; set; } = true;

    /// <summary>Polling interval for instances that cannot reach NetRisk with a webhook.</summary>
    public int PollIntervalMinutes { get; set; } = 15;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual ICollection<IssueStatusMapping> StatusMappings { get; set; } = new List<IssueStatusMapping>();

    public virtual ICollection<FindingIssueLink> Links { get; set; } = new List<FindingIssueLink>();

    // --- Track 4.6 -------------------------------------------------------------------------------
    // Only ever populated for a Jira connection. Navigations rather than lookups by connection id so
    // the service layer can load a connection and its whole configuration in one query -- the admin
    // screen needs all of it at once, and four round trips per connection is what makes a settings
    // screen feel slow.

    /// <summary>Jira's Service Management and Assets facet (4.6). Null for the other providers.</summary>
    public virtual JiraConnectionSettings? JiraSettings { get; set; }

    /// <summary>Per-connection Jira field mapping, including custom fields (4.6).</summary>
    public virtual ICollection<JiraFieldMapping> FieldMappings { get; set; } = new List<JiraFieldMapping>();

    /// <summary>Assets object-type mappings (4.6).</summary>
    public virtual ICollection<JiraObjectMapping> ObjectMappings { get; set; } = new List<JiraObjectMapping>();
}
