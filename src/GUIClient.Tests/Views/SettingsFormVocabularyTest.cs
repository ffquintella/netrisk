using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Guards the settings-form vocabulary. Rationale and history:
/// <c>roadmap/SETTINGS_FORM_ROLLOUT.md</c>.
/// </summary>
public class SettingsFormVocabularyTest
{
    /// <summary>
    /// Classes retired by the rollout, with what to use instead. A retired class is not merely
    /// unused: re-adding the style would make every old call site legal again.
    /// </summary>
    private static readonly Dictionary<string, string> Retired = new(StringComparer.Ordinal)
    {
        ["detailBlock"] = "use \"hint\" for field help, \"notice\" for a statement about state, " +
                          "or \"formData\" for a record's value in a read-only panel",
    };

    private static readonly Regex ClassAttribute =
        new(@"Classes\s*=\s*""(?<classes>[^""{}]*)""", RegexOptions.Compiled);

    private static readonly Regex SelectorClass =
        new(@"\.(?<name>[A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    private static readonly Regex SelectorAttribute =
        new(@"Selector\s*=\s*""(?<selector>[^""]*)""", RegexOptions.Compiled);

    // detailBlock painted black-on-DarkGray, which made a field's explanation the loudest thing
    // on a dark screen. All 56 call sites are gone; this keeps them gone.
    [Fact]
    public void NoViewReferencesARetiredClass()
    {
        var offences = new List<string>();

        foreach (var view in ViewFiles())
        {
            var text = File.ReadAllText(view);

            foreach (Match match in ClassAttribute.Matches(text))
            foreach (var name in match.Groups["classes"].Value
                         .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Retired.TryGetValue(name, out var advice))
                    offences.Add($"{Relative(view)} references Classes=\"{name}\" — {advice}");
            }
        }

        Assert.True(offences.Count == 0,
            "A retired style class is back in a view:" +
            Environment.NewLine + string.Join(Environment.NewLine, offences.Distinct()));
    }

    // Without this, re-adding the style would silently re-legalise the pattern.
    [Fact]
    public void NoStyleDefinesARetiredClass()
    {
        var defined = ClassesDefinedInStyles();
        var back = Retired.Keys.Where(defined.Contains).ToList();

        Assert.True(back.Count == 0,
            "A retired style class has been redefined: " + string.Join(", ", back));
    }

    // Avalonia does not report an unmatched selector, so a missing style renders unstyled.
    [Theory]
    [InlineData("hint")]
    [InlineData("notice")]
    [InlineData("fieldLabel")]
    [InlineData("sectionCaption")]
    [InlineData("formCard")]
    [InlineData("caution")]
    [InlineData("cautionIcon")]
    [InlineData("successIcon")]
    [InlineData("errorIcon")]
    public void TheSettingsFormVocabulary_IsDefinedByAStyle(string className)
        => Assert.Contains(className, ClassesDefinedInStyles());

    /// <summary>
    /// Every settings screen with fields has adopted the vocabulary. Named explicitly so a screen
    /// rewritten back to bare <c>header2</c> labels fails rather than drifting.
    /// </summary>
    [Theory]
    [InlineData("Views/Admin/IntegrationsView.axaml")]
    [InlineData("Views/Admin/GovernanceAdminView.axaml")]
    [InlineData("Views/Admin/FindingsAdminView.axaml")]
    [InlineData("Views/Admin/JiraIntegrationView.axaml")]
    [InlineData("Views/ConfigurationView.axaml")]
    [InlineData("Views/SecretVaultPickerDialog.axaml")]
    public void EverySettingsView_UsesFieldLabels(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(GuiClientRoot(), relativePath));
        Assert.Contains("Classes=\"fieldLabel\"", text);
    }

    /// <summary>
    /// A <c>formCard</c> stretches the inputs inside it, and a horizontal <c>StackPanel</c>
    /// measures a stretched child with infinite width — the box grows without bound and wrapped
    /// text beside it never wraps. This was a real defect twice during the rollout, so it is a
    /// test rather than a note.
    /// </summary>
    [Theory]
    [InlineData("Views/Admin/IntegrationsView.axaml")]
    [InlineData("Views/ConfigurationView.axaml")]
    public void NoFormCardPutsAnInputInAHorizontalStackPanel(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(GuiClientRoot(), relativePath));
        var horizontal = new Regex(@"<StackPanel[^>]*Orientation\s*=\s*""Horizontal""[^>]*>",
            RegexOptions.Compiled);
        var offences = new List<string>();

        foreach (var card in FormCardBodies(text))
        {
            foreach (Match open in horizontal.Matches(card))
            {
                // Only the panel's own run matters, so stop at its close tag.
                var after = card[open.Index..];
                var close = after.IndexOf("</StackPanel>", StringComparison.Ordinal);
                var inner = close < 0 ? after : after[..close];

                if (inner.Contains("<TextBox", StringComparison.Ordinal) ||
                    inner.Contains("<ComboBox", StringComparison.Ordinal))
                    offences.Add($"{relativePath}: a horizontal StackPanel inside a formCard holds " +
                                 "a TextBox/ComboBox — use a Grid with ColumnDefinitions instead");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences.Distinct()));
    }

    /// <summary>Each formCard Border's text up to its matching close tag.</summary>
    private static IEnumerable<string> FormCardBodies(string text)
    {
        var card = new Regex(@"<Border[^>]*Classes\s*=\s*""[^""]*\bformCard\b[^""]*""",
            RegexOptions.Compiled);

        foreach (Match m in card.Matches(text))
        {
            var depth = 0;
            var i = m.Index;

            while (true)
            {
                var open = text.IndexOf("<Border", i + 1, StringComparison.Ordinal);
                var close = text.IndexOf("</Border>", i + 1, StringComparison.Ordinal);
                if (close < 0) break;

                if (open >= 0 && open < close) { depth++; i = open; continue; }
                if (depth == 0) { yield return text[m.Index..close]; break; }

                depth--;
                i = close;
            }
        }
    }

    [Fact]
    public void TheViewTreeIsActuallyBeingScanned()
    {
        Assert.True(ViewFiles().Count > 50);
        Assert.True(StyleFiles().Count >= 3);
        Assert.Contains("dialog1", ClassesDefinedInStyles());
    }

    private static HashSet<string> ClassesDefinedInStyles()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in StyleFiles().Select(File.ReadAllText))
        foreach (Match selector in SelectorAttribute.Matches(document))
        foreach (Match name in SelectorClass.Matches(selector.Groups["selector"].Value))
            defined.Add(name.Groups["name"].Value);

        return defined;
    }

    private static IReadOnlyList<string> ViewFiles() =>
        Directory.GetFiles(Path.Combine(GuiClientRoot(), "Views"), "*.axaml", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> StyleFiles() =>
        Directory.GetFiles(Path.Combine(GuiClientRoot(), "Styles"), "*.axaml", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    private static string Relative(string path) =>
        Path.GetRelativePath(Path.GetDirectoryName(GuiClientRoot())!, path).Replace('\\', '/');

    /// <summary>Resolves <c>src/GUIClient</c> from this test file's own location.</summary>
    private static string GuiClientRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "GUIClient"));
}
