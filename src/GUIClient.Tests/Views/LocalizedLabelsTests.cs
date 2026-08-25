using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Keeps user-facing text out of the views. <c>./build.sh LintUi</c> reports rule R5 (hardcoded
/// user-facing string) as a warning, so a violation is easy to reintroduce and easy to miss in
/// build output; the repo now has none, and this test is what holds that line.
///
/// Source-text scanning, for the same reason as <c>DialogViewConventionTests</c>: GUIClient.Tests
/// does not reference GUIClient, because that would drag all of Avalonia into a headless run.
/// </summary>
public class LocalizedLabelsTests
{
    /// <summary>Rule R5, verbatim from <c>build/Build.cs</c> so the two cannot drift apart.</summary>
    private static readonly Regex HardcodedText =
        new(@"<TextBlock\b[^>]*\bText\s*=\s*""([A-Z][a-z]+[^""]*)""", RegexOptions.Compiled);

    /// <summary>Its two exemptions, also from the linter.</summary>
    private static readonly string[] NotUserFacing = ["Binding", "TemplateBinding"];

    [Fact]
    public void NoViewRendersAHardcodedLabel()
    {
        var offenders = new List<string>();

        foreach (var file in ViewFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = HardcodedText.Match(lines[index]);
                if (!match.Success)
                {
                    continue;
                }

                var text = match.Groups[1].Value;
                if (NotUserFacing.Contains(text) || text.StartsWith('{'))
                {
                    continue;
                }

                offenders.Add($"  {Path.GetFileName(file)}:{index + 1} — \"{text}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} view(s) render text that cannot be translated. Move it to "
            + "Resources/Localization*.resx and bind a Str* property (ui-standard R5):\n"
            + string.Join('\n', offenders.Order()));
    }

    /// <summary>
    /// <c>AvaloniaExtraControls.MultiSelect</c> defaults its two column headers to the English
    /// literals "Available" and "Selected" — it is a generic control library and knows nothing of
    /// NetRisk's localizer — so every use has to supply them.
    /// </summary>
    [Fact]
    public void EveryMultiSelectSuppliesItsOwnColumnHeaders()
    {
        var missing = ViewFiles()
            .Select(file => (File: Path.GetFileName(file), Text: File.ReadAllText(file)))
            .SelectMany(view => Regex.Matches(view.Text, @"<multiSelect:MultiSelect\b[^>]*>",
                                              RegexOptions.Singleline)
                .Where(use => !use.Value.Contains("StrAvailable") || !use.Value.Contains("StrSelected"))
                .Select(use => $"  {view.File} — {Summarise(use.Value)}"))
            .Order()
            .ToList();

        Assert.True(missing.Count == 0,
            "MultiSelect uses that fall back to the control's English defaults:\n"
            + string.Join('\n', missing));
    }

    private static string Summarise(string element)
    {
        var name = Regex.Match(element, @"\bName\s*=\s*""(?<name>[^""]+)""");
        var title = Regex.Match(element, @"\bTitle\s*=\s*""(?<title>[^""]+)""");
        return name.Success ? name.Groups["name"].Value
             : title.Success ? title.Groups["title"].Value
             : "unnamed";
    }

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(GuiClientSourceRoot(), "Views"), "*.axaml",
                                 SearchOption.AllDirectories);

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
