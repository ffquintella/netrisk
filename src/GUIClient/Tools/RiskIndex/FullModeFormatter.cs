using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>One Top Risks row (S40 §5.11; M59, T286): rank, scenario, Gate A, scores, E[L], confidence, next decision.</summary>
public sealed record TopRiskRow(int Rank, string Scenario, string GateA, string Scores, string Loss, string Confidence,
    string NextDecision, bool HasGateA, RiskIndexObjectLabelDto Object);

/// <summary>The three lines of the monetary panel (S40 §5.10; M59, T287) and Gate B on the portfolio.</summary>
public sealed record MonetaryLines(string Residual, string Inherent, string Portfolio, string? GateB, bool ResidualPartial,
    bool InherentPartial);

/// <summary>A reference line of the trend (S40 §5.3): the appetite ceiling × 10 and, in full mode, the target level.</summary>
public sealed record TrendReferenceLine(double Value, string Label, bool IsTarget);

/// <summary>
/// The text of the full-mode panels (S39 §15.1; S40 §5.3, §5.10–§5.12; M59, T286, T287) from their DTOs, pure so
/// <c>GUIClient.Tests</c> pins it. Every figure is the server's; an absent one reads "—" or its reason, never zero.
/// </summary>
public static class FullModeFormatter
{
    private const string Dash = "—";

    /// <summary>Why the Top Risks space holds no list: unavailable (interim mode) or empty; null when there are rows.</summary>
    public static string? TopRisksState(RiskIndexTopRisksDto dto, TextLookup text) =>
        !dto.Available ? RiskText.Get(text, "RiskTopRisks_Unavailable_" + (dto.UnavailableReasonKey ?? "requires_full_mode"))
        : dto.Items.Count == 0 ? RiskText.Get(text, "RiskTopRisks_Empty")
        : null;

    public static IReadOnlyList<TopRiskRow> TopRisks(RiskIndexTopRisksDto dto, TextLookup text, CultureInfo culture) =>
        !dto.Available
            ? []
            : dto.Items.Select(i => new TopRiskRow(i.Rank, ObjectLabelDisplay.Name(i.Scenario, text, culture),
                GateA(i, text, culture),
                i.Residual is null && i.Inherent is null
                    ? Dash
                    : RiskText.Format(text, "RiskTopRisks_ScoresFMT", culture, Score(i.Residual, culture), Score(i.Inherent, culture)),
                i.ExpectedAnnualLoss is { } loss ? RiskText.Format(text, "RiskTopRisks_LossFMT", culture, Money(loss, culture)) : Dash,
                RiskText.Get(text, "RiskTopRisks_Confidence_" + (i.EvidenceConfidence?.ToString(CultureInfo.InvariantCulture) ?? "none")),
                i.NextDecisionKey is { Length: > 0 } next ? RiskText.Get(text, "RiskTopRisks_Next_" + next) : Dash,
                i.GateA, i.Scenario)).ToList();

    /// <summary>"Portão A (flags 1, 3)": the conditions with a flag number; the declared one has none.</summary>
    private static string GateA(RiskIndexTopRiskDto item, TextLookup text, CultureInfo culture)
    {
        if (!item.GateA) return Dash;
        var numbered = item.GateAConditions.Where(c => c is >= 1 and <= 11).Order().ToList();
        return numbered.Count == 0
            ? RiskText.Get(text, "RiskTopRisks_GateA")
            : RiskText.Format(text, "RiskTopRisks_GateAFlagsFMT", culture,
                string.Join(", ", numbered.Select(c => c.ToString(culture))));
    }

    public static MonetaryLines Monetary(RiskIndexMonetaryDto dto, TextLookup text, CultureInfo culture)
    {
        var portfolio = dto.Portfolio is { } p
            ? RiskText.Format(text, "RiskMonetary_PortfolioFMT", culture,
                RiskText.Format(text, "RiskMonetary_P95FMT", culture, Money(p.P95, culture), Money(p.P95CiLow, culture),
                    Money(p.P95CiHigh, culture)),
                RiskText.Format(text, "RiskMonetary_Cvar95FMT", culture, Money(p.Cvar95, culture), Money(p.Cvar95CiLow, culture),
                    Money(p.Cvar95CiHigh, culture)),
                p.Members.ToString(culture), RiskText.Get(text, "RiskMonetary_Dependence_" + p.DependenceKey))
            : RiskText.Get(text, "RiskMonetary_PortfolioUnavailable_" + (dto.PortfolioUnavailableKey ?? "requires_full_mode"));
        var gateB = dto.Portfolio is { } q
            ? RiskText.Format(text, "RiskMonetary_GateBFMT", culture, RiskText.Get(text, "RiskGateB_" + q.GateBStateKey))
            : null;
        return new MonetaryLines(Sum(dto.Residual, text, culture), Sum(dto.Inherent, text, culture), portfolio, gateB,
            dto.Residual.Partial, dto.Inherent.Partial);
    }

    /// <summary>"R$ 1.240.000 · 3 de 41 cenários quantificados · 9% do peso de REG · parcial" — never a bare total.</summary>
    public static string Sum(RiskIndexLossSumDto sum, TextLookup text, CultureInfo culture)
    {
        if (sum.Sum is not { } value)
            return RiskText.Format(text, "RiskMonetary_NoneQuantifiedFMT", culture, sum.Total.ToString(culture));
        var line = RiskText.Format(text, "RiskMonetary_SumFMT", culture, Money(value, culture), sum.Quantified.ToString(culture),
            sum.Total.ToString(culture), HeadlineFormatter.Percent(sum.RegisterWeightFraction, culture));
        return sum.Partial ? line + " · " + RiskText.Get(text, "RiskMonetary_Partial") : line;
    }

    /// <summary>M40's distribution beside the quality (S40 §5.11): "confirmada 3 · indicativa 2 · hipótese 1 · não declarada 4".</summary>
    public static string EvidenceConfidence(RiskIndexEvidenceConfidenceDto c, TextLookup text, CultureInfo culture) =>
        RiskText.Format(text, "RiskQuality_EvidenceDistributionFMT", culture, c.Confirmed.ToString(culture),
            c.Indicative.ToString(culture), c.Hypothesis.ToString(culture), c.NotDeclared.ToString(culture));

    /// <summary>The trend's reference lines (S40 §5.3): the appetite ceiling × 10 on REG, and the target level in full mode.</summary>
    public static IReadOnlyList<TrendReferenceLine> ReferenceLines(RiskIndexTrendDto trend, TextLookup text, CultureInfo culture)
    {
        var lines = new List<TrendReferenceLine>();
        if (trend.AppetiteLine is { } appetite)
            lines.Add(new TrendReferenceLine(appetite,
                RiskText.Format(text, "RiskTrendAppetiteLineFMT", culture, appetite.ToString("0", culture)), false));
        if (trend.TargetLine is { } target)
            lines.Add(new TrendReferenceLine(target,
                RiskText.Format(text, "RiskTrendTargetLineFMT", culture, target.ToString("0", culture)), true));
        return lines;
    }

    private static string Score(double? value, CultureInfo culture) => value is { } v ? v.ToString("0.0", culture) : Dash;

    /// <summary>Money without a currency symbol: the FAIR-lite inputs carry none (S47 D7).</summary>
    private static string Money(double value, CultureInfo culture) => Math.Round(value).ToString("N0", culture);
}
