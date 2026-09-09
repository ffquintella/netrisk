using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards the Mitigation dialog against the layout that made it ignore the window height.
///
/// The dialog is <c>CanResize="True"</c> and opens 760px tall, but its root grid was
/// <c>RowDefinitions="Auto, *"</c> with every control — title, fields, Save/Cancel — parented in row 0
/// and nothing at all in row 1. An <c>Auto</c> row is sized to its content, so the whole form was
/// pinned to its natural height at the top of the window and the star row below it swallowed the
/// remaining ~40% as dead space; the four multi-line inputs stayed at their hard-coded
/// <c>Height="80"</c> no matter how large the operator made the window.
///
/// Two things therefore have to stay true, and this test asserts them separately because they fail
/// separately: the body has to live in a row that grows, and the inputs inside it have to be free to
/// grow with it.
///
/// Like the other tests in this folder, it reads the markup as text/XML — GUIClient.Tests deliberately
/// does not reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class EditMitigationWindowLayoutTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";

    [Fact]
    public void EveryStarRowOfTheDialogIsOccupied()
    {
        var offenders = (from grid in Markup().Descendants(XName.Get("Grid", AvaloniaNamespace))
                         let definition = (string?)grid.Attribute("RowDefinitions")
                         where definition is not null
                         let rows = definition.Split(',').Select(r => r.Trim()).ToArray()
                         from index in Enumerable.Range(0, rows.Length)
                         where rows[index].Contains('*') && !IsOccupied(grid, index)
                         select $"row {index} of \"{definition}\"").ToList();

        Assert.True(offenders.Count == 0,
            "A star row of the Mitigation dialog has no child in it, so the space it claims is dead: "
            + "the content is sized by its Auto rows and stops short of the window's height. Put the "
            + "body in the row that grows.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheMultiLineInputsAreFreeToGrow()
    {
        var offenders = (from input in Markup().Descendants()
                         where (string?)input.Attribute("AcceptsReturn") == "True"
                         let height = (string?)input.Attribute("Height")
                         where height is not null
                         select $"{input.Name.LocalName} Height=\"{height}\"").ToList();

        Assert.True(offenders.Count == 0,
            "A multi-line input in the Mitigation dialog has a fixed Height, so it keeps its original "
            + "size however tall the window is. Use MinHeight and let the row stretch it.\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheBodySitsInTheRowThatGrows()
    {
        var body = Markup().Descendants(XName.Get("Grid", AvaloniaNamespace))
            .Where(g => (string?)g.Attribute("RowDefinitions") is { } d && d.Contains('*'))
            .SelectMany(grid => grid.Elements()
                .Where(child => StarRowsOf(grid).Contains(RowOf(child)))
                .Select(child => child.Name.LocalName))
            .ToList();

        // The fields, not the title or the buttons, are what has to absorb the extra height.
        Assert.Contains("ScrollViewer", body);
    }

    private static bool IsOccupied(XElement grid, int row) =>
        grid.Elements()
            .Where(child => !child.Name.LocalName.Contains('.'))
            .Any(child =>
            {
                var start = RowOf(child);
                return row >= start && row < start + SpanOf(child);
            });

    private static int[] StarRowsOf(XElement grid)
    {
        var rows = ((string)grid.Attribute("RowDefinitions")!).Split(',').Select(r => r.Trim()).ToArray();
        return Enumerable.Range(0, rows.Length).Where(i => rows[i].Contains('*')).ToArray();
    }

    private static int RowOf(XElement element) => Attached(element, "Row", fallback: 0);

    private static int SpanOf(XElement element) => Attached(element, "RowSpan", fallback: 1);

    private static int Attached(XElement element, string property, int fallback)
    {
        var value = (string?)element.Attribute(XName.Get($"Grid.{property}", AvaloniaNamespace))
                    ?? (string?)element.Attribute($"Grid.{property}");

        return value is not null && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static XElement Markup() => XElement.Parse(File.ReadAllText(DialogPath()));

    private static string DialogPath() =>
        Path.Combine(GuiClientSourceRoot(), "Views", "EditMitigationWindow.axaml");

    private static string GuiClientSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "GUIClient");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the GUIClient source directory.");
    }
}
