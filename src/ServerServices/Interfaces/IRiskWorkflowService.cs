using DAL.Entities;
using Model.Governance;

namespace ServerServices.Interfaces;

/// <summary>
/// The enforcement layer Track 8 milestone 8.3 adds: the risk status state machine, the
/// maker-checker (segregation of duties) rule, and the appetite gate.
///
/// One service rather than three because all three are consulted at the same two moments — a risk
/// is being saved, or a decision is being recorded about it — and splitting them would mean every
/// caller remembering to ask all three. The Track 7 audit's recurring lesson is that a control
/// applied at one call site out of several is not a control.
/// </summary>
public interface IRiskWorkflowService
{
    /// <summary>
    /// Refuses a status transition the state machine does not allow (8.3.1).
    ///
    /// Throws <see cref="Model.Exceptions.InvalidStateTransitionException"/>; returns silently when
    /// the transition is legal or when the status is unchanged.
    /// </summary>
    Task EnsureTransitionAllowedAsync(int riskId, string fromStatus, string toStatus);

    /// <summary>
    /// Refuses a decision made by someone too close to the risk (8.3.2) — its submitter, owner or
    /// manager. Administrators are <em>not</em> exempt.
    /// </summary>
    /// <param name="overrideReason">
    /// A stated break-glass reason. Honoured only when the <c>risk_workflow_segregation_break_glass</c>
    /// setting is on, and logged loudly when it is used.
    /// </param>
    Task EnsureSegregationOfDutiesAsync(int riskId, int actingUserId, string action,
        string? overrideReason = null);

    /// <summary>
    /// The appetite in force for a risk and what it implies. Never throws: the caller decides
    /// whether a ceiling breach is fatal (acceptance) or informational (a dashboard count).
    /// </summary>
    Task<AppetiteEvaluation> EvaluateAppetiteAsync(int riskId);

    /// <summary>Open risks whose residual score is above the appetite in force, per entity (8.3.3).</summary>
    Task<List<AppetiteBreachCount>> CountRisksAboveAppetiteAsync();

    /// <summary>Legacy rows that violate the state machine. Reported, never auto-mutated (8.3.1).</summary>
    Task<List<WorkflowViolation>> FindLegacyViolationsAsync();
}
