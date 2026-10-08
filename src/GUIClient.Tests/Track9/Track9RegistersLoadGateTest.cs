using GUIClient.Tools.Track9;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9RegistersLoadGateTest
{
    [Fact]
    public void TestNewGenerationInvalidatesAnOlderRead()
    {
        var gate = new Track9RegistersLoadGate();
        var first = gate.Begin();
        var second = gate.Begin();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void TestDiscardInvalidatesOutstandingRead()
    {
        var gate = new Track9RegistersLoadGate();
        var token = gate.Begin();

        gate.DiscardOutstanding();

        Assert.False(gate.IsCurrent(token));
    }

    [Fact]
    public void TestParentSwitchInvalidatesParentAndChildReads()
    {
        var parentGate = new Track9RegistersLoadGate();
        var childGate = new Track9RegistersLoadGate();
        var parentToken = parentGate.Begin();
        var childToken = childGate.Begin();

        parentGate.DiscardOutstanding();
        childGate.DiscardOutstanding();

        Assert.False(parentGate.IsCurrent(parentToken));
        Assert.False(childGate.IsCurrent(childToken));
    }
}
