using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The link between a NetRisk record and an external issue (Track 4 milestones 4.2.1 and 4.6).
///
/// A record may link to several trackers — security files a Jira ticket, the owning team tracks it
/// in their own GitLab — so this is a row rather than a column pair on <c>vulnerabilities</c>.
///
/// Milestone 4.6 widened it from findings to findings, incidents and risks. Widened rather than
/// duplicated: the poll loop, the webhook lookup, the loop protection and the conflict queue all key
/// off this one table, and a second link table would have meant a second copy of each of them.
///
/// The target is three real foreign keys plus a <see cref="TargetKind"/> discriminator, and not a
/// polymorphic <c>(kind, id)</c> pair, because a polymorphic id cannot carry a foreign key — deleting
/// a risk would leave a link pointing at nothing, and <c>ON DELETE CASCADE</c> would stop working.
/// Exactly one of the three is set; the invariant is enforced by <see cref="Validate"/>, by a
/// <c>CHECK</c> constraint in the schema, and by a test.
/// </summary>
public class FindingIssueLink
{
    public int Id { get; set; }

    /// <summary>
    /// Which of the three target columns is in use. Defaults to <see cref="IssueLinkTargetKind.Finding"/>
    /// so that every row written before 4.6 — all of which are finding links — reads correctly
    /// without a backfill.
    /// </summary>
    public IssueLinkTargetKind TargetKind { get; set; } = IssueLinkTargetKind.Finding;

    /// <summary>The finding, when <see cref="TargetKind"/> is <c>Finding</c>.</summary>
    public int? VulnerabilityId { get; set; }

    /// <summary>The incident, when <see cref="TargetKind"/> is <c>Incident</c>.</summary>
    public int? IncidentId { get; set; }

    /// <summary>The risk, when <see cref="TargetKind"/> is <c>Risk</c>.</summary>
    public int? RiskId { get; set; }

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

    public virtual Incident? Incident { get; set; }

    public virtual Risk? Risk { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }

    public virtual User? CreatedBy { get; set; }

    /// <summary>
    /// The id of whichever record this link targets, regardless of kind. Read-only, so there is no
    /// setter that could put an id in the column that does not match <see cref="TargetKind"/>.
    /// </summary>
    public int TargetId => TargetKind switch
    {
        IssueLinkTargetKind.Incident => IncidentId ?? 0,
        IssueLinkTargetKind.Risk => RiskId ?? 0,
        _ => VulnerabilityId ?? 0
    };

    /// <summary>
    /// Points the link at one record, clearing the other two. The only supported way to set a target:
    /// assigning the columns by hand is how a link ends up claiming to be an incident link while
    /// carrying a vulnerability id.
    /// </summary>
    public void SetTarget(IssueLinkTargetKind kind, int id)
    {
        TargetKind = kind;
        VulnerabilityId = kind == IssueLinkTargetKind.Finding ? id : null;
        IncidentId = kind == IssueLinkTargetKind.Incident ? id : null;
        RiskId = kind == IssueLinkTargetKind.Risk ? id : null;
    }

    /// <summary>
    /// The reason the target is invalid, or null when it is fine. Checked before every save; the
    /// schema's <c>CHECK</c> constraint is the backstop, and a constraint the application can trip is
    /// a crash report rather than a defence.
    /// </summary>
    public string? Validate()
    {
        var set = (VulnerabilityId.HasValue ? 1 : 0)
                  + (IncidentId.HasValue ? 1 : 0)
                  + (RiskId.HasValue ? 1 : 0);

        if (set != 1)
            return $"An issue link must name exactly one target record; this one names {set}.";

        return TargetKind switch
        {
            IssueLinkTargetKind.Finding when !VulnerabilityId.HasValue =>
                "The link's target kind is Finding but no finding is set.",
            IssueLinkTargetKind.Incident when !IncidentId.HasValue =>
                "The link's target kind is Incident but no incident is set.",
            IssueLinkTargetKind.Risk when !RiskId.HasValue =>
                "The link's target kind is Risk but no risk is set.",
            _ => null
        };
    }
}
