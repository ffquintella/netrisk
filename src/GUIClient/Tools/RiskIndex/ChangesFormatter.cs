using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The period of "O que mudou" (§5.7): since the previous snapshot (the default), 7 or 30 days.</summary>
public enum ChangesPeriod { PreviousSnapshot = 0, Days7 = 7, Days30 = 30 }

/// <summary>One line of "O que mudou": the effect, the category, the origin (an object or the effect itself) and the share.</summary>
public sealed record ChangeRow(string Effect, string Category, string Origin, string? OriginHint, string Points, string Percent,
    RiskIndexObjectLabelDto? Object)
{
    public bool HasObject => Object is not null;
}

/// <summary>The panel as shown: the period line, at most <see cref="ChangesFormatter.MaxRows"/> lines and the rest summed.</summary>
public sealed record ChangesView(string Period, IReadOnlyList<ChangeRow> Rows, ChangeRow? Rest, string? ConfigurationNote,
    IReadOnlyList<string> Events, string? ResidualNote);

/// <summary>
/// "O que mudou" (S40 §5.7, S39 §9.4): the server's Aumann–Shapley attribution of the change between two snapshots, by
/// effect, category and object, ordered by magnitude. The 100 largest lines are listed and the rest summed in one; a
/// period that crosses a profile change says how much of it is the configuration's (Δ_versão). Whole points on screen.
/// </summary>
public static class ChangesFormatter
{
    public const int MaxRows = 100;
    private const string Dash = "—";

    /// <summary>The <c>from</c> of the request: none for the previous snapshot (the server's default), else the window's start.</summary>
    public static DateOnly? From(ChangesPeriod period, DateOnly to) =>
        period == ChangesPeriod.PreviousSnapshot ? null : to.AddDays(-(int)period);

    public static string PeriodKey(ChangesPeriod period) => "RiskChangesPeriod_" + period;

    public static ChangesView Format(RiskIndexChangesDto changes, TextLookup text, CultureInfo culture)
    {
        var ordered = changes.Rows.OrderByDescending(r => Math.Abs(r.Points)).ToList();
        var shown = ordered.Take(MaxRows).Select(r => Row(r, text, culture)).ToList();
        var rest = ordered.Skip(MaxRows).ToList();

        ChangeRow? restRow = null;
        if (rest.Count > 0)
        {
            var points = rest.Sum(r => r.Points);
            double? percent = rest.Any(r => r.PercentOfChange is null) ? null : rest.Sum(r => r.PercentOfChange!.Value);
            restRow = new ChangeRow(RiskText.Get(text, "RiskChangesRest"), Dash,
                RiskText.Format(text, "RiskChangesRestFMT", culture, rest.Count.ToString("N0", culture)), null,
                HeadlineFormatter.SignedPoints(Whole(points), culture), PercentText(percent, culture), null);
        }

        var period = RiskText.Format(text, "RiskChangesPeriodFMT", culture, changes.From.ToString("d", culture),
            changes.To.ToString("d", culture), HeadlineFormatter.SignedPoints(changes.DeltaDisplayed, culture),
            changes.DeltaUnfloored.ToString("+0.0;−0.0;0.0", culture));
        var residual = Math.Abs(changes.Residual) >= 0.5
            ? RiskText.Format(text, "RiskChangesResidualFMT", culture, changes.Residual.ToString("0.0", culture))
            : null;
        return new ChangesView(period, shown, restRow, ConfigurationNote(changes, text, culture),
            changes.Events.Select(e => Event(e, text, culture)).ToList(), residual);
    }

    /// <summary>
    /// The share of a profile change (S39 §9.5): a period across a marker shows the version's jump as the configuration
    /// effect. Null when the period crosses no marker.
    /// </summary>
    public static string? ConfigurationNote(RiskIndexChangesDto changes, TextLookup text, CultureInfo culture)
    {
        if (changes.CrossedMarkers.Count == 0) return null;
        var configuration = changes.Rows.Where(r => r.Effect == RiskIndexAttributionEffect.Configuration).ToList();
        var points = configuration.Sum(r => r.Points);
        double? percent = configuration.Count == 0 ? 0
            : configuration.Any(r => r.PercentOfChange is null) ? null : configuration.Sum(r => r.PercentOfChange!.Value);
        var dates = string.Join(", ", changes.CrossedMarkers.Order().Select(d => d.ToString("d", culture)));
        return RiskText.Format(text, "RiskChangesCrossedProfileFMT", culture, dates,
            HeadlineFormatter.SignedPoints(Whole(points), culture), PercentText(percent, culture));
    }

    public static ChangeRow Row(RiskIndexAttributionRowDto row, TextLookup text, CultureInfo culture)
    {
        var effect = text(row.LabelKey) ?? RiskText.Get(text, "RiskEffect_" + row.Effect);
        var category = row.Category is { } c ? HeadlineFormatter.CategoryCode(c) : Dash;
        string origin;
        string? hint = null;
        if (row.Object is { } obj)
        {
            var label = ObjectLabelDisplay.Of(obj, text, culture);
            hint = label.Hint;
            origin = label.Label;
            if (row.IsReattribution) origin = RiskText.Format(text, "RiskChangesReattributionFMT", culture, origin);
            if (row.AuthorName is { Length: > 0 } author)
                origin = RiskText.Format(text, "RiskChangesAuthorFMT", culture, origin, author);
        }
        else origin = text("RiskEffectOrigin_" + row.Effect) ?? Dash;

        return new ChangeRow(effect, category, origin, hint, HeadlineFormatter.SignedPoints(row.PointsRounded, culture),
            PercentText(row.PercentOfChange, culture), row.Object);
    }

    /// <summary>An event of the day: its sentence (<c>RiskEvent_‹kind›</c>) and, when it names one, the object.</summary>
    public static string Event(RiskIndexChangeEventDto e, TextLookup text, CultureInfo culture)
    {
        var sentence = text("RiskEvent_" + e.Kind) ?? e.Kind;
        return e.Object is { } obj ? sentence + ": " + ObjectLabelDisplay.Name(obj, text, culture) : sentence;
    }

    /// <summary>A share of the change as a whole percentage with the same minus sign as the points: "−6%".</summary>
    public static string PercentText(double? percent, CultureInfo culture) =>
        percent is { } p ? HeadlineFormatter.SignedPoints(Whole(p), culture).TrimStart('+') + "%" : Dash;

    private static int Whole(double points) => (int)Math.Round(points, MidpointRounding.AwayFromZero);

    /// <summary>The resource keys this formatter computes, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexAttributionEffect>().Select(e => "RiskEffect_" + e)
            .Concat(new[]
            {
                RiskIndexAttributionEffect.Configuration, RiskIndexAttributionEffect.Coverage,
                RiskIndexAttributionEffect.Unattributed, RiskIndexAttributionEffect.Floor
            }.Select(e => "RiskEffectOrigin_" + e))
            .Concat(Enum.GetValues<ChangesPeriod>().Select(PeriodKey))
            .Concat(new[]
            {
                "RiskChangesRest", "RiskChangesRestFMT", "RiskChangesPeriodFMT", "RiskChangesResidualFMT",
                "RiskChangesCrossedProfileFMT", "RiskChangesReattributionFMT", "RiskChangesAuthorFMT"
            });
}
