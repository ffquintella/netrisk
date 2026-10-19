using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>One series of the grouped columns: a category in the fixed order, a value per month (null = no bar).</summary>
public sealed record MonthlySeries(RiskIndexCategory Category, string Code, IReadOnlyList<double?> Values);

/// <summary>
/// One month of "Ver como tabela" (§5.5.1, §10.8): one cell per category, "—" where the month has no value. The six
/// properties are fixed so a <c>DataGrid</c> can bind them; the columns of categories outside the series are hidden.
/// </summary>
public sealed record MonthlyTableRow(string Month, string Note, string Reg, string Exp, string Ame, string Ctl, string Gov,
    string Ter);

/// <summary>
/// The monthly category summary's pure half (S40 §5.5.1, §10.4, §10.8; T253): month labels with the partial (*) and
/// profile-change (†) marks, the data labels that read the series without colour ("EXP 74"), the series in the fixed
/// order REG, EXP, AME, CTL, GOV, TER, and the table that repeats them.
/// </summary>
public static class MonthlySummaryFormatter
{
    public static readonly IReadOnlyList<int> MonthOptions = [6, 12];

    public const string PartialMark = "*";
    public const string ProfileChangeMark = "†";

    /// <summary>"out 26*", "set 26†": the abbreviated month of the culture (without ICU's trailing dot) and the year.</summary>
    public static string MonthLabel(RiskIndexMonthDto month, CultureInfo culture)
    {
        var label = DateTime.TryParseExact(month.YearMonth, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? culture.DateTimeFormat.GetAbbreviatedMonthName(start.Month).TrimEnd('.') + " " + start.ToString("yy", culture)
            : month.YearMonth;
        if (month.IsPartial) label += PartialMark;
        if (month.HasProfileChange) label += ProfileChangeMark;
        return label;
    }

    /// <summary>The whole number shown for a mean (S39 §4.5: half away from zero).</summary>
    public static int Displayed(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>"EXP 74" on each bar (§10.4, ui-standard §2.6): the series reads by its label, the colour reinforces.</summary>
    public static string DataLabel(RiskIndexCategory category, double? value, CultureInfo culture) =>
        value is { } v ? HeadlineFormatter.CategoryCode(category) + " " + Displayed(v).ToString(culture) : "";

    /// <summary>A table cell: the whole number, or "—" for a month without a value (never 0).</summary>
    public static string Cell(double? value, CultureInfo culture) => value is { } v ? Displayed(v).ToString(culture) : "—";

    /// <summary>The series to draw, in the order the server sent (the fixed order), one value per month.</summary>
    public static IReadOnlyList<MonthlySeries> Series(RiskIndexMonthlySummaryDto summary) =>
        summary.SeriesOrder.Select(c => new MonthlySeries(c, HeadlineFormatter.CategoryCode(c),
            summary.Months.Select(m => m.MeanByCategory.TryGetValue(c, out var v) ? v : null).ToList())).ToList();

    public static IReadOnlyList<string> MonthLabels(RiskIndexMonthlySummaryDto summary, CultureInfo culture) =>
        summary.Months.Select(m => MonthLabel(m, culture)).ToList();

    /// <summary>The table: newest month first, with "parcial" and "a média mistura versões" spelled out.</summary>
    public static IReadOnlyList<MonthlyTableRow> TableRows(RiskIndexMonthlySummaryDto summary, TextLookup text, CultureInfo culture) =>
        summary.Months.Reverse().Select(m =>
        {
            string Of(RiskIndexCategory c) =>
                summary.SeriesOrder.Contains(c) && m.MeanByCategory.TryGetValue(c, out var v) ? Cell(v, culture) : "—";
            var notes = new List<string>();
            if (m.IsPartial) notes.Add(RiskText.Get(text, "RiskMonthlyPartialMonth"));
            if (m.HasProfileChange) notes.Add(RiskText.Get(text, "RiskMonthlyMixesVersions"));
            return new MonthlyTableRow(MonthLabel(m, culture), string.Join(" · ", notes), Of(RiskIndexCategory.Reg),
                Of(RiskIndexCategory.Exp), Of(RiskIndexCategory.Ame), Of(RiskIndexCategory.Ctl), Of(RiskIndexCategory.Gov),
                Of(RiskIndexCategory.Ter));
        }).ToList();

    /// <summary>The legend of the marks, only for the marks the summary uses: "* mês parcial · † troca de perfil".</summary>
    public static string Legend(RiskIndexMonthlySummaryDto summary, TextLookup text)
    {
        var parts = new List<string>();
        if (summary.Months.Any(m => m.IsPartial)) parts.Add(PartialMark + " " + RiskText.Get(text, "RiskMonthlyPartialMonth"));
        if (summary.Months.Any(m => m.HasProfileChange))
            parts.Add(ProfileChangeMark + " " + RiskText.Get(text, "RiskMonthlyProfileChange"));
        return string.Join(" · ", parts);
    }

    /// <summary>Whether any month has any value: an all-empty summary shows its empty state, not an empty chart.</summary>
    public static bool HasValues(RiskIndexMonthlySummaryDto summary) =>
        summary.Months.Any(m => m.MeanByCategory.Values.Any(v => v is not null));
}
