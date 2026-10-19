using System;
using System.Collections.Generic;
using DAL.Enums;
using Material.Icons;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The shape that goes with each colour (S40 §10.7; ui-standard §2.6): a band is read by its glyph and label, never by its
/// fill alone. Glyphs follow the band's <em>position</em> among the profile's bands; the fill follows its ramp step
/// (<see cref="ChartPaletteMap.BandStep"/>).
/// </summary>
public static class BandGlyph
{
    private static readonly IReadOnlyDictionary<int, MaterialIconKind[]> ByCount = new Dictionary<int, MaterialIconKind[]>
    {
        [2] = [MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOctagonOutline],
        [3] = [MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOutline, MaterialIconKind.AlertOctagonOutline],
        [4] =
        [
            MaterialIconKind.CheckCircleOutline, MaterialIconKind.AlertOutline, MaterialIconKind.AlertRhombusOutline,
            MaterialIconKind.AlertOctagonOutline
        ],
        [5] =
        [
            MaterialIconKind.CheckCircleOutline, MaterialIconKind.InformationOutline, MaterialIconKind.AlertOutline,
            MaterialIconKind.AlertRhombusOutline, MaterialIconKind.AlertOctagonOutline
        ]
    };

    /// <summary>An indicative value: not classified, so no band glyph (§5.2.3).</summary>
    public const MaterialIconKind Indicative = MaterialIconKind.HelpCircleOutline;

    /// <summary>No category is available in the node (§5.2.3).</summary>
    public const MaterialIconKind NoData = MaterialIconKind.DatabaseOffOutline;

    /// <summary>The glyph of the band at zero-based <paramref name="position"/> among <paramref name="count"/>.</summary>
    public static MaterialIconKind ForBand(int position, int count)
    {
        if (!ChartPaletteMap.IsValidBand(position, count))
            throw new ArgumentOutOfRangeException(nameof(position), position, "The band is outside the profile's 2 to 5 bands.");
        return ByCount[count][position];
    }

    /// <summary>The headline's glyph by display state; null for a classified value whose band is unknown.</summary>
    public static MaterialIconKind? ForHeadline(RiskIndexDisplayState state, int? bandIndex, int bandCount) => state switch
    {
        RiskIndexDisplayState.NoData => NoData,
        RiskIndexDisplayState.Indicative => Indicative,
        _ when bandIndex is { } index && ChartPaletteMap.IsValidBand(index, bandCount) => ForBand(index, bandCount),
        _ => null
    };

    /// <summary>The 7-day delta's arrow (§10.7): worse ↗, better ↘, stable →.</summary>
    public static MaterialIconKind ForDelta(RiskIndexDeltaDirection direction) => direction switch
    {
        RiskIndexDeltaDirection.Worse => MaterialIconKind.ArrowTopRightThick,
        RiskIndexDeltaDirection.Better => MaterialIconKind.ArrowBottomRightThick,
        _ => MaterialIconKind.ArrowRightThin
    };
}
