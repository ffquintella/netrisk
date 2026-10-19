using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>A window of the trend (§5.3): 30, 90 or 365 days, and why it is off for the node shown.</summary>
public sealed record TrendWindowOption(int Days, string Label, bool IsEnabled, string? DisabledReason);

/// <summary>A series of the trend: the headline (ICR*) or one category.</summary>
public sealed record TrendSeriesOption(RiskIndexCategory Series, string Label);

/// <summary>One band's area over one profile stretch (§5.3 "Áreas de faixa", §10.4): [From, To) × [Lower, Upper].</summary>
public sealed record TrendBandArea(DateOnly From, DateOnly ToExclusive, int Lower, int Upper, int Position, int Count, string Label);

/// <summary>The ignorance interval of one day: an indicative day's whisker, or a day with a category unavailable.</summary>
public sealed record TrendWhisker(DateOnly Date, double Low, double High);

/// <summary>A dated mark on the trend (discontinuity marker or vendor annotation) with the sentence its hint shows.</summary>
public sealed record TrendMark(DateOnly Date, string Text);

/// <summary>One row of "Ver como tabela" (§5.3, §10.8): the same information the chart's hints carry.</summary>
public sealed record TrendTableRow(DateOnly Date, string DateText, string Unfloored, string Floored, string Ewma, string Delta,
    string State, string Interval, string Marker, string Gap);

/// <summary>
/// The trend's pure half (S40 §5.3, §10.4, §10.8; T252): which windows and series apply, the line, the band areas per profile
/// stretch, the floor and discontinuity marks, the shapes of gaps and indicative days, every hint's sentence and the table
/// that repeats them. The client never recomputes a value: everything here formats what the server sent.
/// </summary>
public static class TrendFormatter
{
    public static readonly IReadOnlyList<int> Windows = [30, 90, 365];

    public const int DefaultWindow = 90;

    /// <summary>A partial view has no trend (§5.0, S39 §10.4); neither has an unofficial live preview.</summary>
    public static bool HasTrend(RiskIndexProvenance provenance) =>
        provenance is RiskIndexProvenance.Official or RiskIndexProvenance.OfficialDerived;

    /// <summary>365 days only for a pre-computed node; derived nodes recompose each day, 30 or 90 of them (§5.3).</summary>
    public static bool AllowsWindow(int days, RiskIndexProvenance provenance) =>
        HasTrend(provenance) && Windows.Contains(days) && (days != 365 || provenance == RiskIndexProvenance.Official);

    /// <summary>The window to read: the one chosen when the node allows it, otherwise the default.</summary>
    public static int EffectiveWindow(int requested, RiskIndexProvenance provenance) =>
        AllowsWindow(requested, provenance) ? requested : DefaultWindow;

    public static IReadOnlyList<TrendWindowOption> WindowOptions(RiskIndexProvenance provenance, TextLookup text,
        CultureInfo culture) =>
        Windows.Select(days => new TrendWindowOption(days, RiskText.Format(text, "RiskTrendWindowFMT", culture, days),
            AllowsWindow(days, provenance),
            AllowsWindow(days, provenance) ? null : RiskText.Get(text, "RiskTrendWindowPrecomputedOnly"))).ToList();

    /// <summary>The headline, then each category the node carries in the fixed order (TER only while weighted).</summary>
    public static IReadOnlyList<TrendSeriesOption> SeriesOptions(IReadOnlyList<RiskIndexCategoryDto> categories, TextLookup text)
    {
        var options = new List<TrendSeriesOption> { new(RiskIndexCategory.Icr, RiskText.Get(text, "RiskTrendSeriesIcr")) };
        options.AddRange(ChartPaletteMap.SeriesOrder
            .Where(c => categories.Any(x => x.Category == c &&
                                            x.State is RiskIndexCategoryState.Available or RiskIndexCategoryState.Unavailable))
            .Select(c => new TrendSeriesOption(c, HeadlineFormatter.CategoryCode(c))));
        return options;
    }

    /// <summary>
    /// The daily line (ICR* or C_k, never floored): continuous through a gap, which repeats the last value (S39 §9.3), and
    /// broken only on a day with no data at all.
    /// </summary>
    public static double? LineValue(RiskIndexTrendPointDto point) =>
        point.IsGap || point.DisplayState != RiskIndexDisplayState.NoData ? point.Unfloored : null;

    /// <summary>Gap days: a hollow circle on the repeated value (§10.8).</summary>
    public static IReadOnlyList<(DateOnly Date, double Value)> Gaps(RiskIndexTrendDto trend) =>
        trend.Points.Where(p => p.IsGap && p.Unfloored is not null).Select(p => (p.Date, p.Unfloored!.Value)).ToList();

    /// <summary>Indicative days: a hollow square, never classified by the area it falls in (S39 §8.6).</summary>
    public static IReadOnlyList<(DateOnly Date, double Value)> Indicative(RiskIndexTrendDto trend) =>
        trend.Points.Where(p => !p.IsGap && p.DisplayState == RiskIndexDisplayState.Indicative && p.Unfloored is not null)
            .Select(p => (p.Date, p.Unfloored!.Value)).ToList();

    /// <summary>The interval of an indicative day, or of a day with a weighted category unavailable (§5.3).</summary>
    public static IReadOnlyList<TrendWhisker> Whiskers(RiskIndexTrendDto trend) =>
        trend.Points.Where(p => !p.IsGap && p.IntervalLow is not null && p.IntervalHigh is not null &&
                                (p.DisplayState == RiskIndexDisplayState.Indicative ||
                                 (p.DisplayState == RiskIndexDisplayState.PointWithInterval && p.UnavailableCategories.Count > 0)))
            .Select(p => new TrendWhisker(p.Date, p.IntervalLow!.Value, p.IntervalHigh!.Value)).ToList();

    /// <summary>
    /// One area per band per profile stretch (§5.3): when the bands changed inside the window, the areas change at the
    /// marker. A stretch runs to the next one's first day, so a gap between them is never left unshaded.
    /// </summary>
    public static IReadOnlyList<TrendBandArea> BandAreas(RiskIndexTrendDto trend, TextLookup text, CultureInfo culture,
        CultureInfo ui)
    {
        var segments = trend.BandSegments.OrderBy(s => s.From).ToList();
        var areas = new List<TrendBandArea>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var to = i + 1 < segments.Count ? segments[i + 1].From : segment.To.AddDays(1);
            var bands = segment.Bands.OrderBy(b => b.LowerInclusive).ToList();
            if (!ChartPaletteMap.IsValidBand(0, bands.Count)) continue;
            for (var b = 0; b < bands.Count; b++)
            {
                var upper = b + 1 < bands.Count ? bands[b + 1].LowerInclusive : 100;
                areas.Add(new TrendBandArea(segment.From, to, bands[b].LowerInclusive, upper, b, bands.Count,
                    BandAreaLabel(bands, b, text, culture, ui)));
            }
        }

        return areas;
    }

    /// <summary>"Baixo 0–30", "Médio 31–69", "Alto ≥ 70": the band's label in the interface's language and its range.</summary>
    public static string BandAreaLabel(IReadOnlyList<RiskIndexBandDto> bands, int position, TextLookup text, CultureInfo culture,
        CultureInfo ui)
    {
        var band = bands[position];
        var label = HeadlineFormatter.BandLabel(band, ui);
        return position + 1 < bands.Count
            ? RiskText.Format(text, "RiskTrendBandRangeFMT", culture, label, band.LowerInclusive, bands[position + 1].LowerInclusive - 1)
            : RiskText.Format(text, "RiskTrendBandTopFMT", culture, label, band.LowerInclusive);
    }

    /// <summary>The days a floor held the headline (full mode only; never in interim, S39 §7): [From, To + 1).</summary>
    public static IReadOnlyList<(DateOnly From, DateOnly ToExclusive)> FloorRanges(RiskIndexTrendDto trend) =>
        trend.FlooredRanges.Select(r => (r.From, r.To.AddDays(1))).ToList();

    public static IReadOnlyList<TrendMark> Markers(RiskIndexTrendDto trend, TextLookup text, CultureInfo culture) =>
        trend.Markers.GroupBy(m => m.Date).OrderBy(g => g.Key)
            .Select(g => new TrendMark(g.Key, g.Key.ToString("d", culture) + " — " +
                                             string.Join(" · ", g.Select(m => MarkerText(m, text, culture))) + " · " +
                                             RiskText.Get(text, "RiskMarker_Restart")))
            .ToList();

    /// <summary>
    /// What a discontinuity was (§5.3, S39 §9.5): "Perfil v2 → v3 · Δ da versão −3,4"; "Teto de apetite alterado"…
    /// The arrow and the EWMA restart at every one of them.
    /// </summary>
    public static string MarkerText(RiskIndexMarkerDto marker, TextLookup text, CultureInfo culture)
    {
        string sentence;
        if (marker.Kind == RiskIndexMarkerKind.Profile && marker.Args.TryGetValue("from_version", out var from) &&
            marker.Args.TryGetValue("to_version", out var to))
            sentence = RiskText.Format(text, "RiskMarker_ProfileFMT", culture, from, to);
        else sentence = RiskText.Get(text, "RiskMarker_" + marker.Kind);

        return marker.VersionDelta is { } delta
            ? sentence + " · " + RiskText.Format(text, "RiskMarker_VersionDeltaFMT", culture, Signed(delta, culture))
            : sentence;
    }

    /// <summary>Vendor annotations: a hollow diamond at the foot, never a break (§5.3, S39 §9.5).</summary>
    public static IReadOnlyList<TrendMark> Annotations(RiskIndexTrendDto trend, TextLookup text, CultureInfo culture) =>
        trend.Annotations.OrderBy(a => a.Date).Select(a =>
        {
            var kind = Enum.IsDefined(typeof(AnnotationKind), a.Kind) ? ((AnnotationKind)a.Kind).ToString() : "Note";
            var head = a.Date.ToString("d", culture) + " — " + RiskText.Get(text, "RiskAnnotation_" + kind) + " (" + a.Source +
                       "): " + a.Label;
            return new TrendMark(a.Date, string.IsNullOrWhiteSpace(a.Details) ? head : head + " · " + a.Details);
        }).ToList();

    /// <summary>The annotation kinds of S40 §8.3 (the DTO carries the number).</summary>
    private enum AnnotationKind { VendorModel = 1, VendorRecalibration = 2, VendorBreachPenalty = 3, Note = 4 }

    /// <summary>
    /// A point's hint (§5.3 "Dica do ponto"): date and value, the floored value when it differs, Δ to the previous day,
    /// the indicative or interval state, a gap, the three largest movers and the day's markers. Never names the band an
    /// indicative point falls in.
    /// </summary>
    public static string PointTooltip(RiskIndexTrendPointDto point, IReadOnlyList<TrendMark> markersOfDay, TextLookup text,
        CultureInfo culture)
    {
        var lines = new List<string>
        {
            point.Date.ToString("d", culture) + " — " + (point.Displayed is { } v ? v.ToString(culture) : RiskText.Get(text, "RiskState_NoData"))
        };
        if (point.DisplayedFloored is { } floored && point.Displayed is { } shown && floored != shown)
            lines.Add(RiskText.Format(text, "RiskTrendFlooredFMT", culture, floored));
        if (point.DeltaFromPrevious is { } delta)
            lines.Add(RiskText.Format(text, "RiskTrendDeltaFMT", culture, HeadlineFormatter.SignedPoints(delta, culture)));
        if (StateText(point, text, culture) is { Length: > 0 } state) lines.Add(state);
        if (point.IsGap) lines.Add(RiskText.Get(text, "RiskTrendGapHint"));
        foreach (var mover in point.TopMovers)
            lines.Add("• " + (mover.Object is { } moved ? ObjectLabelDisplay.Name(moved, text, culture) : RiskText.Get(text, mover.LabelKey)) + " " +
                      HeadlineFormatter.SignedPoints(mover.PointsRounded, culture));
        lines.AddRange(markersOfDay.Select(m => m.Text));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>"indicativo, sem faixa · intervalo [20–81]"; "intervalo [40–71]: AME, CTL indisponíveis"; or empty.</summary>
    public static string StateText(RiskIndexTrendPointDto point, TextLookup text, CultureInfo culture)
    {
        var interval = point.IntervalLow is { } low && point.IntervalHigh is { } high
            ? RiskText.Format(text, "RiskTrendIntervalFMT", culture, HeadlineFormatter.Bound(low, culture),
                HeadlineFormatter.Bound(high, culture))
            : null;
        if (point.IsGap) return "";
        return point.DisplayState switch
        {
            RiskIndexDisplayState.Indicative => interval is null
                ? RiskText.Get(text, "RiskTrendIndicative")
                : RiskText.Get(text, "RiskTrendIndicative") + " · " + interval,
            RiskIndexDisplayState.PointWithInterval when point.UnavailableCategories.Count > 0 && interval is not null =>
                RiskText.Format(text, "RiskTrendUnavailableIntervalFMT", culture, interval,
                    HeadlineFormatter.JoinList(point.UnavailableCategories.OrderBy(c => c).Select(HeadlineFormatter.CategoryCode).ToList(),
                        text)),
            RiskIndexDisplayState.NoData => RiskText.Get(text, "RiskState_NoData"),
            _ => ""
        };
    }

    /// <summary>The table alternative (§10.8): one row per day, newest first, with every hint's information.</summary>
    public static IReadOnlyList<TrendTableRow> TableRows(RiskIndexTrendDto trend, TextLookup text, CultureInfo culture)
    {
        var markers = Markers(trend, text, culture).ToDictionary(m => m.Date);
        return trend.Points.OrderByDescending(p => p.Date).Select(p => new TrendTableRow(p.Date, p.Date.ToString("d", culture),
            p.Displayed?.ToString(culture) ?? "—", p.DisplayedFloored?.ToString(culture) ?? "—",
            p.Ewma is { } e ? e.ToString("0.0", culture) : "—",
            p.DeltaFromPrevious is { } d ? HeadlineFormatter.SignedPoints(d, culture) : "—",
            StateLabel(p, text), StateText(p, text, culture), markers.TryGetValue(p.Date, out var m) ? m.Text : "",
            p.IsGap ? RiskText.Get(text, "RiskTrendGapHint") : "")).ToList();
    }

    /// <summary>The display state's name (S39 §8.6): point, point with interval, indicative, no data.</summary>
    public static string StateLabel(RiskIndexTrendPointDto point, TextLookup text) =>
        RiskText.Get(text, point.IsGap ? "RiskTrendState_Gap" : "RiskTrendState_" + point.DisplayState);

    /// <summary>"+0,8", "−3,4": one decimal with a true minus sign.</summary>
    public static string Signed(double value, CultureInfo culture) => value switch
    {
        > 0 => "+" + value.ToString("0.0", culture),
        < 0 => "−" + Math.Abs(value).ToString("0.0", culture),
        _ => value.ToString("0.0", culture)
    };
}

/// <summary>Resource lookups that fall back to the key, so a missing resource is visible rather than blank.</summary>
internal static class RiskText
{
    public static string Get(TextLookup text, string key) => text(key) ?? key;

    public static string Format(TextLookup text, string key, CultureInfo culture, params object[] args) =>
        text(key) is { } template ? string.Format(culture, template, args) : key;
}
