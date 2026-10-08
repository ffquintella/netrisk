using GUIClient.Tools.Track9;
using Xunit;

namespace GUIClient.Tests.Tools.Track9;

public class Track9MonitoringLoadGateTest
{
    [Fact]
    public void OnlyNewestLoadMayPublish()
    {
        var gate = new Track9MonitoringLoadGate();

        var first = gate.Begin();
        var second = gate.Begin();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void ClearDiscardsAnOutstandingLoad()
    {
        var gate = new Track9MonitoringLoadGate();
        var outstanding = gate.Begin();

        gate.DiscardOutstanding();

        Assert.False(gate.IsCurrent(outstanding));
    }
}
