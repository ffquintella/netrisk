using System.Globalization;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// The remembered list width and tab (S38 §3.1, §3.3). A stored value written by hand or by an older
/// build must come back as a usable layout, never an exception or an off-screen pane.
/// </summary>
[TestSubject(typeof(HostsViewLayout))]
public class HostsViewLayoutTest
{
    [Theory]
    [InlineData(null, 280)]
    [InlineData("", 280)]
    [InlineData("wide", 280)]
    [InlineData("NaN", 280)]
    [InlineData("300", 300)]
    [InlineData("312.6", 312.6)]
    [InlineData("100", 220)]
    [InlineData("9000", 520)]
    public void TheStoredWidthIsParsedAndBounded(string? stored, double expected)
    {
        Assert.Equal(expected, HostsViewLayout.ParseWidth(stored), 3);
    }

    [Fact]
    public void TheWidthIsStoredAsWholeInvariantPixels()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            Assert.Equal("313", HostsViewLayout.FormatWidth(312.6));
            Assert.Equal(313, HostsViewLayout.ParseWidth(HostsViewLayout.FormatWidth(312.6)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("x", 0)]
    [InlineData("-1", 0)]
    [InlineData("5", 0)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    public void TheStoredTabIsParsedAndFallsBackToVulnerabilities(string? stored, int expected)
    {
        Assert.Equal(expected, HostsViewLayout.ParseTab(stored));
    }

    [Fact]
    public void TheViewDeclaresTheSameBoundsAsTheLayout()
    {
        var view = HostsTestFiles.Read("Views/HostsView.axaml");

        Assert.Contains(
            $"MinWidth=\"{HostsViewLayout.MinLeftPaneWidth}\" MaxWidth=\"{HostsViewLayout.MaxLeftPaneWidth}\"", view);
    }
}
