using System;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Material.Icons;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T251 (S40 §10.7, §12.4): a band is read by shape before colour. The glyph follows the band's position among the
/// profile's bands; the fill follows its ramp step — and the pill's class and the chart's token come from the same step.
/// </summary>
[TestSubject(typeof(BandGlyph))]
public class BandGlyphTest
{
    [Fact]
    public void TestEachBandCountHasTheSpecifiedGlyphsInOrder()
    {
        Assert.Equal([MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOctagonOutline], Glyphs(2));
        Assert.Equal([MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOutline, MaterialIconKind.AlertOctagonOutline],
            Glyphs(3));
        Assert.Equal(
        [
            MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOutline, MaterialIconKind.AlertRhombusOutline,
            MaterialIconKind.AlertOctagonOutline
        ], Glyphs(4));
        Assert.Equal(
        [
            MaterialIconKind.CheckCircleOutline, MaterialIconKind.InformationOutline, MaterialIconKind.AlertOutline,
            MaterialIconKind.AlertRhombusOutline, MaterialIconKind.AlertOctagonOutline
        ], Glyphs(5));

        static MaterialIconKind[] Glyphs(int count) =>
            Enumerable.Range(0, count).Select(p => BandGlyph.ForBand(p, count)).ToArray();
    }

    [Fact]
    public void TestEveryBandOfACountHasADistinctShape()
    {
        for (var count = 2; count <= 5; count++)
            Assert.Equal(count, Enumerable.Range(0, count).Select(p => BandGlyph.ForBand(p, count)).Distinct().Count());
    }

    [Fact]
    public void TestABandOutsideTheProfileHasNoGlyph()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BandGlyph.ForBand(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BandGlyph.ForBand(0, 6));
    }

    [Fact]
    public void TestTheHeadlineGlyphFollowsTheDisplayState()
    {
        Assert.Equal(MaterialIconKind.DatabaseOffOutline, BandGlyph.ForHeadline(RiskIndexDisplayState.NoData, 2, 3));
        Assert.Equal(MaterialIconKind.HelpCircleOutline, BandGlyph.ForHeadline(RiskIndexDisplayState.Indicative, 2, 3));
        Assert.Equal(MaterialIconKind.AlertOutline, BandGlyph.ForHeadline(RiskIndexDisplayState.Point, 1, 3));
        Assert.Equal(MaterialIconKind.AlertOctagonOutline, BandGlyph.ForHeadline(RiskIndexDisplayState.PointWithInterval, 2, 3));
        Assert.Null(BandGlyph.ForHeadline(RiskIndexDisplayState.Point, null, 3));
        Assert.Null(BandGlyph.ForHeadline(RiskIndexDisplayState.Point, 4, 3));
    }

    [Fact]
    public void TestTheDeltaArrowsAreTheSpecifiedShapes()
    {
        Assert.Equal(MaterialIconKind.ArrowTopRightThick, BandGlyph.ForDelta(RiskIndexDeltaDirection.Worse));
        Assert.Equal(MaterialIconKind.ArrowBottomRightThick, BandGlyph.ForDelta(RiskIndexDeltaDirection.Better));
        Assert.Equal(MaterialIconKind.ArrowRightThin, BandGlyph.ForDelta(RiskIndexDeltaDirection.Stable));
    }

    [Fact]
    public void TestThePillAndTheChartOfTheSameBandUseOneRampStepWhileTheGlyphUsesThePosition()
    {
        // Four bands: positions 0..3, ramp steps 1, 3, 4, 5. The second band is AlertOutline, painted step 3.
        Assert.Equal(MaterialIconKind.AlertOutline, BandGlyph.ForBand(1, 4));
        Assert.Equal("b3", ChartPaletteMap.BandClass(1, 4));
        Assert.Equal("NrCriticality3", ChartPaletteMap.BandTokenKey(1, 4));
    }
}
