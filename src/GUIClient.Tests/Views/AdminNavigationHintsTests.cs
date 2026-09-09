using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The administration window navigates by icon alone — ten Material icons in the top-right corner and
/// no labels — so the hover hint is the only thing that says what an icon opens. Two ways that breaks:
/// a tenth icon lands without a <c>ToolTip.Tip</c> at all, or a hint is bound to the section's own
/// one-word label ("Deduplication") which names the tab rather than telling the operator that the SLA
/// policy, the risk acceptances and the CI API tokens are behind it too.
///
/// Source-text scanning, for the same reason as the other tests in this folder: GUIClient.Tests
/// deliberately does not reference GUIClient, because that would drag all of Avalonia into a headless
/// run.
/// </summary>
public class AdminNavigationHintsTests
{
    /// <summary>One entry per icon button in the navigation bar, in markup order.</summary>
    private static List<(string Button, string? Tip)> NavigationIcons()
    {
        var view = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Views/AdminWindow.axaml"));

        // Each icon is a Classes="navigation" Button wrapped in a Border that carries the hint; the
        // wrapper is what makes the hint reachable, so the Border is the unit being inspected.
        return Regex.Matches(view, @"<Border\b(?<attributes>[^>]*)>\s*(?<body>.*?)</Border>",
                             RegexOptions.Singleline)
            .Where(border => border.Groups["body"].Value.Contains("Classes=\"navigation\""))
            .Select(border => (
                Button: Regex.Match(border.Groups["body"].Value, @"Kind=""(?<kind>\w+)""")
                             .Groups["kind"].Value,
                Tip: Regex.Match(border.Groups["attributes"].Value,
                                 @"ToolTip\.Tip=""\{Binding (?<property>\w+)\}""") is { Success: true } tip
                    ? tip.Groups["property"].Value
                    : null))
            .ToList();
    }

    [Fact]
    public void EveryNavigationIconCarriesAHint()
    {
        var icons = NavigationIcons();

        // A sanity floor: if the regex stops matching the markup the rest of the test would pass
        // vacuously.
        Assert.Equal(10, icons.Count);

        var unhinted = icons.Where(icon => icon.Tip is null).Select(icon => icon.Button).ToList();

        Assert.True(unhinted.Count == 0,
            "The administration navigation bar is icons only, so an icon without ToolTip.Tip is "
            + $"unidentifiable: {string.Join(", ", unhinted)}");
    }

    [Fact]
    public void EveryHintBindsAPropertyTheViewModelActuallyExposes()
    {
        var viewModel = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/AdminViewModel.cs"));

        var missing = NavigationIcons()
            .Select(icon => icon.Tip)
            .OfType<string>()
            .Where(property => !viewModel.Contains($"public string {property} {{ get; }}"))
            .ToList();

        // An unresolved binding shows an empty tooltip rather than throwing, so nothing else catches it.
        Assert.True(missing.Count == 0,
            $"AdminViewModel does not expose: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryHintSaysMoreThanTheIconsSectionName()
    {
        var strings = LocalizedStrings();
        var sectionLabels = new[]
        {
            "Users", "Devices", "Configuration", "Entity Access", "IRP Templates", "Deduplication",
            "API tokens", "Integrations", "Governance", "Plugins"
        };

        var uninformative = new List<string>();

        foreach (var property in NavigationIcons().Select(icon => icon.Tip).OfType<string>())
        {
            var key = HintKey(property);
            Assert.True(strings.TryGetValue(key, out var hint),
                $"{property} reads Localizer[\"{key}\"], which Localization.resx does not declare.");

            // A hint that repeats the section name adds nothing over the icon itself.
            if (sectionLabels.Contains(hint, StringComparer.OrdinalIgnoreCase)
                || hint!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3)
                uninformative.Add($"  {key} — \"{hint}\"");
        }

        Assert.True(uninformative.Count == 0,
            "A navigation hint has to describe what the section holds, not restate its label:\n"
            + string.Join('\n', uninformative.Order()));
    }

    /// <summary>The resource key a <c>Str*Hint</c> property reads, taken from the view model source.</summary>
    private static string HintKey(string property)
    {
        var viewModel = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/AdminViewModel.cs"));

        var declaration = Regex.Match(
            viewModel, $@"public string {Regex.Escape(property)} {{ get; }} = Localizer\[""(?<key>[^""]+)""\]");

        Assert.True(declaration.Success, $"{property} is not declared as a Localizer lookup.");

        return declaration.Groups["key"].Value;
    }

    private static Dictionary<string, string> LocalizedStrings() =>
        XDocument
            .Load(Path.Combine(GuiClientSourceRoot(), "Resources/Localization.resx"))
            .Root!
            .Elements("data")
            .Where(data => data.Attribute("name") != null)
            .GroupBy(data => data.Attribute("name")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                          group => group.First().Element("value")?.Value ?? string.Empty,
                          StringComparer.Ordinal);

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");

            if (File.Exists(project))
                return Path.GetDirectoryName(project)!;
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
