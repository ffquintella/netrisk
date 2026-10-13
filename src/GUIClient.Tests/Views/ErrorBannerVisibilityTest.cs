using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// Every error banner — a <c>TextBlock.warning</c> or <c>TextBlock.warning2</c> whose text is bound to an error
/// property — is hidden when there is no error.
///
/// Both classes paint a red <c>Background</c> (<c>Styles/WindowStyles.axaml</c>), so a banner bound to an empty
/// <c>ErrorMessage</c> with no <c>IsVisible</c> still draws an empty red bar. Ten Track 9 views shipped that way and
/// showed the bar on every screen whether or not anything had failed.
///
/// Why here and not as a <c>LintUi</c> rule: the UI linter enforces the token and copy rules of
/// <c>docs/ui-standard.md</c>, and a new R-rule would change that standard; this is a binding-semantics check of
/// the same kind as <see cref="StyleClassReferenceTest"/>, which this folder already holds. Source is scanned as
/// text because this project deliberately does not reference <c>GUIClient</c> (no Avalonia in a headless run);
/// compiled bindings make the GUIClient build itself prove the <c>IsVisible</c> target exists.
/// </summary>
public class ErrorBannerVisibilityTest
{
    private static readonly HashSet<string> BannerClasses = new(StringComparer.Ordinal) { "warning", "warning2" };

    /// <summary>The binding path of <c>{Binding X}</c>, <c>{Binding Path=X, …}</c> or <c>{CompiledBinding X}</c>.</summary>
    private static readonly Regex BindingPath = new(
        @"^\{\s*(?:Compiled)?Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z0-9_.\[\]]+)",
        RegexOptions.Compiled);

    /// <summary>An error property: <c>ErrorMessage</c>, <c>Error</c>, <c>LoadError</c>, <c>InputErrorText</c>, …</summary>
    private static readonly Regex ErrorProperty = new(@"(?:Error|ErrorMessage|ErrorText)$", RegexOptions.Compiled);

    [Fact]
    public void EveryWarningBannerBoundToAnErrorIsHiddenWhenThereIsNoError()
    {
        var unguarded = ErrorBanners()
            .Where(b => !b.HasVisibilityBinding)
            .Select(b => $"{b.View}:{b.Line} TextBlock.{b.Class} Text=\"{b.Text}\" has no IsVisible binding")
            .ToList();

        Assert.True(unguarded.Count == 0,
            "An error banner with no IsVisible binding draws an empty red bar when there is no error — bind " +
            "IsVisible to the view model's HasError (raised whenever the message changes):" +
            Environment.NewLine + string.Join(Environment.NewLine, unguarded));
    }

    [Fact]
    public void TheScanActuallyFindsTheErrorBanners()
    {
        var banners = ErrorBanners();

        // The ten Track 9 banners this guard was written for, plus the dashboard, entity form, technique editor
        // and the monitoring screens' load errors. A rename of the classes or of the binding syntax that made the
        // scan match nothing would otherwise pass the test above vacuously.
        Assert.True(banners.Count >= 15, $"expected at least 15 error banners, found {banners.Count}");
        Assert.Contains(banners, b => b.View == "Views/Track9/ThirdPartyRegisterView.axaml" && b.Text.Contains("ErrorMessage"));
        Assert.Contains(banners, b => b.View == "Views/MasterDashboardView.axaml" && b.HasVisibilityBinding);
    }

    [Fact]
    public void TheMatcherRecognisesAnUnguardedBanner()
    {
        const string view = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <StackPanel>
                <TextBlock Classes="warning" Text="{Binding ErrorMessage}" TextWrapping="Wrap"/>
                <TextBlock Classes="header_detail warning2" Text="{Binding Path=LoadError}" IsVisible="{Binding HasLoadError}"/>
                <TextBlock Classes="warning" Text="{Binding StrNotAccepted}"/>
                <TextBlock Classes="header" Text="{Binding ErrorMessage}"/>
              </StackPanel>
            </UserControl>
            """;

        var banners = BannersIn("inline.axaml", XDocument.Parse(view, LoadOptions.SetLineInfo));

        Assert.Equal(2, banners.Count);
        Assert.False(banners[0].HasVisibilityBinding);
        Assert.Equal("warning", banners[0].Class);
        Assert.True(banners[1].HasVisibilityBinding);
        Assert.Equal("warning2", banners[1].Class);
    }

    private sealed record Banner(string View, int Line, string Class, string Text, bool HasVisibilityBinding);

    private static List<Banner> ErrorBanners() =>
        Directory.GetFiles(Path.Combine(GuiClientRoot(), "Views"), "*.axaml", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .SelectMany(file => BannersIn(Relative(file), XDocument.Load(file, LoadOptions.SetLineInfo)))
            .ToList();

    private static List<Banner> BannersIn(string view, XDocument document)
    {
        var banners = new List<Banner>();

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "TextBlock"))
        {
            var bannerClass = ((string?)element.Attribute("Classes") ?? "")
                .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(BannerClasses.Contains);
            if (bannerClass is null)
                continue;

            var text = (string?)element.Attribute("Text") ?? "";
            var path = BindingPath.Match(text);
            if (!path.Success || !ErrorProperty.IsMatch(path.Groups["path"].Value))
                continue;

            var isVisible = ((string?)element.Attribute("IsVisible"))?.TrimStart() ?? "";
            var line = element is System.Xml.IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

            banners.Add(new Banner(view, line, bannerClass, text, isVisible.StartsWith('{')));
        }

        return banners;
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(GuiClientRoot(), path).Replace('\\', '/');

    /// <summary>Resolves <c>src/GUIClient</c> from this test file's own location.</summary>
    private static string GuiClientRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "GUIClient"));
}
