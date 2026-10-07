using System;
using DAL.Enums;
using JetBrains.Annotations;
using Model.TreatmentEconomics;
using Tools.TreatmentEconomics;
using Xunit;

namespace Tools.Tests.TreatmentEconomics;

/// <summary>
/// Stage 9.6 (S47 §4.6, §8 GC1–GC10) — Gate C: <c>E[L before] − E[L after]</c> against the annualized total cost. The
/// edge cases the methodology names are here: a missing monetary cost is not assessable, never a silent pass (GC3,
/// T181); the means and never the medians (GC7); Gordon–Loeb as a reference and not a 37 % rule (GC8).
/// </summary>
[TestSubject(typeof(GateC))]
public class GateCTest
{
    /// <summary>A quantified reduce treatment: E[L] 100 000 → 40 000 (benefit 60 000) at the given annual cost.</summary>
    private static GateCInput Quantified(decimal annualCost, TreatmentOption option = TreatmentOption.Reduce) => new()
    {
        Option = option,
        Cost = new TreatmentCost(0, annualCost, 0, null),
        QuantitativeAnalysis = true,
        ExpectedLossBefore = 100_000,
        ExpectedLossAfter = 40_000,
        ResidualMedianRecorded = true
    };

    /// <summary>GC1 — the benefit above the cost passes, with the figures.</summary>
    [Fact]
    public void TestGC1_ABenefitAboveTheCostPasses()
    {
        var result = GateC.Evaluate(Quantified(50_000));

        Assert.Equal(GateCOutcome.Passes, result.Outcome);
        Assert.Empty(result.NotAssessableReasons);
        Assert.Equal(60_000, result.Benefit);
        Assert.Equal(50_000, result.AnnualizedCost);
        Assert.Equal(10_000, result.NetBenefit);
        Assert.Equal(1.2, result.BenefitCostRatio!.Value, 10);
    }

    /// <summary>GC2 — "exceed" is strict: a benefit equal to the cost fails, and so does a lower one.</summary>
    [Theory]
    [InlineData(60_000)]
    [InlineData(80_000)]
    public void TestGC2_ABenefitAtOrBelowTheCostFails(double cost)
    {
        var result = GateC.Evaluate(Quantified((decimal)cost));

        Assert.Equal(GateCOutcome.Fails, result.Outcome);
        Assert.True(result.NetBenefit <= 0);
    }

    /// <summary>
    /// GC3 (T181) — no monetary cost is <b>not</b> a zero cost: Gate C is not assessable, says why, and is never a pass
    /// even when the benefit is enormous.
    /// </summary>
    [Fact]
    public void TestGC3_NoMonetaryCostIsNotAssessableNeverAPass()
    {
        var input = Quantified(0) with { Cost = null, ExpectedLossBefore = 1e12, ExpectedLossAfter = 0 };

        var result = GateC.Evaluate(input);

        Assert.Equal(GateCOutcome.NotAssessable, result.Outcome);
        Assert.Equal([GateCNotAssessableReason.NoMonetaryCost], result.NotAssessableReasons);
        Assert.Null(result.AnnualizedCost);
        Assert.Null(result.NetBenefit);
        Assert.Contains("not a zero cost", result.Explanation);
        Assert.Contains("neither a pass nor a fail", result.Explanation);
    }

    /// <summary>GC4 — a declared zero cost is a cost: assessable, and it passes on any positive benefit.</summary>
    [Fact]
    public void TestGC4_ADeclaredZeroCostIsAssessable()
    {
        var result = GateC.Evaluate(Quantified(0));

        Assert.Equal(GateCOutcome.Passes, result.Outcome);
        Assert.Equal(0, result.AnnualizedCost);
        Assert.Null(result.BenefitCostRatio);
    }

    /// <summary>GC5 — "accept" has no control to weigh: not applicable, whatever else is known.</summary>
    [Fact]
    public void TestGC5_AcceptIsNotApplicable()
    {
        var result = GateC.Evaluate(Quantified(10, TreatmentOption.Accept) with { Cost = null });

        Assert.Equal(GateCOutcome.NotApplicable, result.Outcome);
        Assert.Empty(result.NotAssessableReasons);
    }

    /// <summary>GC6 — no quantitative analysis means no E[L] before: not assessable.</summary>
    [Fact]
    public void TestGC6_NoQuantitativeAnalysisIsNotAssessable()
    {
        var result = GateC.Evaluate(Quantified(10) with
            { QuantitativeAnalysis = false, ExpectedLossBefore = null, ExpectedLossAfter = null });

        Assert.Equal(GateCOutcome.NotAssessable, result.Outcome);
        Assert.Equal([GateCNotAssessableReason.NoQuantitativeAnalysis], result.NotAssessableReasons);
        Assert.Null(result.GordonLoebReference);
    }

    /// <summary>
    /// GC7 — the means, never the medians. A residual median without the residual mean (an analysis computed before
    /// schema 95) is not assessable rather than a fallback; no residual at all is "no residual run", not a zero benefit.
    /// </summary>
    [Fact]
    public void TestGC7_TheResidualMedianIsNeverAStandInForTheMean()
    {
        var noMean = GateC.Evaluate(Quantified(10) with { ExpectedLossAfter = null, ResidualMedianRecorded = true });
        Assert.Equal(GateCOutcome.NotAssessable, noMean.Outcome);
        Assert.Equal([GateCNotAssessableReason.ResidualMeanNotRecorded], noMean.NotAssessableReasons);
        Assert.Null(noMean.Benefit);

        var noRun = GateC.Evaluate(Quantified(10) with { ExpectedLossAfter = null, ResidualMedianRecorded = false });
        Assert.Equal([GateCNotAssessableReason.NoResidualRun], noRun.NotAssessableReasons);
        Assert.Null(noRun.Benefit);
    }

    /// <summary>
    /// GC8 — Gordon–Loeb is a reference, not a rule: a cost at 50 % of E[L] before (above 1/e ≈ 36.8 %) that is still
    /// below the benefit passes, with the reference reported as exceeded; one at 30 % is not above it.
    /// </summary>
    [Fact]
    public void TestGC8_GordonLoebIsReportedButNeverDecides()
    {
        var above = GateC.Evaluate(Quantified(50_000));
        Assert.Equal(GateCOutcome.Passes, above.Outcome);
        Assert.Equal(100_000 / System.Math.E, above.GordonLoebReference!.Value, 6);
        Assert.True(above.ExceedsGordonLoebReference);
        Assert.Contains("not a rule", above.Explanation);

        var below = GateC.Evaluate(Quantified(30_000));
        Assert.Equal(GateCOutcome.Passes, below.Outcome);
        Assert.False(below.ExceedsGordonLoebReference);
    }

    /// <summary>GC9 — every reason that holds is reported, in order.</summary>
    [Fact]
    public void TestGC9_EveryReasonIsReportedInOrder()
    {
        var result = GateC.Evaluate(Quantified(10) with
        {
            Cost = null, ExpectedLossAfter = null, ResidualMedianRecorded = true, ResidualIsForThisMitigation = false
        });

        Assert.Equal(
            [
                GateCNotAssessableReason.NoMonetaryCost, GateCNotAssessableReason.ResidualMeanNotRecorded,
                GateCNotAssessableReason.ResidualForAnotherMitigation
            ],
            result.NotAssessableReasons);
    }

    /// <summary>A residual computed for another mitigation of the risk is not this one's benefit.</summary>
    [Fact]
    public void TestGC9b_AResidualOfAnotherMitigationIsNotThisOnesBenefit()
    {
        var result = GateC.Evaluate(Quantified(10) with { ResidualIsForThisMitigation = false });

        Assert.Equal([GateCNotAssessableReason.ResidualForAnotherMitigation], result.NotAssessableReasons);
        Assert.Null(result.Benefit);
        Assert.Null(result.ExpectedLossAfter);
    }

    /// <summary>GC10 — with Gate A the result is still computed, but says it is informational: Gate A comes first.</summary>
    [Fact]
    public void TestGC10_GateAMakesTheResultInformational()
    {
        var result = GateC.Evaluate(Quantified(80_000) with { GateAHolds = true });

        Assert.Equal(GateCOutcome.Fails, result.Outcome);
        Assert.True(result.GateAHolds);
        Assert.StartsWith("Gate A holds", result.Explanation);
    }
}
