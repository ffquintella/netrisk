namespace DAL.Enums;

/// <summary>
/// What an inbound external status is mapped to (Track 4 milestone 4.2.3), persisted in
/// <c>issue_status_mappings.action</c>.
///
/// Deliberately not "the external status maps to a NetRisk status": closing a ticket does not always
/// mean the finding is fixed. Teams that require a re-scan before believing it need
/// <see cref="ScheduleReverify"/>, and the ones that do not need <see cref="MarkMitigated"/>. Both
/// are legitimate policies, so the mapping names the action rather than the destination state.
/// </summary>
public enum IssueSyncAction
{
    /// <summary>Do nothing. The default for statuses that are just workflow noise.</summary>
    None = 0,

    /// <summary>Transition the finding to <c>Mitigated</c>.</summary>
    MarkMitigated = 1,

    /// <summary>Leave the finding open and create a re-verification task instead.</summary>
    ScheduleReverify = 2,

    /// <summary>Transition the finding to <c>FalsePositive</c>. Needs a justification, which the sync supplies.</summary>
    MarkFalsePositive = 3,

    /// <summary>Transition the finding back to <c>Active</c> — a reopened ticket.</summary>
    Reactivate = 4
}
