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

    /// <summary>
    /// A button class that fixes its own Width or Height also opts out of the base theme's button
    /// metrics.
    ///
    /// Semi's Button control theme sets <c>Padding="12 0"</c> and
    /// <c>MinHeight="{SemiHeightControlDefault}"</c>. On a labelled button that is correct. On a
    /// 25x25 icon button the padding leaves roughly one pixel of content box, and the MinHeight
    /// quietly wins over the declared Height — which is how the risk status filter shipped as four
    /// empty purple circles for the length of one prototype. Nothing about it fails to compile, and
    /// nothing about it fails a colour check, because no colour is wrong.
    ///
    /// So: declare a size, declare the padding too. The reset selector near the top of
    /// WindowStyles.axaml is where classes opt out.
    /// </summary>
    [Fact]
    public void EveryFixedSizeButtonClassOptsOutOfTheThemeMetrics()
    {
        var sheet = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Styles", "WindowStyles.axaml"));

        // The one style whose selector lists several classes and zeroes Padding/MinHeight for them.
        var resetSelector = Regex.Matches(sheet, @"<Style Selector=""(?<selector>Button[^""]*)"">(?<body>.*?)</Style>", RegexOptions.Singleline)
            .Where(style => style.Groups["body"].Value.Contains(@"Property=""MinHeight"" Value=""0"""))
            .Select(style => style.Groups["selector"].Value)
            .FirstOrDefault();

        Assert.NotNull(resetSelector);

        var exempt = Regex.Matches(resetSelector, @"Button\.(?<class>[\w-]+)")
            .Select(match => match.Groups["class"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unguarded = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match style in Regex.Matches(sheet, @"<Style Selector=""Button\.(?<class>[\w-]+)"">(?<body>.*?)</Style>", RegexOptions.Singleline))
        {
            var body = style.Groups["body"].Value;
            var cssClass = style.Groups["class"].Value;

            var fixesSize = body.Contains(@"Property=""Width""") || body.Contains(@"Property=""Height""");
            var setsOwnPadding = body.Contains(@"Property=""Padding""");

            if (fixesSize && !setsOwnPadding && !exempt.Contains(cssClass))
                unguarded.Add(cssClass);
        }

        Assert.True(unguarded.Count == 0,
            "These button classes fix their own size but inherit the base theme's padding and " +
            "MinHeight, which will crop or resize their content. Add them to the reset selector in " +
            "WindowStyles.axaml, or give them an explicit Padding:\n  " + string.Join("\n  ", unguarded));
    }

    /// <summary>
    /// The converse of the test above, and the half that was missing.
    ///
    /// The reset selector zeroes <c>Padding</c>, <c>MinWidth</c> and <c>MinHeight</c> for the
    /// classes it lists. That is only safe for a class that then says how wide it is:
    /// <c>Button.toolbar</c> declared <c>Height="30"</c> and nothing else, so its width fell to the
    /// glyph's own — the vulnerability toolbar shipped as a row of cramped rectangles, and the
    /// collapsed details pane leaked its text into the pixels the narrower button no longer used.
    ///
    /// A class satisfies this by declaring a Width/MinWidth, or by taking its own Padding back
    /// (which is what a label-carrying class like <c>nav-base</c> does — it should grow to content).
    /// </summary>
    [Fact]
    public void EveryClassInTheMetricsResetDeclaresItsOwnWidth()
    {
        var sheet = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Styles", "WindowStyles.axaml"));

        var resetSelector = Regex.Matches(sheet, @"<Style Selector=""(?<selector>Button[^""]*)"">(?<body>.*?)</Style>", RegexOptions.Singleline)
            .Where(style => style.Groups["body"].Value.Contains(@"Property=""MinWidth"" Value=""0"""))
            .Select(style => style.Groups["selector"].Value)
            .FirstOrDefault();

        Assert.NotNull(resetSelector);

        var reset = Regex.Matches(resetSelector, @"Button\.(?<class>[\w-]+)")
            .Select(match => match.Groups["class"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(reset);

        var bodies = Regex.Matches(sheet, @"<Style Selector=""Button\.(?<class>[\w-]+)"">(?<body>.*?)</Style>", RegexOptions.Singleline)
            .Where(style => reset.Contains(style.Groups["class"].Value))
            .ToDictionary(style => style.Groups["class"].Value, style => style.Groups["body"].Value, StringComparer.Ordinal);

        var widthless = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var cssClass in reset)
        {
            if (!bodies.TryGetValue(cssClass, out var body))
            {
                widthless.Add($"{cssClass} (listed in the reset selector but has no style of its own)");
                continue;
            }

            var declaresWidth = body.Contains(@"Property=""Width""") || body.Contains(@"Property=""MinWidth""");
            var reclaimsPadding = Regex.IsMatch(body, @"Property=""Padding""\s+Value=""(?!0"")");

            if (!declaresWidth && !reclaimsPadding)
                widthless.Add(cssClass);
        }

        Assert.True(widthless.Count == 0,
            "These classes opt out of the base theme's button metrics — which zeroes their MinWidth " +
            "and Padding — without saying how wide they are, so they collapse to the width of their " +
            "glyph. Give them a Width/MinWidth, or take their Padding back:\n  " +
            string.Join("\n  ", widthless));
    }

    /// <summary>
    /// A <c>CompactInline</c> SplitView pane is still laid out when it is closed, at
    /// <c>CompactPaneLength</c>. Any content sharing that pane with the toggle button therefore gets
    /// whatever width the toggle does not use and renders into it — in the vulnerability register,
    /// the details text came out one character per line down the full height of the window.
    ///
    /// So the content has to be collapsed explicitly, off the same property that drives
    /// <c>IsPaneOpen</c>. Closing the pane is not what hides it.
    /// </summary>
    [Fact]
    public void CompactSplitViewPanesCollapseTheirContentWhenClosed()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(GuiClientSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            foreach (Match splitView in Regex.Matches(
                         text, @"<SplitView\b(?<attributes>[^>]*)>(?<body>.*?)</SplitView>", RegexOptions.Singleline))
            {
                if (!splitView.Groups["attributes"].Value.Contains("CompactPaneLength"))
                    continue;

                var openBinding = Regex.Match(splitView.Groups["attributes"].Value,
                    @"IsPaneOpen=""\{Binding\s+(?<property>[\w.]+)");

                Assert.True(openBinding.Success,
                    $"{Path.GetFileName(file)}: a compact SplitView whose IsPaneOpen is not a simple binding.");

                var pane = Regex.Match(splitView.Groups["body"].Value,
                    @"<SplitView\.Pane>(?<pane>.*?)</SplitView\.Pane>", RegexOptions.Singleline);

                Assert.True(pane.Success, $"{Path.GetFileName(file)}: compact SplitView with no explicit Pane.");

                var expected = $@"IsVisible=""{{Binding {openBinding.Groups["property"].Value}}}""";

                if (!pane.Groups["pane"].Value.Contains(expected, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: pane content is never collapsed (expected {expected} on it)");
            }
        }

        Assert.True(offenders.Count == 0,
            "A compact SplitView pane is laid out at CompactPaneLength while closed, so its content " +
            "renders into the sliver the toggle button leaves over:\n  " + string.Join("\n  ", offenders));
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
