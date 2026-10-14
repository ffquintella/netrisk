using DAL.Enums;

namespace Model.Findings;

/// <summary>
/// What a desktop close, reject or reopen did (T239). It takes two calls — the lifecycle transition, then the register's
/// workflow status — and the first commits on its own, so "it failed" is not one outcome: when the lifecycle moved and
/// the workflow write then failed, the finding <b>is</b> closed (or reopened) for the API, the SLA and the risk index,
/// and the client must show that state rather than the one it started from, and say the second half was not saved.
/// </summary>
/// <param name="LifecycleStatus">The finding's lifecycle state afterwards, as the server holds it.</param>
/// <param name="WorkflowStatusApplied">False when the lifecycle change committed and the workflow status write failed.</param>
/// <param name="WorkflowError">Why the workflow status was not written, when it was not.</param>
public sealed record LifecycleChangeOutcome(FindingStatus LifecycleStatus, bool WorkflowStatusApplied,
    string? WorkflowError = null)
{
    /// <summary>The lifecycle moved and the workflow status did not.</summary>
    public bool IsPartial => !WorkflowStatusApplied;
}
