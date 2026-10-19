using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GUIClient.Tests.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// S40 §5.12.1, §12.4 (T256): S39 §8.6 orders the data-quality items always visible, and §5.2.5 the governance indicators
/// beside them. Neither strip may sit inside an <c>Expander</c> nor have an <c>IsVisible</c> — its own or an ancestor's —
/// bound to a collapsible state. Source scan, because this project does not load Avalonia.
/// </summary>
public class RiskQualityStripAlwaysVisibleTest
{
    private static readonly string[] Strips = ["RiskQualityStrip", "RiskGovernanceIndicatorsStrip"];

    private static readonly Regex Collapsible = new(@"Expand|Collaps", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ViewsDirectory() => Path.Combine(RiskIndexTestText.ClientDirectory(), "Views", "RiskOverview");

    private static IEnumerable<string> Violations(XDocument page, string strip)
    {
        var placed = page.Descendants().Where(e => e.Name.LocalName == strip).ToList();
        if (placed.Count != 1)
        {
            yield return $"{strip} is placed {placed.Count} times; it must be on the page exactly once";
            yield break;
        }

        foreach (var element in placed[0].AncestorsAndSelf())
        {
            if (element.Name.LocalName == "Expander") yield return $"{strip} is inside an Expander";
            if (element.Attribute("IsVisible")?.Value is { } visible && Collapsible.IsMatch(visible))
                yield return $"{strip} or an ancestor ({element.Name.LocalName}) has IsVisible=\"{visible}\"";
        }
    }

    [Fact]
    public void TestTheStripsAreNeverInAnExpanderNorBehindACollapsibleState()
    {
        var page = XDocument.Load(Path.Combine(ViewsDirectory(), "RiskOverviewView.axaml"));

        var violations = Strips.SelectMany(strip => Violations(page, strip)).ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Inside its own view, a strip's root shows unconditionally and nothing in it collapses.</summary>
    [Theory]
    [InlineData("RiskQualityStrip.axaml")]
    [InlineData("RiskGovernanceIndicatorsStrip.axaml")]
    public void TestAStripsOwnViewHasNothingThatCollapses(string file)
    {
        var view = XDocument.Load(Path.Combine(ViewsDirectory(), file));
        var content = view.Root!.Elements().Single(e => !e.Name.LocalName.Contains('.'));

        Assert.Null(content.Attribute("IsVisible"));
        Assert.DoesNotContain(view.Descendants(), e => e.Name.LocalName == "Expander");
        Assert.DoesNotContain(view.Descendants(), e => e.Attribute("IsVisible")?.Value is { } v && Collapsible.IsMatch(v));
    }

    [Fact]
    public void TestTheScanCatchesAStripInAnExpanderOrBehindIsExpanded()
    {
        var nested = XDocument.Parse("""
            <UserControl xmlns="https://github.com/avaloniaui" xmlns:r="clr-namespace:X">
              <StackPanel>
                <Expander><r:RiskQualityStrip/></Expander>
                <StackPanel IsVisible="{Binding Governance.IsExpanded}"><r:RiskGovernanceIndicatorsStrip/></StackPanel>
              </StackPanel>
            </UserControl>
            """);
        var missing = XDocument.Parse("""<UserControl xmlns="https://github.com/avaloniaui"><StackPanel/></UserControl>""");

        Assert.Contains(Violations(nested, "RiskQualityStrip"), v => v.Contains("Expander"));
        Assert.Contains(Violations(nested, "RiskGovernanceIndicatorsStrip"), v => v.Contains("IsExpanded"));
        Assert.Contains(Violations(missing, "RiskQualityStrip"), v => v.Contains("0 times"));
    }
}
