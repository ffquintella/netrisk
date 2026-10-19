using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Material.Icons;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// One line of "Categorias contribuintes" (§5.4): value with its 0–100 meter, band as text and glyph, configured and
/// effective weight, Euler points, what holds the category and its coverage — or, when it has no value, why.
/// </summary>
public sealed record CategoryRow(
    RiskIndexCategory Category, string Code, string Name, bool IsAvailable, bool IsNotApplicable, string ValueText,
    double Fraction, string? BandLabel, MaterialIconKind? BandGlyph, string? BandClass, string WeightText, string PointsText,
    string HeldByText, string? HeldByHint, string CoverageText, string? CoverageHint, string? StateText,
    MaterialIconKind? StateGlyph, IReadOnlyList<string> Seals);

/// <summary>
/// The contributing categories' pure half (S40 §5.4; T253): the rows in the fixed order, the disabled category (ω = 0)
/// left out, the unavailable one kept with "—" and its reason, the not-applicable one dimmed. Formats only — the values,
/// weights, points and holders are the server's.
/// </summary>
public static class CategoryRowFormatter
{
    /// <summary>Weights are shown to two decimals; an effective weight this close to the configured one is not repeated.</summary>
    private const double WeightTolerance = 0.005;

    public static IReadOnlyList<CategoryRow> Rows(IReadOnlyList<RiskIndexCategoryDto> categories,
        IReadOnlyList<RiskIndexBandDto> bands, TextLookup text, CultureInfo culture, CultureInfo ui) =>
        ChartPaletteMap.SeriesOrder
            .Select(c => categories.FirstOrDefault(x => x.Category == c))
            .Where(c => c is not null && c.State != RiskIndexCategoryState.Disabled)
            .Select(c => Row(c!, bands, text, culture, ui))
            .ToList();

    public static CategoryRow Row(RiskIndexCategoryDto c, IReadOnlyList<RiskIndexBandDto> bands, TextLookup text,
        CultureInfo culture, CultureInfo ui)
    {
        var code = HeadlineFormatter.CategoryCode(c.Category);
        var name = text("RiskCategory_" + code + "_Name") ?? code;
        var weight = Weight(c, culture);
        var seals = c.Category == RiskIndexCategory.Reg ? HeadlineFormatter.Seals(c.Seals, text, culture) : [];

        if (c.State == RiskIndexCategoryState.Available && c.Value is { } value)
        {
            var displayed = c.Displayed ?? MonthlySummaryFormatter.Displayed(value);
            var band = BandIndex(c.BandKey) is { } index && ChartPaletteMap.IsValidBand(index, bands.Count)
                ? HeadlineFormatter.BandAt(bands, index)
                : null;
            return new CategoryRow(c.Category, code, name, true, false, displayed.ToString(culture), Math.Clamp(value / 100, 0, 1),
                band is null ? null : HeadlineFormatter.BandLabel(band, ui),
                band is null ? null : BandGlyph.ForBand(band.Index, bands.Count),
                band is null ? null : ChartPaletteMap.BandClass(band.Index, bands.Count),
                weight, c.Points is { } p ? MonthlySummaryFormatter.Displayed(p).ToString(culture) : "—",
                HeldBy(c, text, culture), c.HeldBy?.Object.RequiresPermission is { } permission
                    ? RiskText.Format(text, "RiskCategoryRequiresPermissionFMT", culture, permission)
                    : null,
                c.Coverage is { } cov ? HeadlineFormatter.Percent(cov, culture) + "%" : "—",
                c.PresumptionPoints is { } presumed and > 0
                    ? RiskText.Format(text, "RiskCategoryPresumptionFMT", culture, presumed.ToString("0.0", culture))
                    : null,
                null, null, seals);
        }

        var notApplicable = c.State == RiskIndexCategoryState.NotApplicable;
        var state = notApplicable
            ? RiskText.Get(text, "RiskCategoryState_NotApplicable")
            : c.RequiresRef is { Length: > 0 } requires
                ? RiskText.Format(text, "RiskCategoryState_UnavailableRequiresFMT", culture, requires)
                : c.UnavailableReasonKey is { Length: > 0 } reason && text("RiskCategoryReason_" + reason) is { } why
                    ? RiskText.Format(text, "RiskCategoryState_UnavailableReasonFMT", culture, why)
                    : RiskText.Get(text, "RiskCategoryState_Unavailable");
        return new CategoryRow(c.Category, code, name, false, notApplicable, "—", 0, null, null, null, weight, "—", "", null,
            notApplicable ? "—" : "0%", null, state, notApplicable ? MaterialIconKind.Cancel : MaterialIconKind.CloudOffOutline,
            seals);
    }

    /// <summary>"0,30", or "0,30 → 0,40" when an unavailable category renormalized the others (S39 §4.4).</summary>
    public static string Weight(RiskIndexCategoryDto c, CultureInfo culture)
    {
        var configured = c.Weight.ToString("0.00", culture);
        return Math.Abs(c.EffectiveWeight - c.Weight) > WeightTolerance
            ? configured + " → " + c.EffectiveWeight.ToString("0.00", culture)
            : configured;
    }

    /// <summary>
    /// "Sustentado por" (§5.4, S39 §4.8): the object holding the floor with "(piso)", a tie between N objects, or "média"
    /// when the mean branch sets the value. The object's label is already redacted by the server when it must be (§11.3).
    /// </summary>
    public static string HeldBy(RiskIndexCategoryDto c, TextLookup text, CultureInfo culture) => c.HeldBy switch
    {
        null => RiskText.Get(text, "RiskCategoryHeldByMean"),
        { IsTie: true } h => RiskText.Format(text, "RiskCategoryHeldByTieFMT", culture, h.TieCount),
        { } h => RiskText.Format(text, "RiskCategoryHeldByFloorFMT", culture, ObjectLabelDisplay.Name(h.Object, text, culture))
    };

    /// <summary>The band position of a server band key (<c>b0</c>, <c>b1</c>…), or null.</summary>
    public static int? BandIndex(string? key) =>
        key is { Length: > 1 } k && k[0] == 'b' && int.TryParse(k.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var i)
            ? i
            : null;

    /// <summary>The note under the table while TER has no weight (§5.4: "TER entra a partir do M48").</summary>
    public static bool ShowsTerNote(IReadOnlyList<RiskIndexCategoryDto> categories) =>
        categories.All(c => c.Category != RiskIndexCategory.Ter || c.State == RiskIndexCategoryState.Disabled);
}

/// <summary>Meter and segment widths for the <c>Border</c> bars (§10.4: those bars are not LiveCharts).</summary>
public static class MeterGeometry
{
    /// <summary>The fill of a 0–1 fraction in a bar <paramref name="width"/> wide, never negative nor wider than the bar.</summary>
    public static double Fill(double fraction, double width) =>
        double.IsNaN(fraction) ? 0 : Math.Round(Math.Clamp(fraction, 0, 1) * width, 1);

    /// <summary>
    /// Proportional widths of a segmented bar; a non-empty segment is at least <paramref name="minimum"/> wide, so a
    /// small count stays visible, and the widths still add up to the bar.
    /// </summary>
    public static IReadOnlyList<double> Segments(IReadOnlyList<int> counts, double width, double minimum = 2)
    {
        var total = counts.Where(c => c > 0).Sum();
        if (total <= 0) return counts.Select(_ => 0.0).ToList();
        var raw = counts.Select(c => c > 0 ? Math.Max(minimum, width * c / total) : 0).ToList();
        var scale = raw.Sum() > 0 ? width / raw.Sum() : 0;
        return raw.Select(w => Math.Round(w * scale, 1)).ToList();
    }
}
