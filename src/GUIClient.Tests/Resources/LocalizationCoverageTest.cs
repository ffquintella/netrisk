using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Resources;

/// <summary>
/// Every <c>Localizer["Key"]</c> in the desktop client resolves to a real resource, in every shipped
/// culture.
///
/// This exists because the milestone before it shipped a missing key: a help paragraph under the issue
/// templates read `IssueTemplatePlaceholdersMSG` on screen instead of a sentence. Nothing catches that
/// — the localizer returns the key name rather than throwing, the view renders, the build is clean, and
/// the defect only surfaces when somebody opens that tab and reads it.
///
/// Source is scanned as *text* rather than by referencing the client, because
/// <c>GUIClient.Tests</c> deliberately does not pull Avalonia into a headless run (see the csproj).
/// That is a limitation with a real upside here: it also catches keys used from XAML-adjacent code
/// that a runtime test would never reach.
/// </summary>
public class LocalizationCoverageTest
{
    /// <summary>The three resource files the client ships: the neutral fallback plus two cultures.</summary>
    private static readonly string[] ResourceFiles =
    [
        "Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx"
    ];

    /// <summary>
    /// Keys built at run time rather than written as literals, which this scan cannot see and which a
    /// missing-key check must not fail on. Each one is a documented exception, not a blanket ignore.
    /// </summary>
    private static readonly HashSet<string> Interpolated = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys that were already undeclared when this test was written, allowlisted with a reason each —
    /// the same shape as <c>ControllerAuthorizationInventoryTest</c>'s justified-anonymous list.
    ///
    /// The list exists so the guard is live for every new key without this change also rewriting six
    /// unrelated screens' strings. It is a record of a known state, not permission: nothing may be
    /// added to it, and the three marked as visible defects are worth fixing on their own.
    /// </summary>
    private static readonly Dictionary<string, string> PreExisting = new(StringComparer.Ordinal)
    {
        // Renders correctly by accident: the key name *is* the English label the localizer returns
        // when it finds nothing. Wrong for a Portuguese user, harmless for an English one.
        ["Assignee"] = "key name reads as its own English label",
        ["Configuration"] = "key name reads as its own English label",
        ["Ok"] = "key name reads as its own English label",
        ["Parallel"] = "key name reads as its own English label",
        ["Refresh"] = "key name reads as its own English label",
        ["User"] = "key name reads as its own English label",

        // Genuinely visible: these render as the literal key on screen. ErrorSavingMSG is the worst of
        // the three — ViewModelBase.RunAsync uses it as the generic write-failure toast, so every
        // unexpected save error in the client shows "ErrorSavingMSG" to the operator.
        ["ErrorSavingMSG"] = "VISIBLE DEFECT: the generic save-failure toast in ViewModelBase.RunAsync",
        ["AddDocumentMsg"] = "VISIBLE DEFECT: EditMitigationViewModel's add-document toast",
        ["SaveDocumentMsg"] = "VISIBLE DEFECT: the save-document toast in mitigations and file reports"
    };

    private static DirectoryInfo RepoRoot([CallerFilePath] string thisFile = "")
    {
        // .../src/GUIClient.Tests/Resources/LocalizationCoverageTest.cs → repo root
        var directory = new FileInfo(thisFile).Directory!;

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return directory!;
    }

    private static DirectoryInfo ClientDirectory() =>
        new(Path.Combine(RepoRoot().FullName, "src", "GUIClient"));

    private static Dictionary<string, HashSet<string>> DeclaredKeys()
    {
        var byFile = new Dictionary<string, HashSet<string>>();

        foreach (var file in ResourceFiles)
        {
            var path = Path.Combine(ClientDirectory().FullName, "Resources", file);

            Assert.True(File.Exists(path), $"{file} is missing from GUIClient/Resources.");

            byFile[file] = XDocument.Load(path).Root!
                .Elements("data")
                .Select(d => d.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToHashSet(StringComparer.Ordinal);
        }

        return byFile;
    }

    /// <summary>Every <c>Localizer["Key"]</c> literal in the client's source, with where it came from.</summary>
    private static List<(string Key, string File)> UsedKeys()
    {
        var pattern = new Regex(@"Localizer\[""(\w+)""\]", RegexOptions.Compiled);
        var used = new List<(string, string)>();
        var root = ClientDirectory();

        foreach (var source in root.GetFiles("*.cs", SearchOption.AllDirectories))
        {
            // bin/obj hold generated copies and stale builds; scanning them would report keys that
            // were removed from source as still in use.
            var relative = Path.GetRelativePath(root.FullName, source.FullName);

            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar)) continue;

            foreach (Match match in pattern.Matches(File.ReadAllText(source.FullName)))
                used.Add((match.Groups[1].Value, relative));
        }

        return used;
    }

    /// <summary>
    /// Every key used in the client is declared in the **neutral** resource file.
    ///
    /// The neutral file is the one that matters, because it is the fallback: a key missing from
    /// <c>pt-BR</c> renders in English, but a key missing from the neutral file has nothing to fall
    /// back to and renders as its own name. That is the exact defect this test was written after — a
    /// help paragraph under the issue templates read `IssueTemplatePlaceholdersMSG` on screen.
    /// </summary>
    [Fact]
    public void EveryLocalizerKeyUsedInTheClientIsDeclaredInTheNeutralResource()
    {
        var neutral = DeclaredKeys()["Localization.resx"];
        var used = UsedKeys();

        Assert.NotEmpty(used);

        var offenders = used
            .Where(u => !Interpolated.Contains(u.Key)
                        && !PreExisting.ContainsKey(u.Key)
                        && !neutral.Contains(u.Key))
            .Distinct()
            .Select(u => $"'{u.Key}' (used in {u.File})")
            .OrderBy(o => o)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A missing resource does not throw — the localizer returns the key name, so the label "
            + "renders as 'SomeKeyMSG' and the build stays clean. These would ship that way:"
            + string.Join("", offenders.Select(o => "\n  " + o)));
    }

    /// <summary>
    /// The allowlist above does not outlive what it describes.
    ///
    /// An entry that has been fixed, or whose key nobody uses any more, has to leave the list —
    /// otherwise the list slowly becomes a place where a real offender could hide behind a stale name
    /// that happens to match.
    /// </summary>
    [Fact]
    public void ThePreExistingAllowlistHasNoStaleEntries()
    {
        var neutral = DeclaredKeys()["Localization.resx"];
        var used = UsedKeys().Select(u => u.Key).ToHashSet(StringComparer.Ordinal);

        var stale = PreExisting.Keys
            .Where(key => neutral.Contains(key) || !used.Contains(key))
            .OrderBy(key => key)
            .Select(key => neutral.Contains(key)
                ? $"'{key}' is declared now — remove it from the allowlist"
                : $"'{key}' is no longer used — remove it from the allowlist")
            .ToList();

        Assert.True(stale.Count == 0, string.Join("\n  ", stale));
    }

    /// <summary>
    /// The Portuguese file declares every key the neutral one does.
    ///
    /// This is the culture that a gap actually degrades: a key the neutral file has and
    /// <c>pt-BR</c> does not renders in English inside an otherwise Portuguese screen. It holds today
    /// and is worth holding.
    ///
    /// <c>en-US</c> is deliberately **not** asserted the same way. It is 22 keys short of the neutral
    /// file, all of them from the report-template work and all keyed by their own English text
    /// (<c>"Add section"</c>), and every one falls back to a neutral value that is already correct
    /// English — so the gap is cosmetic. Asserting it would mean either failing on a pre-existing
    /// condition this change did not create, or quietly filling 22 entries as a side effect of a Jira
    /// feature. Neither is right; it is reported instead.
    /// </summary>
    [Fact]
    public void ThePortugueseResourceDeclaresEveryNeutralKey()
    {
        var declared = DeclaredKeys();

        var offenders = declared["Localization.resx"]
            .Except(declared["Localization.pt-BR.resx"])
            .OrderBy(k => k)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These keys would render in English on a Portuguese screen:"
            + string.Join("", offenders.Select(o => "\n  " + o)));
    }

    /// <summary>
    /// No culture file declares a key the neutral one lacks.
    ///
    /// Such a key is dead weight nothing reads — and worse, it reads as translated coverage that does
    /// not exist, because the code cannot be using a key the fallback has never heard of.
    /// </summary>
    [Fact]
    public void NoCultureResourceDeclaresAKeyTheNeutralOneLacks()
    {
        var declared = DeclaredKeys();
        var neutral = declared["Localization.resx"];

        var offenders = new List<string>();

        foreach (var (file, keys) in declared.Where(d => d.Key != "Localization.resx"))
            offenders.AddRange(keys.Except(neutral).OrderBy(k => k)
                .Select(extra => $"{file} declares '{extra}', which the neutral file does not"));

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
    }

    /// <summary>
    /// No resource is declared with an empty value.
    ///
    /// An empty value is worse than a missing key: the label renders as nothing at all, so the control
    /// looks broken rather than untranslated, and there is no string to search the source for.
    /// </summary>
    [Fact]
    public void NoResourceIsDeclaredEmpty()
    {
        var offenders = new List<string>();

        foreach (var file in ResourceFiles)
        {
            var path = Path.Combine(ClientDirectory().FullName, "Resources", file);

            foreach (var data in XDocument.Load(path).Root!.Elements("data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;

                if (name != null && string.IsNullOrWhiteSpace(value))
                    offenders.Add($"{file}: '{name}' has no value");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
    }
}
