using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards every view against the binding that lost a Vision One connection's region.
///
/// <c>ComboBox.SelectedItem</c> is a two-way binding by default, and a ComboBox whose
/// <c>ItemsSource</c> does not contain the current value resets its selection to null and writes that
/// null into the source. Binding it straight at a model property therefore lets the control erase the
/// model: <c>IntegrationsView.axaml</c> bound it to <c>TrendMicroDraft.Region</c>, the region list is
/// empty on first render and is cleared again on every reload, and so the connection went to the
/// server with no region unless the operator happened to touch the dropdown. The server refused it
/// with "The Region field is required" — a message that took three rounds of investigation to see,
/// because the desktop client was dropping the response body at the time.
///
/// A view-model property that ignores the empty write is the fix. This test is what stops the direct
/// binding coming back, here or in any view added later: it is the shape of the binding that is
/// wrong, not one view's spelling of it.
///
/// Like the other tests in this folder, it scans source text — GUIClient.Tests deliberately does not
/// reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class ComboBoxSelectionBindingTests
{
    /// <summary>
    /// A <c>SelectedItem</c> (or <c>SelectedValue</c>) binding whose path has a dot in it — that is,
    /// one that reaches through a property into an object the view does not own.
    /// </summary>
    private static readonly Regex NestedSelectionBinding = new(
        @"Selected(?:Item|Value)\s*=\s*""\{\s*Binding\s+(?<path>[A-Za-z_]\w*(?:\.\w+)+)",
        RegexOptions.Compiled);

    [Fact]
    public void NoViewBindsASelectionStraightIntoAModelProperty()
    {
        var offenders = new List<string>();

        foreach (var view in Views())
        {
            foreach (Match match in NestedSelectionBinding.Matches(File.ReadAllText(view)))
            {
                var path = match.Groups["path"].Value;

                // Reaching into a view-model the view owns (`SomeChildViewModel.Property`) is fine;
                // what is not is a two-way selection pointed at a draft or entity the view is editing.
                if (!LooksLikeModelState(path)) continue;

                offenders.Add($"{Path.GetFileName(view)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A ComboBox selection is bound directly into model state. The control writes null into the "
            + "source whenever it cannot resolve the current value — on first render, and whenever the "
            + "ItemsSource is cleared — which silently erases the value. Bind to a view-model property "
            + "that ignores the empty write instead.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheRegionSelectionIsGuardedInTheViewModel()
    {
        var source = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/Admin/IntegrationsViewModel.cs"));

        // The property the region ComboBox binds to has to refuse an empty write; without that the
        // control's own reset still reaches TrendMicroDraft.Region.
        var property = Between(source, "public string? SelectedTrendMicroRegion", "\n    }");

        Assert.Contains("IsNullOrWhiteSpace", property);
        Assert.Contains("return", property);
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.True(from >= 0,
            "IntegrationsViewModel no longer declares SelectedTrendMicroRegion. If the region ComboBox "
            + "was rebound, it must still be to something that cannot be nulled by the control.");

        var to = source.IndexOf(end, from, StringComparison.Ordinal);

        return source[from..(to < 0 ? source.Length : to)];
    }

    /// <summary>
    /// True for a path that reads as data being edited rather than a nested view-model. Deliberately
    /// conservative: it names the shapes this codebase uses for editable state.
    /// </summary>
    private static bool LooksLikeModelState(string path)
    {
        var root = path.Split('.')[0];

        return root.EndsWith("Draft", StringComparison.Ordinal)
               || root.EndsWith("Entity", StringComparison.Ordinal)
               || root.EndsWith("Record", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(GuiClientSourceRoot(), "*.axaml", SearchOption.AllDirectories);

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
