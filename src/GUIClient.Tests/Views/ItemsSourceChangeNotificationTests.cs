using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards every view against the binding that left the Flagged risks "Flag" dropdown empty.
///
/// An <c>ItemsSource</c> bound to a plain <c>List&lt;T&gt;</c> is read once, when the binding resolves.
/// <c>RiskFlagFilterViewModel.FlagOptions</c> was such a list, created empty and only filled by
/// <c>LoadAsync</c> after the view had bound it, so the ComboBox never saw the catalogue: its box still
/// read "All flags" (the selected item is a separately notified property) but the drop-down had nothing
/// in it, and the operator could not pick a flag.
///
/// A list filled in the view-model's constructor is safe — it is complete before any view binds it. A
/// list filled anywhere else has to be an <c>ObservableCollection</c> (or be replaced through a notifying
/// setter). Like the other tests in this folder, it scans source text, because GUIClient.Tests
/// deliberately does not reference GUIClient.
/// </summary>
public class ItemsSourceChangeNotificationTests
{
    private static readonly Regex DataType = new(
        @"x:DataType\s*=\s*""(?:\w+:)?(?<type>\w+)""", RegexOptions.Compiled);

    private static readonly Regex ItemsSourceBinding = new(
        @"ItemsSource\s*=\s*""\{\s*(?:Compiled)?Binding\s+(?<name>[A-Za-z_]\w*)\s*[,}]", RegexOptions.Compiled);

    [Fact]
    public void NoItemsSourceIsAPlainListFilledAfterConstruction()
    {
        var offenders = new List<string>();
        var viewModelSources = Directory
            .EnumerateFiles(Path.Combine(GuiClientSourceRoot(), "ViewModels"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        foreach (var view in Views())
        {
            var xaml = File.ReadAllText(view);
            var root = DataType.Match(xaml);
            if (!root.Success) continue;

            var type = root.Groups["type"].Value;
            var body = viewModelSources.Select(source => ClassBody(source, type)).FirstOrDefault(b => b != null);
            if (body == null) continue;

            foreach (var name in ItemsSourceBinding.Matches(xaml).Select(m => m.Groups["name"].Value).Distinct())
            {
                var declaredAsList = Regex.IsMatch(body,
                    $@"\b(?:I?List|IReadOnlyList)<[^;{{]+>\s+{name}\s*\{{\s*get;\s*\}}");
                if (!declaredAsList) continue;

                var outsideConstructor = WithoutConstructors(body, type);
                if (Regex.IsMatch(outsideConstructor, $@"\b{name}\s*\.\s*(?:Add|AddRange|Insert|Remove|RemoveAt|Clear)\s*\("))
                    offenders.Add($"{Path.GetFileName(view)}: ItemsSource=\"{{Binding {name}}}\" on {type}");
            }
        }

        Assert.True(offenders.Count == 0,
            "An ItemsSource is bound to a plain List that is filled after the view-model is constructed. "
            + "The control reads it once and never sees the items added later, so the list renders empty. "
            + "Declare it as an ObservableCollection.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFlaggedRisksFlagOptionsNotifyTheView()
    {
        var source = File.ReadAllText(Path.Combine(
            GuiClientSourceRoot(), "ViewModels/Track9/Track9RiskFlagsViewModels.cs"));
        var body = ClassBody(source, "RiskFlagFilterViewModel");

        Assert.NotNull(body);
        Assert.Matches(@"ObservableCollection<Track9Choice>\s+FlagOptions\s*\{\s*get;\s*\}", body);
    }

    /// <summary>The text of <c>class {type}</c> from its opening brace to the matching close.</summary>
    private static string? ClassBody(string source, string type)
    {
        var declaration = Regex.Match(source, $@"\bclass\s+{type}\b");
        if (!declaration.Success) return null;

        var open = source.IndexOf('{', declaration.Index);
        return open < 0 ? null : source[open..BlockEnd(source, open)];
    }

    /// <summary>The class body with every constructor's block removed.</summary>
    private static string WithoutConstructors(string body, string type)
    {
        var constructor = new Regex($@"\b{type}\s*\([^)]*\)\s*(?::\s*(?:base|this)\s*\([^)]*\)\s*)?\{{");
        for (var match = constructor.Match(body); match.Success; match = constructor.Match(body))
        {
            var open = match.Index + match.Length - 1;
            body = body[..match.Index] + body[BlockEnd(body, open)..];
        }

        return body;
    }

    /// <summary>The index just past the brace that closes the block opened at <paramref name="open"/>.</summary>
    private static int BlockEnd(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return i + 1;
        }

        return source.Length;
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
