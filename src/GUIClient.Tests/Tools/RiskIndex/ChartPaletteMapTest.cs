using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T257 (S40 §10.4, §10.6, §12.4): chart colours are theme tokens named by <see cref="ChartPaletteMap"/>, since
/// <c>LintUi</c> reads no C#. Every band count maps to the ramp the spec fixes, the pill class and the chart token of a band
/// carry the same step, every role names a token <c>Tokens.axaml</c> defines, and no index code outside the adapter builds
/// an <c>SKColor</c>.
/// </summary>
[TestSubject(typeof(ChartPaletteMap))]
public class ChartPaletteMapTest
{
    [Theory]
    [InlineData(2, new[] { 1, 5 })]
    [InlineData(3, new[] { 1, 3, 5 })]
    [InlineData(4, new[] { 1, 3, 4, 5 })]
    [InlineData(5, new[] { 1, 2, 3, 4, 5 })]
    public void TestBandStepFollowsTheRampOfEachBandCount(int count, int[] steps)
    {
        Assert.Equal(steps, Enumerable.Range(0, count).Select(position => ChartPaletteMap.BandStep(position, count)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 6)]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    public void TestBandStepRefusesWhatNoProfileCanDeclare(int position, int count)
    {
        Assert.False(ChartPaletteMap.IsValidBand(position, count));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartPaletteMap.BandStep(position, count));
    }

    [Fact]
    public void TestThePillClassAndTheChartTokenOfABandCarryTheSameStep()
    {
        for (var count = ChartPaletteMap.MinBands; count <= ChartPaletteMap.MaxBands; count++)
        for (var position = 0; position < count; position++)
        {
            var step = ChartPaletteMap.BandStep(position, count);
            Assert.Equal("b" + step, ChartPaletteMap.BandClass(position, count));
            Assert.Equal("NrCriticality" + step, ChartPaletteMap.BandTokenKey(position, count));
            Assert.Equal("NrCriticality" + step + "Text", ChartPaletteMap.BandTextTokenKey(position, count));
        }

        // Three bands are b1, b3 and b5 — never b1, b2, b3 (§5.2.2).
        Assert.Equal(["b1", "b3", "b5"], Enumerable.Range(0, 3).Select(p => ChartPaletteMap.BandClass(p, 3)));
    }

    [Fact]
    public void TestEachPillClassIsPaintedWithItsOwnStepsToken()
    {
        var sheet = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "Styles", "WindowStyles.axaml"));

        for (var step = 1; step <= 5; step++)
        {
            var fill = Regex.Match(sheet, $@"<Style Selector=""Border\.riskBand\.b{step}"">(?<body>.*?)</Style>", RegexOptions.Singleline);
            Assert.True(fill.Success, $"Border.riskBand.b{step} has no style.");
            Assert.Contains($"{{DynamicResource NrCriticality{step}}}", fill.Groups["body"].Value);

            var text = Regex.Match(sheet, $@"<Style Selector=""Border\.riskBand\.b{step} TextBlock\.riskBandText"">(?<body>.*?)</Style>",
                RegexOptions.Singleline);
            Assert.True(text.Success, $"The text of Border.riskBand.b{step} has no style.");
            Assert.Contains($"{{DynamicResource NrCriticality{step}Text}}", text.Groups["body"].Value);
        }
    }

    [Fact]
    public void TestTheCategorySeriesFollowTheFixedOrder()
    {
        Assert.Equal(["NrChartSeries1", "NrChartSeries2", "NrChartSeries3", "NrChartSeries4", "NrChartSeries5", "NrChartSeries6"],
            new[]
            {
                RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl, RiskIndexCategory.Gov,
                RiskIndexCategory.Ter
            }.Select(ChartPaletteMap.SeriesTokenKey));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartPaletteMap.SeriesTokenKey(RiskIndexCategory.Icr));
    }

    [Fact]
    public void TestTheOtherRolesNameTheirTokensAndTheOpacitiesAreFractions()
    {
        Assert.Equal(("NrChartEwma", "NrChartMarker", "NrChartFloorShade", "NrChartGrid", "NrChartAxis"),
            (ChartPaletteMap.EwmaToken, ChartPaletteMap.MarkerToken, ChartPaletteMap.FloorShadeToken, ChartPaletteMap.GridToken,
                ChartPaletteMap.AxisToken));
        Assert.InRange(ChartPaletteMap.BandAreaOpacity, 0.01, 0.99);
        Assert.InRange(ChartPaletteMap.FloorShadeOpacity, 0.01, 0.99);
        Assert.InRange(ChartPaletteMap.IntervalOpacity, 0.01, 0.99);
    }

    [Fact]
    public void TestEveryTokenThePaletteNamesIsDefinedByTheTokenSheet()
    {
        var sheet = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "Styles", "Tokens.axaml"));
        var defined = Regex.Matches(sheet, @"x:Key=""(?<key>Nr\w+)""").Select(m => m.Groups["key"].Value).ToHashSet();

        var missing = ChartPaletteMap.AllTokenKeys().Where(key => !defined.Contains(key)).ToList();

        Assert.True(missing.Count == 0, "ChartPaletteMap names tokens Tokens.axaml does not define: " + string.Join(", ", missing));
        Assert.Equal(21, ChartPaletteMap.AllTokenKeys().Distinct().Count());
    }

    [Fact]
    public void TestTheNewChartTokensAliasTheBaseThemeInsteadOfAddingAHex()
    {
        var sheet = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "Styles", "Tokens.axaml"));

        foreach (Match token in Regex.Matches(sheet, @"<SolidColorBrush x:Key=""(?<key>NrChart\w+)"" Color=""(?<color>[^""]+)"""))
            Assert.StartsWith("{DynamicResource Semi", token.Groups["color"].Value);
    }

    [Fact]
    public void TestNoIndexCodeOutsideTheAdapterBuildsASkiaColour()
    {
        var client = RiskIndexTestText.ClientDirectory();
        var folders = new[] { "ViewModels/RiskOverview", "ViewModels/Admin/RiskIndex", "Tools/RiskIndex" }
            .Select(f => Path.Combine(client, f)).Where(Directory.Exists).ToList();
        Assert.NotEmpty(folders);

        var offenders = folders.SelectMany(f => Directory.EnumerateFiles(f, "*.cs", SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(file) != "ChartPalette.cs")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"SKColor\(|SKColors\.|SKColor\.Parse"))
            .Select(Path.GetFileName).ToList();

        Assert.True(offenders.Count == 0, "Colour literals belong in Tokens.axaml: " + string.Join(", ", offenders));
    }
}
