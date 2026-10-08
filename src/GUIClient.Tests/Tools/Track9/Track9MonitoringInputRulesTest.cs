using GUIClient.Tools.Track9;
using Xunit;

namespace GUIClient.Tests.Tools.Track9;

public class Track9MonitoringInputRulesTest
{
    [Fact]
    public void BacktestRequiresEitherRisksOrAnExplicitNoScenarioAnswer()
    {
        Assert.False(Track9MonitoringInputRules.CanAssessBacktest([], false));
        Assert.True(Track9MonitoringInputRules.CanAssessBacktest([], true));
        Assert.True(Track9MonitoringInputRules.CanAssessBacktest([8, 8, 3], false));
        Assert.False(Track9MonitoringInputRules.CanAssessBacktest([8], true));
    }

    [Fact]
    public void PositiveIdsAreDistinctAndKeepTheirFirstOrder()
    {
        Assert.Equal([8, 3], Track9MonitoringInputRules.PositiveDistinctIds([8, 0, 8, -1, 3]));
    }
}
