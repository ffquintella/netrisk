using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The buttons of the Administration → Users view follow docs/ui-standard.md §4: one class from the §4.1 taxonomy each,
/// a captioned button in an auto-sized class, an icon-only one in <c>operation</c> with a tooltip and an accessible
/// name.
///
/// The view mixed them: Save and Change password were captioned <c>operation</c> buttons (a fixed-size icon class that
/// zeroes Semi's padding, so they rendered cramped and shorter than the captioned <c>dialog2</c> buttons beside the
/// permission list), the list toolbars paired <c>subButton</c> with <c>subButton type3</c> (two fills for one row), and
/// no icon-only button had a tooltip or an accessible name.
///
/// Scoped to this view on purpose: the rest of the app is tracked by the compliance audit, not by this test.
/// </summary>
public class UsersViewButtonsTest
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>§4.1 classes that size themselves to their content (§4.8).</summary>
    private static readonly HashSet<string> AutoSized = new(StringComparer.Ordinal) { "dialog1", "dialog2", "type2", "type3" };

    /// <summary>§4.1 classes for an icon-only button.</summary>
    private static readonly HashSet<string> IconOnly = new(StringComparer.Ordinal) { "operation" };

    [Fact]
    public void EveryButtonCarriesExactlyOneTaxonomyClassThatFitsItsContent()
    {
        var offenders = new List<string>();
        foreach (var button in Buttons())
        {
            var classes = ((string?)button.Attribute("Classes") ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var captioned = button.Descendants(Avalonia + "TextBlock").Any();
            var allowed = captioned ? AutoSized : IconOnly;

            if (classes.Length != 1 || !allowed.Contains(classes[0]))
                offenders.Add($"  {Describe(button)} Classes=\"{string.Join(' ', classes)}\" — expected one of " +
                              string.Join(", ", allowed.Order()) + (captioned ? " (captioned)" : " (icon-only)"));
        }

        Assert.True(offenders.Count == 0, "UsersView buttons outside the §4.1 taxonomy:" + Environment.NewLine +
                                          string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryIconOnlyButtonNamesItselfThroughALocalizedTooltipAndAccessibleName()
    {
        var offenders = Buttons()
            .Where(button => !button.Descendants(Avalonia + "TextBlock").Any())
            .Where(button => !IsStringBinding(button.Attribute("ToolTip.Tip")) ||
                             !IsStringBinding(button.Attribute("AutomationProperties.Name")))
            .Select(Describe)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Icon-only buttons with no localized ToolTip.Tip and AutomationProperties.Name: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheClearAllButtonHasACaption()
    {
        var clearAll = Buttons().Single(button => (string?)button.Attribute("Name") == "BtDeselectAll");

        var caption = clearAll.Descendants(Avalonia + "TextBlock").Select(t => (string?)t.Attribute("Text")).Single();
        Assert.Equal("{Binding StrCleanAll}", caption);
    }

    [Fact]
    public void NoButtonSizesItself()
    {
        var sized = Buttons()
            .Where(button => button.Attribute("Width") is not null || button.Attribute("Height") is not null ||
                             button.Attribute("MinWidth") is not null || button.Attribute("MinHeight") is not null)
            .Select(Describe)
            .ToList();

        Assert.True(sized.Count == 0, "Buttons take their size from their class (§4.8): " + string.Join(", ", sized));
    }

    private static bool IsStringBinding(XAttribute? attribute) =>
        attribute?.Value.Replace(" ", "").StartsWith("{BindingStr", StringComparison.Ordinal) == true;

    private static string Describe(XElement button) =>
        (string?)button.Attribute("Name") ?? $"Button(Command={(string?)button.Attribute("Command")})";

    private static List<XElement> Buttons()
    {
        var buttons = XDocument.Load(Path.Combine(GuiClientSourceRoot(), "Views", "UsersView.axaml"))
            .Descendants(Avalonia + "Button").ToList();
        Assert.True(buttons.Count >= 13, $"expected the 13 buttons of UsersView, found {buttons.Count}");
        return buttons;
    }

    private static string GuiClientSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project)) return Path.GetDirectoryName(project)!;
        }
        throw new InvalidOperationException($"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }
}
