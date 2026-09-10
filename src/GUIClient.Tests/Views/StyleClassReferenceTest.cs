using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Every <c>Classes="…"</c> token a view references is actually defined by a style.
///
/// This is the failure mode the UI linter cannot see. <c>./build.sh LintUi</c> checks that a
/// button *has* a class; it cannot check that the class *exists*. Avalonia does not complain
/// about an unmatched style class — the selector simply never applies — so a typo compiles, runs,
/// and renders an unstyled control that looks almost right. Closing UI-STD-001 turned every
/// unclassed button into a classed one, which makes this the remaining way a view can claim
/// compliance and still not be styled.
///
/// Source is scanned as text for the same reason as
/// <see cref="Resources.LocalizationCoverageTest"/>: this project deliberately does not reference
/// <c>GUIClient</c>, so no Avalonia is pulled into a headless run.
/// </summary>
public class StyleClassReferenceTest
{
    /// <summary>
    /// Class references that were already dangling when this guard was written, each with a
    /// reason. Same shape as <see cref="Resources.LocalizationCoverageTest"/>'s pre-existing list
    /// and <c>ControllerAuthorizationInventoryTest</c>'s justified-anonymous list: the guard is
    /// live for every new class reference without this change also redesigning four unrelated
    /// screens.
    ///
    /// These are **defects, not exemptions** — each one renders an unstyled control today. Nothing
    /// may be added to this list; entries come off it as the screens are fixed.
    ///
    /// The list is now empty: <c>EditTitle</c> (EditMgmtReview, EditMitigationWindow,
    /// RiskGovernanceWindow) and <c>subHeader</c> (VulnerabilityImportWindow) were fixed by
    /// adopting the documented <c>TextBlock.header</c> band and <c>TextBlock.header3</c>
    /// respectively, rather than by inventing new classes.
    /// </summary>
    private static readonly Dictionary<string, string> DanglingWhenWritten = new(StringComparer.Ordinal);

    private static readonly Regex ClassAttribute = new(@"Classes\s*=\s*""(?<classes>[^""{}]*)""",
        RegexOptions.Compiled);

    /// <summary>Captures every <c>.class</c> segment of a selector, including compound ones.</summary>
    private static readonly Regex SelectorClass = new(@"\.(?<name>[A-Za-z0-9_\-]+)",
        RegexOptions.Compiled);

    private static readonly Regex SelectorAttribute = new(@"Selector\s*=\s*""(?<selector>[^""]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryClassReferencedByAView_IsDefinedBySomeStyle()
    {
        var global = ClassesDefinedIn(StyleFiles().Select(File.ReadAllText));
        var dangling = new List<string>();

        foreach (var view in ViewFiles())
        {
            var text = File.ReadAllText(view);

            // A view may declare its own <UserControl.Styles>; those count as defined too.
            var known = new HashSet<string>(global, StringComparer.Ordinal);
            known.UnionWith(ClassesDefinedIn([text]));

            foreach (Match match in ClassAttribute.Matches(text))
            {
                foreach (var name in match.Groups["classes"].Value
                             .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (known.Contains(name) || DanglingWhenWritten.ContainsKey(name))
                        continue;

                    dangling.Add($"{Relative(view)} references Classes=\"{name}\", which no style defines");
                }
            }
        }

        Assert.True(dangling.Count == 0,
            "A style class is referenced but never defined, so the control renders unstyled:" +
            Environment.NewLine + string.Join(Environment.NewLine, dangling.Distinct()));
    }

    /// <summary>
    /// The allowlist above is a record of a known state. If one of its entries is no longer
    /// referenced anywhere, it was fixed and must be deleted rather than left to rot.
    /// </summary>
    [Fact]
    public void EveryDanglingClassOnTheAllowlist_IsStillReferenced()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var text in ViewFiles().Select(File.ReadAllText))
        {
            foreach (Match match in ClassAttribute.Matches(text))
            {
                referenced.UnionWith(match.Groups["classes"].Value
                    .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
        }

        var stale = DanglingWhenWritten.Keys.Where(k => !referenced.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "These classes are on the dangling allowlist but no view references them any more — " +
            "remove them from the list: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryDanglingClassOnTheAllowlist_IsStillUndefined()
    {
        var global = ClassesDefinedIn(StyleFiles().Select(File.ReadAllText));
        var nowDefined = DanglingWhenWritten.Keys.Where(global.Contains).ToList();

        Assert.True(nowDefined.Count == 0,
            "These classes now have a style, so they are no longer defects — remove them from the " +
            "dangling allowlist: " + string.Join(", ", nowDefined));
    }

    [Fact]
    public void TheViewAndStyleTreesAreActuallyBeingScanned()
    {
        Assert.True(ViewFiles().Count > 50);
        Assert.True(StyleFiles().Count >= 3);
        // The canonical button taxonomy has to be found, or the scan is looking in the wrong place.
        Assert.Contains("dialog1", ClassesDefinedIn(StyleFiles().Select(File.ReadAllText)));
    }

    private static HashSet<string> ClassesDefinedIn(IEnumerable<string> documents)
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            foreach (Match selector in SelectorAttribute.Matches(document))
            {
                foreach (Match name in SelectorClass.Matches(selector.Groups["selector"].Value))
                    defined.Add(name.Groups["name"].Value);
            }
        }

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
