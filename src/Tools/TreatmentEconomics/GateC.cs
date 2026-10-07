using System.Globalization;
using DAL.Enums;
using Model.TreatmentEconomics;

namespace Tools.TreatmentEconomics;

/// <summary>What Gate C needs about one mitigation (S47 §4.6).</summary>
public sealed record GateCInput
{
    /// <summary>The declared option; null when the mitigation has no economics row.</summary>
    public TreatmentOption? Option { get; init; }

    /// <summary>The declared monetary cost; null when none is declared — never a zero.</summary>
    public TreatmentCost? Cost { get; init; }

    /// <summary>Whether the risk has a quantitative analysis (<c>quant_computed_at</c> set).</summary>
    public bool QuantitativeAnalysis { get; init; }

    /// <summary>The inherent mean annual loss, <c>quant_ale_mean</c>.</summary>
    public double? ExpectedLossBefore { get; init; }

    /// <summary>The residual mean annual loss, <c>quant_residual_ale_mean</c>.</summary>
    public double? ExpectedLossAfter { get; init; }

    /// <summary>Whether a residual median (<c>quant_residual_ale_p50</c>) exists — a residual run happened.</summary>
    public bool ResidualMedianRecorded { get; init; }

    /// <summary>Whether the residual run was computed for this mitigation (the risk's most recent one).</summary>
    public bool ResidualIsForThisMitigation { get; init; } = true;

    public bool GateAHolds { get; init; }

    public DateTime? QuantComputedAt { get; init; }

    public bool Stale { get; init; }
}

/// <summary>
/// Gate C of MIGR-TI/IA Phase 4 — marginal economics (Stage 9.6, S47 §4.6): a control is worth prioritizing
/// when <c>E[L before] − E[L after]</c> exceeds its total cost, side effects included.
///
/// Three rules a naive implementation breaks, each pinned by a test in <c>GateCTest</c>:
/// <list type="number">
/// <item><b>No monetary cost is not a zero cost.</b> It is <see cref="GateCOutcome.NotAssessable"/> with
/// <see cref="GateCNotAssessableReason.NoMonetaryCost"/>, whatever the benefit — never a silent pass (GC3, T181).</item>
/// <item><b>The means, never the medians.</b> The median year of a low-frequency risk has no loss at all, so a
/// median benefit is zero for exactly the risks a quantitative method exists to surface — the defect T142 fixed in
/// the band mapping. A residual median without the residual mean is <see cref="GateCNotAssessableReason.ResidualMeanNotRecorded"/>,
/// not a fallback (GC7).</item>
/// <item><b>Gordon–Loeb is a reference, not a rule.</b> Gordon &amp; Loeb (2002) bound the optimal security
/// investment by 1/e ≈ 36.8 % of the expected loss for two classes of breach-probability functions; the methodology
/// cites it "as a reference, not a fixed rule". The reference is reported beside the result and never changes the
/// outcome: a cost above 36.8 % of E[L] that is still below the benefit passes (GC8, S47 D3).</item>
/// </list>
/// Gate A comes first: when it holds the result is informational — a failing Gate C never deprioritizes a mandatory
/// treatment (GC10).
/// </summary>
public static class GateC
{
    /// <summary>1/e — the Gordon–Loeb bound, shown as a reference only.</summary>
    public static readonly double GordonLoebFraction = 1.0 / System.Math.E;

    public static GateCResultDto Evaluate(GateCInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result = new GateCResultDto
        {
            GateAHolds = input.GateAHolds,
            QuantComputedAt = input.QuantComputedAt,
            Stale = input.Stale,
            AnnualizedCost = input.Cost is null ? null : (double)input.Cost.AnnualizedTotal
        };

        if (input.Option == TreatmentOption.Accept)
        {
            result.Outcome = GateCOutcome.NotApplicable;
            result.Explanation = Prefix(input) +
                                 "The treatment option is 'accept': there is no control to weigh, so Gate C does not apply. " +
                                 "A formal acceptance still goes through Gate A, segregation of duties, authority and the appetite.";
            return result;
        }

        var reasons = new List<GateCNotAssessableReason>();
        if (input.Cost is null) reasons.Add(GateCNotAssessableReason.NoMonetaryCost);

        var analysis = input.QuantitativeAnalysis && input.ExpectedLossBefore is not null;
        if (!analysis)
        {
            reasons.Add(GateCNotAssessableReason.NoQuantitativeAnalysis);
        }
        else
        {
            result.ExpectedLossBefore = input.ExpectedLossBefore;
            result.GordonLoebReference = input.ExpectedLossBefore!.Value * GordonLoebFraction;

            if (input.ExpectedLossAfter is null)
                reasons.Add(input.ResidualMedianRecorded
                    ? GateCNotAssessableReason.ResidualMeanNotRecorded
                    : GateCNotAssessableReason.NoResidualRun);

            if (!input.ResidualIsForThisMitigation)
                reasons.Add(GateCNotAssessableReason.ResidualForAnotherMitigation);

            if (input.ExpectedLossAfter is not null && input.ResidualIsForThisMitigation)
            {
                result.ExpectedLossAfter = input.ExpectedLossAfter;
                result.Benefit = input.ExpectedLossBefore.Value - input.ExpectedLossAfter.Value;
            }
        }

        if (result.AnnualizedCost is { } knownCost && result.GordonLoebReference is { } glReference)
            result.ExceedsGordonLoebReference = knownCost > glReference;

        if (reasons.Count > 0)
        {
            result.Outcome = GateCOutcome.NotAssessable;
            result.NotAssessableReasons = reasons.OrderBy(r => (int)r).ToList();
            result.Explanation = Prefix(input) + "Gate C cannot be assessed: " +
                                 string.Join("; ", result.NotAssessableReasons.Select(Describe)) +
                                 ". This is neither a pass nor a fail.";
            return result;
        }

        var benefit = result.Benefit!.Value;
        var cost = result.AnnualizedCost!.Value;

        result.NetBenefit = benefit - cost;
        result.BenefitCostRatio = cost > 0 ? benefit / cost : null;
        result.Outcome = benefit > cost ? GateCOutcome.Passes : GateCOutcome.Fails;

        var verdict = result.Outcome == GateCOutcome.Passes
            ? $"Gate C passes: the expected annual loss avoided ({Money(benefit)}) exceeds the annualized total cost ({Money(cost)})."
            : $"Gate C fails: the expected annual loss avoided ({Money(benefit)}) does not exceed the annualized total cost ({Money(cost)}).";

        var reference = result.ExceedsGordonLoebReference == true
            ? $" The cost is above the Gordon–Loeb reference of {Money(result.GordonLoebReference!.Value)} (1/e of the expected " +
              "loss) — a reference the methodology cites, not a rule, so it does not change the outcome."
            : string.Empty;

        result.Explanation = Prefix(input) + verdict + reference;
        return result;
    }

    /// <summary>A sentence per reason, written for the person reading the result.</summary>
    public static string Describe(GateCNotAssessableReason reason) => reason switch
    {
        GateCNotAssessableReason.NoMonetaryCost =>
            "no monetary cost is declared (a missing cost is not a zero cost)",
        GateCNotAssessableReason.NoQuantitativeAnalysis =>
            "the risk has no quantitative analysis, so there is no expected loss before the treatment",
        GateCNotAssessableReason.NoResidualRun =>
            "the quantitative analysis has no residual run — the mitigation's effectiveness is zero or was not declared",
        GateCNotAssessableReason.ResidualMeanNotRecorded =>
            "the residual mean was not recorded (the analysis predates it) — recompute it; the median is never used instead",
        GateCNotAssessableReason.ResidualForAnotherMitigation =>
            "the residual run was computed for another, more recent mitigation of the same risk",
        _ => reason.ToString()
    };

    private static string Prefix(GateCInput input) => input.GateAHolds
        ? "Gate A holds: this treatment is mandatory whatever the economics, and this result is informational. "
        : string.Empty;

    private static string Money(double value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
