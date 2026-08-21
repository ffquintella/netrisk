using Xunit;
using RiskHelper = Model.Risks.RiskHelper;
using RiskNextStepAction = Model.Risks.RiskNextStepAction;

namespace ServerServices.Tests.Track1;

/// <summary>
/// Track 1 Milestone 1.5 (IX-6 next-step affordance): a management review's chosen next step must
/// resolve to the risk-lifecycle stage the GUI then offers. Only the seeded next_step values that
/// imply an in-app stage may map to one — everything else must stay <see cref="RiskNextStepAction.None"/>,
/// so the GUI stays silent rather than guessing at a stage.
/// </summary>
public class RiskNextStepActionTests
{
    [Theory]
    [InlineData(2, RiskNextStepAction.PlanMitigation)]  // "Consider for Project"
    [InlineData(4, RiskNextStepAction.CloseRisk)]       // "Reject"
    public void MapsTheNextStepsThatImplyAStage(int nextStep, RiskNextStepAction expected)
    {
        Assert.Equal(expected, RiskHelper.GetNextStepAction(nextStep));
    }

    [Theory]
    [InlineData(1)]  // "Accept until Next Review" — handled outside the risk lifecycle
    [InlineData(3)]  // "Submit as a Production Issue" — handled outside NetRisk
    public void SeededNextStepsWithoutAnInAppStageMapToNone(int nextStep)
    {
        Assert.Equal(RiskNextStepAction.None, RiskHelper.GetNextStepAction(nextStep));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnknownNextStepValuesMapToNone(int nextStep)
    {
        Assert.Equal(RiskNextStepAction.None, RiskHelper.GetNextStepAction(nextStep));
    }
}
