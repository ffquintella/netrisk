namespace DAL.Enums;

/// <summary>
/// What caused a finding's status to change. Recorded on every <c>finding_status_history</c> row so
/// an auditor reading the timeline can tell a human decision from an automated one — the two carry
/// very different weight when the question is "who accepted this risk".
/// </summary>
public enum FindingStatusChangeSource
{
    /// <summary>A user acted through the API or the desktop client.</summary>
    Manual = 1,

    /// <summary>A scanner import: a new finding, a reactivation, or a regression.</summary>
    Import = 2,

    /// <summary>A scheduled job: acceptance expiry, auto-close, SLA processing.</summary>
    Job = 3,

    /// <summary>An external issue tracker pushed a state change back (Track 4.2).</summary>
    IssueSync = 4
}
