using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

[TestSubject(typeof(HostSummaryLine))]
public class HostSummaryLineTest
{
    [Fact]
    public void BlankPartsAreLeftOutRatherThanLeavingADanglingSeparator()
    {
        Assert.Equal("10.0.0.1 · Windows", HostSummaryLine.Join("10.0.0.1", null, "  ", "Windows "));
        Assert.Equal(string.Empty, HostSummaryLine.Join(null, ""));
    }

    [Fact]
    public void ALabelledPartIsDroppedWhenItHasNoValue()
    {
        Assert.Equal("Owner: Infra", HostSummaryLine.Labelled("Owner", " Infra "));
        Assert.Null(HostSummaryLine.Labelled("Owner", " "));
    }
}
