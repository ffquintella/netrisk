namespace DAL.Entities;

/// <summary>
/// A mirrored Jira Service Management customer request (Track 4 milestone 4.6).
///
/// Mirrored rather than read live, unlike the queues, because these are the rows that have to survive
/// a restart, be joined against findings for reporting, and be swept by a job looking for SLA
/// breaches. A live read cannot answer "what breached overnight".
/// </summary>
public class JiraServiceRequest
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    /// <summary>The Jira key — <c>SD-4711</c>. Unique per connection.</summary>
    public string IssueKey { get; set; } = null!;

    public string? IssueId { get; set; }

    public int? ServiceDeskId { get; set; }

    public string? RequestTypeId { get; set; }

    public string? RequestTypeName { get; set; }

    public string? Summary { get; set; }

    /// <summary>The desk's own status name, verbatim — <c>Waiting for support</c>.</summary>
    public string? StatusName { get; set; }

    /// <summary>
    /// Jira's status *category* (<c>new</c>, <c>indeterminate</c>, <c>done</c>). Kept alongside the
    /// name because the name is renamed per workflow and the category is not, which makes it the only
    /// reliable "is this finished" signal.
    /// </summary>
    public string? StatusCategory { get; set; }

    public string? ReporterAccountId { get; set; }

    public string? ReporterDisplayName { get; set; }

    public string? OrganizationName { get; set; }

    public string? PriorityName { get; set; }

    public string? AssigneeDisplayName { get; set; }

    public DateTime? CreatedAtRemote { get; set; }

    public DateTime? UpdatedAtRemote { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>The customer portal URL Jira reports, so the grid can link straight out.</summary>
    public string? RequestUrl { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Last sync failure for this request, redacted of credentials.</summary>
    public string? SyncError { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual ICollection<JiraRequestSla> Slas { get; set; } = new List<JiraRequestSla>();
}
