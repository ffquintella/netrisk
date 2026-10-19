using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Material.Icons;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>A resource lookup: the localized text of a key, or null when no resource declares it.</summary>
public delegate string? TextLookup(string key);

/// <summary>The 7-day line (§5.2.2): its text, its style class and, only when an arrow is earned, its glyph.</summary>
public sealed record HeadlineDelta(string Text, string CssClass, MaterialIconKind? Glyph, RiskIndexDeltaDirection? Direction);

/// <summary>The quality line (§5.2.2): the quality seal and the coverage and freshness percentages.</summary>
public sealed record HeadlineQuality(string BandText, string ValueText, string CoverageText, string FreshnessText);

/// <summary>The ignorance interval: on the card, or "in the detail" with the values in the hint (§5.2.2, S39 §8.6).</summary>
public sealed record HeadlineInterval(bool OnCard, string Text, string? Hint);

/// <summary>A chip as the card shows it: the server's key (or <c>category_unavailable</c>) and its sentence.</summary>
public sealed record HeadlineChip(string Key, string Text);

/// <summary>
/// The headline card's wording (S40 §5.2–§5.2.3, §10.8), pure so <c>GUIClient.Tests</c> covers it against the real
/// <c>.resx</c> text. It formats what the server computed — the index is never recomputed on the client (§5.0).
/// </summary>
public static class HeadlineFormatter
{
    /// <summary>Headline chips the card does not list: the floor is the branch line, data quality is the quality strip (T256), and
    /// the EAV closure only explains the partial banner.</summary>
    public static readonly IReadOnlySet<string> NotShownAsChips =
        new HashSet<string>(StringComparer.Ordinal) { "headline_floor", "data_quality", "closure_through_eav" };

    /// <summary>The chip the card derives from the categories: a weighted category with no data in the node (§5.2.3).</summary>
    public const string CategoryUnavailableChip = "category_unavailable";

    /// <summary>The seal keys the server sends today (<c>RiskIndexReadService.SealsAsync</c>), each with a <c>RiskSeal_‹key›MSG</c>.</summary>
    public static readonly IReadOnlyList<string> KnownSealKeys =
        ["scenarios_above_appetite", "scenarios_accepted_above_appetite", "acceptances_expiring", "appetite_not_configured"];

    /// <summary>The chip keys the card shows, each with a <c>RiskChip_‹key›MSG</c>.</summary>
    public static readonly IReadOnlyList<string> KnownChipKeys =
        ["no_critical_process", "unassigned_above_threshold", "partial_visibility", CategoryUnavailableChip];

    // ------------------------------------------------------------------------------------------------------------------
    // Value and band
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>A band's label in the interface's language: pt-BR for Portuguese, otherwise en-US, the neutral default (§10.5).</summary>
    public static string BandLabel(RiskIndexBandDto band, CultureInfo ui) =>
        ui.TwoLetterISOLanguageName == "pt" ? band.LabelPtBr : band.LabelEnUs;

    /// <summary>The band whose <c>Index</c> is <paramref name="index"/>, or null.</summary>
    public static RiskIndexBandDto? BandAt(IReadOnlyList<RiskIndexBandDto> bands, int? index) =>
        index is { } i ? bands.FirstOrDefault(b => b.Index == i) : null;

    /// <summary>The band a displayed integer falls in: the one with the greatest inclusive lower bound not above it.</summary>
    public static RiskIndexBandDto? BandOf(int value, IReadOnlyList<RiskIndexBandDto> bands) =>
        bands.Where(b => b.LowerInclusive <= value).OrderByDescending(b => b.LowerInclusive).FirstOrDefault();

    /// <summary>Whether the card classifies the value: a point value with a known band. Indicative and no-data never do.</summary>
    public static bool ShowsBand(RiskIndexHeadlineDto headline, IReadOnlyList<RiskIndexBandDto> bands) =>
        headline.DisplayState is RiskIndexDisplayState.Point or RiskIndexDisplayState.PointWithInterval &&
        headline.Displayed is not null && headline.BandIndex is { } index && ChartPaletteMap.IsValidBand(index, bands.Count) &&
        BandAt(bands, index) is not null;

    /// <summary>The pill's style class, from the same ramp step the chart token uses (<see cref="ChartPaletteMap.BandClass"/>).</summary>
    public static string? BandClass(RiskIndexHeadlineDto headline, IReadOnlyList<RiskIndexBandDto> bands) =>
        ShowsBand(headline, bands) ? ChartPaletteMap.BandClass(headline.BandIndex!.Value, bands.Count) : null;

    /// <summary>A signed whole number with a true minus sign: "+4", "−5", "0".</summary>
    public static string SignedPoints(int points, CultureInfo culture) => points switch
    {
        > 0 => "+" + points.ToString(culture),
        < 0 => "−" + Math.Abs(points).ToString(culture),
        _ => "0"
    };

    /// <summary>A fraction as a whole percentage, rounded half away from zero: 0.915 → "92".</summary>
    public static string Percent(double fraction, CultureInfo culture) =>
        ((int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero)).ToString(culture);

    /// <summary>A bound of the interval, rounded to an integer like the displayed value (S39 §4.5).</summary>
    public static string Bound(double value, CultureInfo culture) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(culture);

    // ------------------------------------------------------------------------------------------------------------------
    // Lines
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The 7-day line (§5.2.2, S39 §9.3), or null when the card shows none: no comparison was computed (a derived node), the
    /// view is partial (§5.6.5), or the value is indicative or empty. An arrow only for a worse or better move; otherwise the
    /// text says why there is none.
    /// </summary>
    public static HeadlineDelta? Delta(RiskIndexHeadlineDto headline, RiskIndexProvenance provenance, int? arrowThreshold,
        TextLookup text, CultureInfo culture)
    {
        if (headline.Delta7d is not { } delta || provenance == RiskIndexProvenance.PartialRestricted ||
            headline.DisplayState is RiskIndexDisplayState.Indicative or RiskIndexDisplayState.NoData)
            return null;

        switch (delta.SuppressedReason)
        {
            case RiskIndexDeltaSuppressedReason.Discontinuity:
                return new HeadlineDelta(Format(text, "RiskOverviewDeltaDiscontinuityFMT", culture,
                    delta.DiscontinuityDate?.ToString("d", culture) ?? "—"), "neutral", null, null);
            case RiskIndexDeltaSuppressedReason.MissingBaseline:
                return new HeadlineDelta(Get(text, "RiskOverviewDeltaMissingBaseline"), "neutral", null, null);
            case RiskIndexDeltaSuppressedReason.BelowThreshold:
                return Stable();
        }

        return delta.Direction switch
        {
            RiskIndexDeltaDirection.Worse when delta.Points is { } p => new HeadlineDelta(
                Format(text, "RiskOverviewDeltaWorseFMT", culture, SignedPoints(p, culture)), "worse",
                BandGlyph.ForDelta(RiskIndexDeltaDirection.Worse), RiskIndexDeltaDirection.Worse),
            RiskIndexDeltaDirection.Better when delta.Points is { } p => new HeadlineDelta(
                Format(text, "RiskOverviewDeltaBetterFMT", culture, SignedPoints(p, culture)), "better",
                BandGlyph.ForDelta(RiskIndexDeltaDirection.Better), RiskIndexDeltaDirection.Better),
            _ => Stable()
        };

        HeadlineDelta Stable() => new(arrowThreshold is { } t
                ? Format(text, "RiskOverviewDeltaStableFMT", culture, t.ToString(culture))
                : Get(text, "RiskOverviewDeltaStableNoThreshold"), "neutral",
            BandGlyph.ForDelta(RiskIndexDeltaDirection.Stable), RiskIndexDeltaDirection.Stable);
    }

    /// <summary>"Qualidade [Alta 0,91] · Cobertura 92% · Frescor 99%" (§5.2.2, S39 §8.5).</summary>
    public static HeadlineQuality Quality(RiskIndexQualitySummaryDto quality, TextLookup text, CultureInfo culture) =>
        new(text("RiskQuality_" + quality.QualityBandKey) ?? quality.QualityBandKey,
            quality.Quality.ToString("0.00", culture),
            Format(text, "RiskOverviewCoverageFMT", culture, Percent(quality.Coverage, culture)),
            Format(text, "RiskOverviewFreshnessFMT", culture, Percent(quality.Freshness, culture)));

    /// <summary>
    /// The ignorance interval (§5.2.2): on the card when the server says so (low coverage or a weighted category unavailable),
    /// otherwise "in the detail" with its bounds in the hint; null when there is none or the value is indicative (which says
    /// it in its own line).
    /// </summary>
    public static HeadlineInterval? Interval(RiskIndexHeadlineDto headline, TextLookup text, CultureInfo culture)
    {
        if (headline.DisplayState is RiskIndexDisplayState.Indicative or RiskIndexDisplayState.NoData) return null;
        if (headline.IntervalLow is not { } low || headline.IntervalHigh is not { } high) return null;

        var bounds = Format(text, "RiskOverviewIntervalOnCardFMT", culture, Bound(low, culture), Bound(high, culture));
        return headline.ShowIntervalOnCard
            ? new HeadlineInterval(true, bounds, null)
            : new HeadlineInterval(false, Get(text, "RiskOverviewIntervalInDetail"), bounds);
    }

    /// <summary>"ICR indicativo: 47 · intervalo [20–81]" and why it is not classified (§5.2.3); null unless indicative.</summary>
    public static (string Text, string Reason)? Indicative(RiskIndexHeadlineDto headline, double coverage,
        double? coverageIndicative, TextLookup text, CultureInfo culture)
    {
        if (headline.DisplayState != RiskIndexDisplayState.Indicative) return null;

        var value = headline.Displayed?.ToString(culture) ?? "—";
        var low = headline.IntervalLow is { } l ? Bound(l, culture) : "—";
        var high = headline.IntervalHigh is { } h ? Bound(h, culture) : "—";
        var reason = coverageIndicative is { } threshold
            ? Format(text, "RiskState_IndicativeReasonFMT", culture, Percent(coverage, culture), Percent(threshold, culture))
            : Format(text, "RiskState_IndicativeReasonNoThresholdFMT", culture, Percent(coverage, culture));
        return (Format(text, "RiskState_IndicativeFMT", culture, value, low, high), reason);
    }

    /// <summary>Which branch set the headline (§5.2.2, S39 §4.4/§4.8): the weighted mean, or a category held by τ_G.</summary>
    public static string? Branch(RiskIndexHeadlineDto headline, TextLookup text, CultureInfo culture)
    {
        if (headline.DisplayState == RiskIndexDisplayState.NoData) return null;
        if (headline.MeanBranchActive) return Get(text, "RiskOverviewBranchMean");
        return headline.HeldByCategory is { } category
            ? Format(text, "RiskOverviewBranchHeldByFMT", culture, CategoryCode(category))
            : null;
    }

    /// <summary>
    /// The unfloored value (§5.2.2, S39 §7): only in full mode and only while a floor holds the headline — "piso: Portão A —
    /// R1 (flag 2) · sem piso 64 (Médio)". Null otherwise; in interim mode no floor reason is ever sent.
    /// </summary>
    public static string? Unfloored(RiskIndexHeadlineDto headline, RiskIndexMode mode, IReadOnlyList<RiskIndexBandDto> bands,
        TextLookup text, CultureInfo culture, CultureInfo ui)
    {
        if (mode != RiskIndexMode.Full || headline.FloorReasons.Count == 0 || headline.Unfloored is not { } unfloored) return null;

        var reasons = string.Join("; ", headline.FloorReasons.Select(r =>
        {
            var reason = Format(text, "RiskOverviewFloorReasonFMT", culture, r.GateCode,
                ObjectLabelDisplay.Name(r.Scenario, text, culture));
            return r.FlagKey is { Length: > 0 } flag ? reason + " " + Format(text, "RiskOverviewFloorFlagFMT", culture, flag) : reason;
        }));
        var value = (int)Math.Round(unfloored, MidpointRounding.AwayFromZero);
        var band = BandOf(value, bands) is { } b ? BandLabel(b, ui) : "—";
        return Format(text, "RiskOverviewUnflooredFMT", culture, reasons, value.ToString(culture), band);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Seals and chips
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Each seal's sentence (S39 §6.6), its count in <c>{0}</c>; an unknown key reads as itself rather than vanishing.</summary>
    public static IReadOnlyList<string> Seals(IReadOnlyList<RiskIndexSealDto> seals, TextLookup text, CultureInfo culture) =>
        seals.Select(s => text("RiskSeal_" + s.Key + "MSG") is { } template
            ? string.Format(culture, template, s.Count ?? 0)
            : s.Key).ToList();

    /// <summary>
    /// The card's chips (§5.2.2): the server's headline chips minus those shown elsewhere, plus a "category unavailable" chip
    /// naming every weighted category without data — "AME e CTL indisponíveis".
    /// </summary>
    public static IReadOnlyList<HeadlineChip> Chips(IReadOnlyList<RiskIndexChipDto> chips,
        IReadOnlyList<RiskIndexCategoryDto> categories, TextLookup text, CultureInfo culture)
    {
        var shown = chips.Where(c => !NotShownAsChips.Contains(c.Key))
            .Select(c => new HeadlineChip(c.Key, text("RiskChip_" + c.Key + "MSG") ?? c.Key)).ToList();

        var unavailable = categories.Where(c => c.State == RiskIndexCategoryState.Unavailable && c.Weight > 0)
            .OrderBy(c => c.Category).Select(c => CategoryCode(c.Category)).ToList();
        if (unavailable.Count > 0)
        {
            var key = unavailable.Count == 1 ? "RiskChip_category_unavailableMSG" : "RiskChip_categories_unavailableMSG";
            shown.Add(new HeadlineChip(CategoryUnavailableChip, Format(text, key, culture, JoinList(unavailable, text))));
        }

        return shown;
    }

    /// <summary>"AME", "AME e CTL", "AME, CTL e GOV".</summary>
    public static string JoinList(IReadOnlyList<string> items, TextLookup text) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + Get(text, "RiskOverviewListAnd") + items[^1]
    };

    /// <summary>A category's code as S39 writes it: REG, EXP, AME, CTL, GOV, TER.</summary>
    public static string CategoryCode(RiskIndexCategory category) => category.ToString().ToUpperInvariant();

    // ------------------------------------------------------------------------------------------------------------------
    // Accessibility (§10.8)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The card's <c>AutomationProperties.Name</c>: "Índice consolidado de risco 64 de 100, faixa Médio, piorou 4 pontos em 7
    /// dias, cobertura 92 %". The delta clause is left out where the card shows no arrow.
    /// </summary>
    public static string AccessibleName(RiskIndexHeadlineDto? headline, IReadOnlyList<RiskIndexBandDto> bands, double coverage,
        RiskIndexProvenance provenance, TextLookup text, CultureInfo culture, CultureInfo ui)
    {
        if (headline is null || headline.DisplayState == RiskIndexDisplayState.NoData || headline.Displayed is null)
            return Get(text, "RiskOverviewA11yNoData");

        var value = headline.Displayed.Value.ToString(culture);
        var cov = Percent(coverage, culture);
        if (headline.DisplayState == RiskIndexDisplayState.Indicative)
            return Format(text, "RiskOverviewA11yIndicativeFMT", culture, value, cov);

        var band = ShowsBand(headline, bands) ? BandLabel(BandAt(bands, headline.BandIndex)!, ui) : "—";
        var clause = Delta(headline, provenance, null, text, culture)?.Direction switch
        {
            RiskIndexDeltaDirection.Worse => Format(text, "RiskOverviewA11yWorseFMT", culture,
                Math.Abs(headline.Delta7d!.Points ?? 0).ToString(culture)),
            RiskIndexDeltaDirection.Better => Format(text, "RiskOverviewA11yBetterFMT", culture,
                Math.Abs(headline.Delta7d!.Points ?? 0).ToString(culture)),
            RiskIndexDeltaDirection.Stable => Get(text, "RiskOverviewA11yStable"),
            _ => null
        };
        return Format(text, "RiskOverviewA11yFMT", culture, value, band, clause is null ? "" : ", " + clause, cov);
    }

    // ------------------------------------------------------------------------------------------------------------------

    private static string Get(TextLookup text, string key) => text(key) ?? key;

    private static string Format(TextLookup text, string key, CultureInfo culture, params object[] args) =>
        text(key) is { } template ? string.Format(culture, template, args) : key;
}
