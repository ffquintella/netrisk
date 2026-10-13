using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GUIClient.Tools;
using Xunit;

namespace GUIClient.Tests.Resources;

/// <summary>
/// Semi.Avalonia's built-in control strings come from the client's resx, never from the theme's
/// default zh-CN dictionary. Regression for every DatePicker in the client rendering 月 日 年 as its
/// day/month/year placeholders.
/// </summary>
public class ThemeStringsTest
{
    private static readonly string[] ResourceFiles =
    [
        "Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx"
    ];

    private static readonly Regex Cjk = new(@"[぀-ヿ㐀-䶿一-鿿가-힯]");

    private static string ClientDirectory([CallerFilePath] string thisFile = "")
    {
        var directory = new FileInfo(thisFile).Directory!;
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "GUIClient");
    }

    private static Dictionary<string, string> Values(string file) =>
        XDocument.Load(Path.Combine(ClientDirectory(), "Resources", file)).Root!
            .Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")?.Value ?? "",
                StringComparer.Ordinal);

    public static TheoryData<string> Files() => new(ResourceFiles);

    [Theory]
    [MemberData(nameof(Files))]
    public void EverySemiStringHasANonChineseTranslationInEveryCulture(string file)
    {
        var values = Values(file);

        var problems = ThemeStrings.SemiKeys
            .Select(pair => pair.Value)
            .Where(resxKey => !values.TryGetValue(resxKey, out var text)
                              || string.IsNullOrWhiteSpace(text) || Cjk.IsMatch(text))
            .ToList();

        Assert.True(problems.Count == 0,
            $"{file}: missing, empty or CJK text for {string.Join(", ", problems)}");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TheFileExistsPromptKeepsItsFormatPlaceholder(string file) =>
        Assert.Contains("{0}", Values(file)[ThemeStrings.SemiKeys["STRING_CHOOSER_PROMPT_FILE_ALREADY_EXISTS"]]);

    [Fact]
    public void TheDatePickerPlaceholdersAreCovered()
    {
        Assert.Contains("STRING_DATEPICKER_DAY_TEXT", ThemeStrings.SemiKeys.Keys);
        Assert.Contains("STRING_DATEPICKER_MONTH_TEXT", ThemeStrings.SemiKeys.Keys);
        Assert.Contains("STRING_DATEPICKER_YEAR_TEXT", ThemeStrings.SemiKeys.Keys);
    }

    [Fact]
    public void ResolveReturnsLocalizedTextUnderSemiKeys()
    {
        var pt = Values("Localization.pt-BR.resx");

        var resolved = ThemeStrings.Resolve(key => pt.GetValueOrDefault(key)).ToDictionary();

        Assert.Equal("dia", resolved["STRING_DATEPICKER_DAY_TEXT"]);
        Assert.Equal("mês", resolved["STRING_DATEPICKER_MONTH_TEXT"]);
        Assert.Equal("ano", resolved["STRING_DATEPICKER_YEAR_TEXT"]);
        Assert.Equal(ThemeStrings.SemiKeys.Count, resolved.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ThemeDatePickerDay")] // IStringLocalizer echoes the key name when it finds nothing
    public void ResolveSkipsAnUnresolvedKeySoTheThemeFallbackShows(string? localized)
    {
        var resolved = ThemeStrings.Resolve(key => key == "ThemeDatePickerDay" ? localized : "x").ToDictionary();

        Assert.DoesNotContain("STRING_DATEPICKER_DAY_TEXT", resolved.Keys);
        Assert.Equal(ThemeStrings.SemiKeys.Count - 1, resolved.Count);
    }

    [Fact]
    public void ResolveRejectsANullLocalizer() =>
        Assert.Throws<ArgumentNullException>(() => ThemeStrings.Resolve(null!).ToList());

    /// <summary>
    /// The application must also pin the theme's own Locale: unset, Semi loads zh-CN for any key the
    /// map above does not cover yet, and the app must apply the map at all.
    /// </summary>
    [Fact]
    public void TheApplicationPinsTheThemeLocaleAndAppliesTheMap()
    {
        var axaml = File.ReadAllText(Path.Combine(ClientDirectory(), "App.axaml"));
        var code = File.ReadAllText(Path.Combine(ClientDirectory(), "App.axaml.cs"));

        Assert.Matches(@"<semi:SemiTheme\s+Locale=""en-US""", axaml);
        Assert.Contains("ThemeStrings.Resolve", code);
    }
}
