using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The desktop client used to take two controls — <c>Badge</c> and <c>GroupBox</c> — from the
/// <c>libs/Aura.UI</c> submodule, and it took them invisibly: Aura mapped its control namespace onto
/// the default Avalonia xmlns, so the views wrote <c>&lt;Badge&gt;</c> and <c>&lt;GroupBox&gt;</c>
/// with no prefix and a text search for "Aura" over <c>Views/</c> returned nothing. The dependency
/// was reported as unused on that evidence and it was not; only the compiler knew.
///
/// The submodule is gone and both controls now live in <c>AvaloniaExtraControls.Controls</c>, kept
/// reachable without a prefix by the assembly's <c>XmlnsDefinition</c>. Two things can quietly undo
/// that, and neither shows up as an Aura reference:
/// <list type="number">
///   <item>the <c>XmlnsDefinition</c> is dropped, and every bare usage stops resolving;</item>
///   <item>a view binds a property the replacement never grew, which the previous control had.</item>
/// </list>
///
/// Source-text scanning, for the same reason as the rest of this folder: GUIClient.Tests does not
/// reference GUIClient, because that would pull all of Avalonia into a headless run.
/// </summary>
public class VendoredControlOwnershipTests
{
    /// <summary>Nothing in the tracked tree depends on Aura.UI any more — not a project reference,
    /// not the solution, not <c>.gitmodules</c>, not the AppBuilder chain.</summary>
    [Fact]
    public void NoBuildInputStillDependsOnAuraUi()
    {
        var root = RepositoryRoot();

        var offenders = new[]
            {
                Path.Combine("src", "GUIClient", "GUIClient.csproj"),
                Path.Combine("src", "GUIClient", "Program.cs"),
                Path.Combine("src", "netrisk.sln"),
                ".gitmodules"
            }
            .Where(relative => Declarations(Path.Combine(root, relative))
                                   .Contains("Aura.UI", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Comments are stripped first, and the supply-chain docs are not read at all. Those still
        // *narrate* the Aura.UI rewind incident — the Dependabot pull request that proposed moving
        // the pointer ten commits backwards — and that history is worth keeping. A prose mention is
        // history; a declaration in one of these four files is a dependency.
        Assert.True(offenders.Count == 0,
            "These build inputs reference Aura.UI again. The submodule was removed deliberately — " +
            "Badge and GroupBox live in AvaloniaExtraControls/Controls now:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>File contents with comment lines removed, so a narrated incident is not read as a
    /// dependency. Covers the three comment syntaxes in play: <c>#</c> (.gitmodules), <c>//</c>
    /// (C#) and <c>&lt;!-- --&gt;</c> (csproj/sln).</summary>
    private static string Declarations(string path)
    {
        var text = Regex.Replace(File.ReadAllText(path), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

        return string.Join('\n', text.Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#') && !line.TrimStart().StartsWith("//")));
    }

    /// <summary>
    /// The replacements are only reachable from a view because the assembly maps them onto the
    /// default Avalonia xmlns. Without this attribute every bare <c>&lt;Badge&gt;</c> and
    /// <c>&lt;GroupBox&gt;</c> fails to resolve — at XAML compile time, so it is loud, but the
    /// attribute is a one-line file nobody opens and this says why it is there.
    /// </summary>
    [Fact]
    public void ExtraControlsAreMappedOntoTheDefaultAvaloniaXmlns()
    {
        var assemblyInfo = Path.Combine(ExtraControlsSourceRoot(), "Properties", "AssemblyInfo.cs");

        Assert.True(File.Exists(assemblyInfo),
            $"Expected the XmlnsDefinition that makes GroupBox and Badge resolve without a prefix at {assemblyInfo}.");

        Assert.Matches(
            @"XmlnsDefinition\(\s*""https://github\.com/avaloniaui""\s*,\s*""AvaloniaExtraControls\.Controls""\s*\)",
            File.ReadAllText(assemblyInfo));
    }

    /// <summary>
    /// Every property a view sets on a <c>Badge</c> or a <c>GroupBox</c> exists on the replacement.
    ///
    /// This is the half of the port the XAML compiler covers, so the value here is the other
    /// direction: it fails when a *new* usage reaches for something Aura had and the replacement
    /// does not, naming the property instead of leaving a resolve error to be read.
    /// </summary>
    [Theory]
    [InlineData("Badge")]
    [InlineData("GroupBox")]
    public void EveryPropertyViewsSetOnAReplacedControlExists(string control)
    {
        var declared = DeclaredMembers(control);
        var used = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var view in Directory.EnumerateFiles(Path.Combine(GuiClientSourceRoot(), "Views"),
                                                      "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match element in Regex.Matches(File.ReadAllText(view),
                                                    $@"<{control}\b(?<attributes>[^>]*)>", RegexOptions.Singleline))
            {
                foreach (Match attribute in Regex.Matches(element.Groups["attributes"].Value,
                                                          @"(?<![\w.])(?<name>[A-Z]\w*)\s*="))
                {
                    used.Add(attribute.Groups["name"].Value);
                }
            }
        }

        Assert.NotEmpty(used);

        var missing = used.Where(property => !declared.Contains(property)).ToList();

        Assert.True(missing.Count == 0,
            $"Views set these properties on {control}, which AvaloniaExtraControls.Controls.{control} " +
            $"does not define:\n  {string.Join("\n  ", missing)}");
    }

    /// <summary>
    /// Properties the replacement control defines, plus the ones it inherits that views legitimately
    /// use. Inheritance is listed rather than reflected because this project cannot load Avalonia.
    /// </summary>
    private static HashSet<string> DeclaredMembers(string control)
    {
        var source = File.ReadAllText(Path.Combine(ExtraControlsSourceRoot(), "Controls", $"{control}.cs"));

        var declared = Regex.Matches(source, @"AvaloniaProperty\.Register(?:Direct)?<[^>]+>\(\s*nameof\((?<name>\w+)\)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // From ContentControl / HeaderedContentControl / TemplatedControl / Control / StyledElement.
        foreach (var inherited in new[]
                 {
                     "Content", "ContentTemplate", "Header", "HeaderTemplate", "Classes", "Name",
                     "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius",
                     "FontSize", "FontWeight", "Padding", "Margin", "Width", "Height", "MinWidth",
                     "MinHeight", "MaxWidth", "MaxHeight", "IsVisible", "IsEnabled", "Opacity",
                     "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment",
                     "VerticalContentAlignment", "ToolTip.Tip", "Grid.Row", "Grid.Column",
                     "Grid.RowSpan", "Grid.ColumnSpan", "DockPanel.Dock", "ZIndex"
                 })
        {
            declared.Add(inherited);
        }

        // Attached properties are written Grid.Row="…"; the attribute scan above drops the owner, so
        // the bare names have to be present too.
        foreach (var attached in new[] { "Row", "Column", "RowSpan", "ColumnSpan", "Dock", "Tip" })
            declared.Add(attached);

        return declared;
    }

    private static string ExtraControlsSourceRoot() =>
        Path.Combine(RepositoryRoot(), "src", "AvaloniaExtraControls");

    private static string GuiClientSourceRoot() =>
        Path.Combine(RepositoryRoot(), "src", "GUIClient");

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
