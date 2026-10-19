using System;
using System.Collections.Generic;
using System.Globalization;
using DAL.Enums;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// Chart roles → theme token keys (S40 §10.4 "Paleta", T257). Pure: <c>LintUi</c> reads no C#, so a chart colour is a
/// token key named here, resolved by <c>ChartPalette</c>, and never an <c>SKColor</c> literal. A band's chart token and
/// its pill class come from the same <see cref="BandStep"/>, so one band can never have two colours (§5.2.2, §10.6).
/// </summary>
public static class ChartPaletteMap
{
    /// <summary>Fewest and most bands a profile may declare (S39 §12.4; <c>RiskIndexProfileRules</c>).</summary>
    public const int MinBands = 2;

    public const int MaxBands = 5;

    /// <summary>Opacity of a band's area behind the trend (§10.4: the areas are context, the line is the data).</summary>
    public const double BandAreaOpacity = 0.18;

    /// <summary>Opacity of the shading over the days a floor held the headline.</summary>
    public const double FloorShadeOpacity = 0.30;

    /// <summary>Opacity of the interval whisker of an indicative day.</summary>
    public const double IntervalOpacity = 0.55;

    public const string EwmaToken = "NrChartEwma";
    public const string MarkerToken = "NrChartMarker";
    public const string FloorShadeToken = "NrChartFloorShade";
    public const string GridToken = "NrChartGrid";
    public const string AxisToken = "NrChartAxis";

    /// <summary>The ramp steps used by each band count: 2 → 1, 5; 3 → 1, 3, 5; 4 → 1, 3, 4, 5; 5 → 1…5.</summary>
    private static readonly IReadOnlyDictionary<int, int[]> Ramp = new Dictionary<int, int[]>
    {
        [2] = [1, 5],
        [3] = [1, 3, 5],
        [4] = [1, 3, 4, 5],
        [5] = [1, 2, 3, 4, 5]
    };

    /// <summary>The category series in their fixed order (REG, EXP, AME, CTL, GOV, TER → series 1…6).</summary>
    public static IReadOnlyList<RiskIndexCategory> SeriesOrder { get; } =
    [
        RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl, RiskIndexCategory.Gov,
        RiskIndexCategory.Ter
    ];

    /// <summary>Whether <paramref name="position"/> (zero-based, the band's <c>Index</c>) of <paramref name="count"/> bands is mappable.</summary>
    public static bool IsValidBand(int position, int count) =>
        count is >= MinBands and <= MaxBands && position >= 0 && position < count;

    /// <summary>The criticality-ramp step (1…5) of the band at zero-based <paramref name="position"/> among <paramref name="count"/>.</summary>
    public static int BandStep(int position, int count)
    {
        if (count is < MinBands or > MaxBands)
            throw new ArgumentOutOfRangeException(nameof(count), count, "A profile has 2 to 5 bands.");
        if (position < 0 || position >= count)
            throw new ArgumentOutOfRangeException(nameof(position), position, "The band position is outside the profile's bands.");
        return Ramp[count][position];
    }

    /// <summary>The fill token of a band, in charts and pills alike: <c>NrCriticality{step}</c>.</summary>
    public static string BandTokenKey(int position, int count) =>
        "NrCriticality" + BandStep(position, count).ToString(CultureInfo.InvariantCulture);

    /// <summary>The text token drawn on a band's fill: <c>NrCriticality{step}Text</c> (AA contrast, <c>Tokens.axaml</c>).</summary>
    public static string BandTextTokenKey(int position, int count) => BandTokenKey(position, count) + "Text";

    /// <summary>The pill's style class (<c>Border.riskBand.b{step}</c>, §10.6): the ramp step, not the position.</summary>
    public static string BandClass(int position, int count) =>
        "b" + BandStep(position, count).ToString(CultureInfo.InvariantCulture);

    /// <summary>The token of a category's series: <c>NrChartSeries1</c> (REG) … <c>NrChartSeries6</c> (TER).</summary>
    public static string SeriesTokenKey(RiskIndexCategory category)
    {
        var index = -1;
        for (var i = 0; i < SeriesOrder.Count; i++)
            if (SeriesOrder[i] == category) index = i;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(category), category, "The headline is not a category series.");
        return "NrChartSeries" + (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Every token key this map can name, so the token sheet can be checked to define each one.</summary>
    public static IReadOnlyList<string> AllTokenKeys()
    {
        var keys = new List<string>();
        for (var step = 1; step <= MaxBands; step++)
        {
            keys.Add("NrCriticality" + step.ToString(CultureInfo.InvariantCulture));
            keys.Add("NrCriticality" + step.ToString(CultureInfo.InvariantCulture) + "Text");
        }

        foreach (var category in SeriesOrder) keys.Add(SeriesTokenKey(category));
        keys.AddRange([EwmaToken, MarkerToken, FloorShadeToken, GridToken, AxisToken]);
        return keys;
    }
}
