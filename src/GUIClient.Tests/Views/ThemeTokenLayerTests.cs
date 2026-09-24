using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The desktop client's colours are defined in exactly one file, <c>Styles/Tokens.axaml</c>, and
/// every other style sheet names a token instead of a value. That is what made swapping the base
/// theme for Semi.Avalonia a one-file edit rather than a ninety-three-view edit, and it is only true
/// for as long as nobody adds a literal back.
///
/// <c>./build.sh LintUi</c> already forbids literal colours in <c>Views/</c>. It does not look at
/// <c>Styles/</c> — which is correct, since the styles are where colour is *supposed* to live — so
/// the style sheets need their own guard, and that is this class.
///
/// The second test covers the failure mode that is invisible at build time: Avalonia resolves a
/// <c>DynamicResource</c> at runtime and silently leaves the property at its default when the key
/// does not exist. A typo in a token name therefore compiles, passes every other test, and shows up
/// as one control rendered in the wrong colour that nobody notices for a release.
/// </summary>
public class ThemeTokenLayerTests
{
    /// <summary>The sheets that may name a colour but must not define one.</summary>
    private static readonly string[] ConsumerSheets =
    [
        "Styles/WindowStyles.axaml",
        "Styles/DarkStyles.axaml",
        "Styles/ComponentStyles.axaml"
    ];

    /// <summary>Named colours that carry no palette decision.</summary>
    private static readonly HashSet<string> ColourlessKeywords =
        new(StringComparer.Ordinal) { "Transparent" };

    [Fact]
    public void OnlyTheTokenSheetDefinesAColour()
    {
        var offenders = new List<string>();

        foreach (var sheet in ConsumerSheets)
        {
            var text = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), sheet));

            foreach (Match setter in Regex.Matches(
                         text, @"Property=""(?<property>Background|Foreground|BorderBrush)""\s+Value=""(?<value>[^""]+)"""))
            {
                var value = setter.Groups["value"].Value;

                if (value.StartsWith('{') || ColourlessKeywords.Contains(value))
                    continue;

                offenders.Add($"{sheet}: {setter.Groups["property"].Value}=\"{value}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            "These setters hold a colour instead of a token. Add the colour to Styles/Tokens.axaml " +
            "and reference it with {DynamicResource Nr…} — a literal here is a colour the next theme " +
            "change has to find:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryTokenReferencedIsDefined()
    {
        var defined = Regex.Matches(File.ReadAllText(TokenSheet()), @"x:Key=""(?<key>Nr\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(defined);

        var dangling = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(GuiClientSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match reference in Regex.Matches(File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource\s+(?<key>Nr\w+)\}"))
            {
                if (!defined.Contains(reference.Groups["key"].Value))
                    dangling.Add($"{reference.Groups["key"].Value}  ({Path.GetFileName(file)})");
            }
        }

        Assert.True(dangling.Count == 0,
            "These tokens are referenced but not defined in Styles/Tokens.axaml. Avalonia resolves a " +
            "DynamicResource at runtime and leaves the property at its default when the key is " +
            "missing, so this renders wrong rather than failing:\n  " + string.Join("\n  ", dangling));
    }

    /// <summary>
    /// Every token is used. A token nobody references is a colour decision with no subject, and the
    /// set of them is what somebody reads to understand the palette — so a stale one is misleading
    /// in a way an unused private field is not.
    /// </summary>
    [Fact]
    public void EveryTokenDefinedIsReferenced()
    {
        var defined = Regex.Matches(File.ReadAllText(TokenSheet()), @"x:Key=""(?<key>Nr\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToList();

        var referenced = Directory
            .EnumerateFiles(GuiClientSourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("Tokens.axaml", StringComparison.Ordinal))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\{(?:Dynamic|Static)Resource\s+(?<key>Nr\w+)\}")
                                     .Select(match => match.Groups["key"].Value))
            .ToHashSet(StringComparer.Ordinal);

        var unused = defined.Where(token => !referenced.Contains(token)).ToList();

        Assert.True(unused.Count == 0,
            "These tokens are defined in Styles/Tokens.axaml and used nowhere:\n  " +
            string.Join("\n  ", unused));
    }

    private static string TokenSheet() => Path.Combine(GuiClientSourceRoot(), "Styles", "Tokens.axaml");

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
