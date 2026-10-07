using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards the Vulnerability editor dialog's layout contract. The window was reorganised to follow
/// <c>docs/ui-standard.md</c> §5.2 (responsive sizing) and §5.6 (scrolling): the form must stretch
/// and scroll instead of relying on pixel heights, and every string it renders must resolve.
///
/// Like <c>DialogViewConventionTests</c>, this scans source text: GUIClient.Tests deliberately does
/// not reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class EditVulnerabilitiesDialogTests
{
    private const string View = "Views/EditVulnerabilitiesDialog.axaml";
    private const string ViewModel = "ViewModels/EditVulnerabilitiesDialogViewModel.cs";

    /// <summary><c>{Binding Something}</c>, ignoring anything after the property path.</summary>
    private static readonly Regex Binding =
        new(@"\{Binding\s+(?<path>[A-Za-z_][\w.]*)", RegexOptions.Compiled);

    /// <summary><c>public string StrSomething { get; }</c> and friends.</summary>
    private static readonly Regex StringProperty =
        new(@"public\s+(?:new\s+)?string\??\s+(?<name>\w+)\s*(?:\{|=>)", RegexOptions.Compiled);

    /// <summary><c>Localizer["SomeKey"]</c>.</summary>
    private static readonly Regex LocalizerKey =
        new(@"Localizer\[""(?<key>[^""]+)""\]", RegexOptions.Compiled);

    [Fact]
    public void TheWindowIsResizableWithASizeFloorInsteadOfAPinnedSize()
    {
        var window = WindowElement();

        // DialogWindowBase pins Min = the opening size when no Min is declared, so a dialog without
        // these cannot be made smaller than the screen it was designed on.
        Assert.Equal("True", (string?) window.Attribute("CanResize"));
        Assert.NotNull(window.Attribute("MinWidth"));
        Assert.NotNull(window.Attribute("MinHeight"));

        Assert.True(double.Parse((string) window.Attribute("MinWidth")!) <
                    double.Parse((string) window.Attribute("Width")!),
                    "MinWidth must be a floor below the opening Width, not a second fixed size.");
        Assert.True(double.Parse((string) window.Attribute("MinHeight")!) <
                    double.Parse((string) window.Attribute("Height")!),
                    "MinHeight must be a floor below the opening Height, not a second fixed size.");
    }

    [Fact]
    public void TheFormStretchesAndScrollsRatherThanUsingFixedHeights()
    {
        var view = WindowElement();
        AvaloniaNamespace(view, out var avalonia);

        var sized = view.Descendants()
            .Where(element => element.Attribute("Height") is not null)
            .Select(element => element.Name.LocalName)
            .ToList();

        Assert.True(sized.Count == 0,
            "Content must be sized by the grid, not in pixels (ui-standard §5.8). Offenders: "
            + string.Join(", ", sized));

        // The free-text boxes take the vertical slack; a floor keeps them usable when it runs out.
        var textBoxes = view.Descendants(avalonia + "TextBox")
            .Where(box => box.Attribute("AcceptsReturn")?.Value == "True")
            .ToList();
        Assert.Equal(3, textBoxes.Count);
        Assert.All(textBoxes, box => Assert.NotNull(box.Attribute("MinHeight")));

        // …and the whole form scrolls when the window is too short for even the floors.
        Assert.Contains(view.Descendants(avalonia + "ScrollViewer"),
                        scroller => scroller.Attribute("VerticalScrollBarVisibility")?.Value == "Auto");
    }

    [Fact]
    public void EveryStringTheDialogRendersResolvesToAViewModelProperty()
    {
        // ViewModelBase carries the strings every dialog shares (Save, Cancel, the MultiSelect
        // column headers), so it counts as declared too.
        var declared = new[] { ViewModel, "ViewModels/ViewModelBase.cs" }
            .SelectMany(file => StringProperty.Matches(File.ReadAllText(Path.Combine(GuiClientSourceRoot(), file))))
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = Binding.Matches(File.ReadAllText(Path.Combine(GuiClientSourceRoot(), View)))
            .Select(match => match.Groups["path"].Value)
            .Where(path => path.StartsWith("Str", StringComparison.Ordinal) && !declared.Contains(path))
            .Distinct()
            .Order()
            .ToList();

        Assert.True(missing.Count == 0,
            "Bound to labels the view model does not expose (they render empty): "
            + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("Resources/Localization.resx")]
    [InlineData("Resources/Localization.en-US.resx")]
    [InlineData("Resources/Localization.pt-BR.resx")]
    public void EveryLocalizationKeyTheDialogAsksForExists(string resource)
    {
        var keys = XDocument.Load(Path.Combine(GuiClientSourceRoot(), resource))
            .Root!.Elements("data")
            .Select(data => (string) data.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = LocalizerKey.Matches(File.ReadAllText(Path.Combine(GuiClientSourceRoot(), ViewModel)))
            .Select(match => match.Groups["key"].Value)
            .Where(key => !keys.Contains(key))
            .Distinct()
            .Order()
            .ToList();

        Assert.True(missing.Count == 0,
            $"{resource} is missing keys the vulnerability dialog asks for (the Localizer echoes the "
            + "key back as the label): " + string.Join(", ", missing));
    }

    /// <summary>
    /// GitHub #79 — the form carries a labelled "Vulnerability source" selector bound to the view model's
    /// options and selection, and the label is a localized string rather than literal copy.
    /// </summary>
    [Fact]
    public void TheFormHasALabelledVulnerabilitySourceSelector()
    {
        var view = WindowElement();
        AvaloniaNamespace(view, out var avalonia);

        var combo = Assert.Single(view.Descendants(avalonia + "ComboBox"),
            box => (string?) box.Attribute("ItemsSource") == "{Binding SourceTypeOptions}");
        Assert.Equal("{Binding SelectedSourceType}", (string?) combo.Attribute("SelectedItem"));

        // Its label sits in the same grid cell row, in the label column.
        var label = Assert.Single(view.Descendants(avalonia + "TextBlock"),
            text => (string?) text.Attribute("Text") == "{Binding StrVulnerabilitySource}");
        Assert.Equal((string?) combo.Attribute("Grid.Row"), (string?) label.Attribute("Grid.Row"));
        Assert.Equal("0", (string?) label.Attribute("Grid.Column"));
        Assert.Equal("form_label", (string?) label.Attribute("Classes"));
        Assert.Same(label.Parent, combo.Parent);
    }

    /// <summary>
    /// GitHub #79 — the selection reaches the entity that is saved, and a stored value is shown when the
    /// dialog opens on an existing vulnerability. Scanned as text for the reason in the class summary.
    /// </summary>
    [Fact]
    public void TheViewModelLoadsAndSavesTheSourceType()
    {
        var source = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), ViewModel));

        Assert.Matches(@"Vulnerability\.SourceType\s*=\s*SelectedSourceType\?\.Value", source);
        Assert.Matches(@"SelectedSourceType\s*=\s*VulnerabilitySourceSummary\.OptionFor\(\s*SourceTypeOptions,\s*Vulnerability\?\.SourceType",
            source);
    }

    /// <summary>Adding the selector renumbered the tab order; no two controls may share a stop.</summary>
    [Fact]
    public void EveryTabIndexIsUnique()
    {
        var duplicates = WindowElement().Descendants()
            .Select(element => (string?) element.Attribute("TabIndex"))
            .Where(index => index is not null)
            .GroupBy(index => index)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicate TabIndex values: " + string.Join(", ", duplicates));
    }

    private static XElement WindowElement() =>
        XDocument.Load(Path.Combine(GuiClientSourceRoot(), View)).Root!;

    private static void AvaloniaNamespace(XElement window, out XNamespace avalonia) =>
        avalonia = window.Name.Namespace;

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project))
            {
                return Path.GetDirectoryName(project)!;
            }
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
