using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Tests.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// S40 §5.0, §5.6.1, acceptance 10 (T254): the Explore panel is always visible — on the page itself, never in an
/// <c>Expander</c>, with no visibility of its own bound on the page — and it is the only place with a dimension selector.
/// Source scans, because this project does not load Avalonia.
/// </summary>
public class RiskExplorePanelAlwaysVisibleTest
{
    private static string Client => RiskIndexTestText.ClientDirectory();

    [Fact]
    public void TestTheExplorePanelIsNotInsideAnExpanderNorHiddenByThePage()
    {
        var page = File.ReadAllText(Path.Combine(Client, "Views", "RiskOverview", "RiskOverviewView.axaml"));

        var tag = Regex.Match(page, @"<riskOverview:RiskExplorePanel\b[^>]*/>");
        Assert.True(tag.Success, "RiskOverviewView no longer places the Explore panel.");
        var before = page[..tag.Index];
        Assert.Equal(Regex.Matches(before, "<Expander\\b").Count, Regex.Matches(before, "</Expander>").Count);
        Assert.DoesNotContain("IsVisible", tag.Value);
    }

    [Fact]
    public void TestOnlyTheExplorePanelChoosesTheDimension()
    {
        var binders = Directory.EnumerateFiles(Path.Combine(Client, "Views"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\{Binding (DimensionOptions|SelectedDimension)\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["RiskExplorePanel.axaml"], binders);
    }
}
