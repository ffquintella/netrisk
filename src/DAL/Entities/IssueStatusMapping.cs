using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// "When this connection reports an issue in state <see cref="ExternalStatus"/>, do
/// <see cref="Action"/>" (Track 4 milestone 4.2.3).
///
/// Per connection, not global: <c>Done</c> in one Jira project is a verified fix and in another it
/// is "the developer says so", and those deserve different actions.
/// </summary>
public class IssueStatusMapping
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    /// <summary>The tracker's own state name, compared case-insensitively.</summary>
    public string ExternalStatus { get; set; } = null!;

    public IssueSyncAction Action { get; set; }

    /// <summary>
    /// What NetRisk says on the linked issue when a finding reaches the mapped state — the outbound
    /// direction of the same mapping. Null means "comment only, do not transition".
    /// </summary>
    public string? OutboundTransition { get; set; }

    public virtual IssueTrackerConnection? Connection { get; set; }
}
