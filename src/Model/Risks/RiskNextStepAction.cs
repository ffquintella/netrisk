namespace Model.Risks;

/// <summary>
/// The risk-lifecycle stage implied by a management review's chosen next step.
/// Used to offer the next stage right after a review commits (IX-6 next-step affordance).
/// </summary>
public enum RiskNextStepAction
{
    /// <summary>The next step needs no immediate action inside the risk lifecycle.</summary>
    None = 0,

    /// <summary>Open the mitigation-planning flow.</summary>
    PlanMitigation = 1,

    /// <summary>Open the mitigation editor for an existing plan.</summary>
    ReviseMitigation = 2,

    /// <summary>Open the close-risk flow.</summary>
    CloseRisk = 3
}
