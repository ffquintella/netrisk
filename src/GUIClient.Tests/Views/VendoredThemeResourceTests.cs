using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The desktop client's base theme is Semi.Avalonia, but one control — TreeDataGrid, vendored as the
/// <c>libs/TreeDataGrid.Avalonia</c> submodule — ships only a Fluent theme, and App.axaml includes
/// it because there is no Semi equivalent. That theme builds its brushes from
/// <c>{StaticResource System*Color}</c>, keys FluentTheme defined and Semi does not.
///
/// The failure mode is worth spelling out, because it looks nothing like its cause: Avalonia builds
/// a style's resource dictionary lazily, the first time a lookup walks the application's styles and
/// reaches it. So a missing <c>System*Color</c> does not render a grid wrong and does not fail on a
/// TreeDataGrid screen — it throws <c>KeyNotFoundException</c> out of a layout pass on whichever
/// screen happens to ask for an unrelated DynamicResource first (in practice, the risk view), and an
/// exception in a layout pass is unhandled and ends the process.
///
/// Nothing at build time sees it: the include compiles, the keys are resolved at runtime. Hence this
/// test — every <c>StaticResource</c> key a vendored theme asks of the host application has to be
/// defined in <c>Styles/Tokens.axaml</c>.
///
/// Source-text scanning, for the same reason as the rest of this folder: GUIClient.Tests does not
/// reference GUIClient, because that would pull all of Avalonia into a headless run.
/// </summary>
public class VendoredThemeResourceTests
{
    /// <summary>
    /// A theme App.axaml includes from outside the Avalonia/Semi packages, paired with the source
    /// file in the submodule it is compiled from. The <c>avares</c> assembly name is what the test
    /// checks App.axaml for, so dropping or renaming the include fails here rather than silently
    /// leaving this test asserting about a file nobody loads.
    /// </summary>
    private static readonly (string AvaresAssembly, string SourceFile)[] VendoredThemes =
    [
        ("Avalonia.Controls.TreeDataGrid",
            "libs/TreeDataGrid.Avalonia/src/Avalonia.Controls.TreeDataGrid/Themes/Fluent.axaml")
    ];

    /// <summary>Prefixes owned by a sheet other than the token sheet: Semi's own palette, which its
    /// theme defines, and NetRisk's tokens, which ThemeTokenLayerTests already covers.</summary>
    private static readonly string[] ForeignPrefixes = ["Semi", "Nr"];

    [Fact]
    public void EveryVendoredThemeIsStillIncludedByTheApplication()
    {
        var app = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "App.axaml"));

        var missing = VendoredThemes
            .Where(theme => !app.Contains($"avares://{theme.AvaresAssembly}/", StringComparison.Ordinal))
            .Select(theme => theme.AvaresAssembly)
            .ToList();

        Assert.True(missing.Count == 0,
            "App.axaml no longer includes these vendored themes. If that is deliberate, drop the " +
            "entry from VendoredThemes here and the keys it needed from Styles/Tokens.axaml:\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryResourceAVendoredThemeAsksOfTheApplicationIsDefined()
    {
        var defined = Regex.Matches(File.ReadAllText(TokenSheet()), @"x:Key=""(?<key>[\w.]+)""")
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(defined);

        var dangling = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (_, sourceFile) in VendoredThemes)
        {
            var path = Path.Combine(RepositoryRoot(), sourceFile.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"The vendored theme {sourceFile} is missing. Is the submodule checked out?");

            var text = File.ReadAllText(path);

            // Keys the theme defines itself are answered by its own dictionary; only the ones it
            // reaches outward for are the host application's problem.
            var own = Regex.Matches(text, @"x:Key=""(?<key>[\w.]+)""")
                .Select(match => match.Groups["key"].Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (Match reference in Regex.Matches(text, @"\{StaticResource\s+(?<key>[\w.]+)\}"))
            {
                var key = reference.Groups["key"].Value;

                if (own.Contains(key) || defined.Contains(key))
                    continue;

                if (ForeignPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;

                dangling.Add($"{key}  ({sourceFile})");
            }
        }

        Assert.True(dangling.Count == 0,
            "These resources are asked of the application by a vendored theme and defined nowhere. " +
            "A StaticResource that misses throws KeyNotFoundException while a style's dictionary is " +
            "being built — during a layout pass, unhandled, on an unrelated screen. Define them in " +
            "Styles/Tokens.axaml:\n  " + string.Join("\n  ", dangling));
    }

    private static string TokenSheet() => Path.Combine(GuiClientSourceRoot(), "Styles", "Tokens.axaml");

    private static string GuiClientSourceRoot() => Path.Combine(RepositoryRoot(), "src", "GUIClient");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "netrisk.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find src/netrisk.sln walking up from {AppContext.BaseDirectory}.");
    }
}
