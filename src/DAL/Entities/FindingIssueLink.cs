using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The link between a NetRisk finding and an external issue (Track 4 milestone 4.2.1).
///
/// A finding may link to several trackers — security files a Jira ticket, the owning team tracks it
/// in their own GitLab — so this is a row rather than a column pair on <c>vulnerabilities</c>.
/// </summary>
public class FindingIssueLink
{
    public int Id { get; set; }

    public int VulnerabilityId { get; set; }

    public int ConnectionId { get; set; }

    /// <summary>The tracker's human key — <c>SEC-1421</c>, <c>#88</c>, work-item <c>4712</c>.</summary>
    public string IssueKey { get; set; } = null!;

    /// <summary>The tracker's internal id, when it differs from the key (Jira and GitLab both do).</summary>
    public string? IssueId { get; set; }

    /// <summary>Browser URL, so the register can link straight out.</summary>
    public string? IssueUrl { get; set; }

    /// <summary>The external state as of the last successful sync.</summary>
    public string? LastSyncedStatus { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>Last sync failure, redacted of credentials. Null once a sync succeeds again.</summary>
    public string? SyncError { get; set; }

    /// <summary>
    /// True when the last inbound change came from the tracker. Loop protection: the outbound push
    /// skips a link whose current state it just received, so an inbound "Done" does not echo back out
    /// as a comment that the tracker then reports as a change.
    /// </summary>
    public bool LastChangeFromRemote { get; set; }

    /// <summary>
    /// Set when NetRisk and the tracker moved in incompatible directions between two syncs — the
    /// "sync conflicts" review queue. Last-writer-wins is applied, and the row is flagged so a human
    /// can see that it happened rather than discovering it from a finding that reopened itself.
    /// </summary>
    public bool HasConflict { get; set; }

    public string? ConflictDetail { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Vulnerability? Vulnerability { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual User? CreatedBy { get; set; }
}
