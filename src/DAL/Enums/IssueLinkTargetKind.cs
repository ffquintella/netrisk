namespace DAL.Enums;

/// <summary>
/// Which kind of NetRisk record an issue link hangs off (Track 4 milestone 4.6), persisted in
/// <c>finding_issue_links.target_kind</c>.
///
/// Milestone 4.2 linked issues to vulnerability findings only. Incidents and risks are added here
/// rather than in a second link table, because the sync engine, the conflict queue and the loop
/// protection all key off that one table and a second table would mean a second copy of each.
///
/// <see cref="Finding"/> is 1 so that the additive migration can default every existing row to it —
/// every link that predates 4.6 is a finding link.
/// </summary>
public enum IssueLinkTargetKind
{
    /// <summary>A vulnerability finding. The only kind that inbound status actions apply to.</summary>
    Finding = 1,

    /// <summary>An incident. Its external status is mirrored; nothing is transitioned automatically.</summary>
    Incident = 2,

    /// <summary>A risk. Same read-only treatment as <see cref="Incident"/>.</summary>
    Risk = 3
}
