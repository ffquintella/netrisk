using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.TreatmentEconomics;
using Tools.TreatmentEconomics;
using Xunit;

namespace Tools.Tests.TreatmentEconomics;

/// <summary>
/// Stage 9.6 (S47 §4.5, §8 TC1–TC4) — the two figures the gates read from a declared cost, and the validation of a
/// requested one.
/// </summary>
[TestSubject(typeof(TreatmentCost))]
public class TreatmentCostTest
{
    private static TreatmentCostRequest Request(decimal? oneTime = 300_000m, decimal? annual = 20_000m,
        decimal? side = 5_000m, int? horizon = 3) =>
        new() { OneTime = oneTime, Annual = annual, SideEffectsAnnual = side, HorizonYears = horizon };

    /// <summary>TC1 — Gate C's cost is annual + side effects + one-time ÷ horizon.</summary>
    [Fact]
    public void TestTC1_TheAnnualizedTotalAmortizesTheOneTimeCostAndIncludesSideEffects()
    {
        var cost = TreatmentCost.FromRequest(Request());

        Assert.Equal(20_000m + 5_000m + 100_000m, cost.AnnualizedTotal);
        Assert.Equal(125_000m, cost.ToDto().AnnualizedTotal);
    }

    /// <summary>TC2 — Gate D's budget draw is the first year's cash: one-time + annual, without side effects (S47 D4).</summary>
    [Fact]
    public void TestTC2_TheFirstYearIsOneTimePlusAnnualWithoutSideEffects()
    {
        var cost = TreatmentCost.FromRequest(Request());

        Assert.Equal(320_000m, cost.FirstYear);
        Assert.Equal(320_000m, cost.ToDto().FirstYear);
    }

    /// <summary>TC3 — every invalid amount is refused, naming the field.</summary>
    [Theory]
    [InlineData(null, 1.0, 1.0, 3, "Cost.OneTime")]
    [InlineData(1.0, null, 1.0, 3, "Cost.Annual")]
    [InlineData(1.0, 1.0, null, 3, "Cost.SideEffectsAnnual")]
    [InlineData(-1.0, 1.0, 1.0, 3, "Cost.OneTime")]
    [InlineData(1.0, -0.01, 1.0, 3, "Cost.Annual")]
    [InlineData(1.0, 1.0, -5.0, 3, "Cost.SideEffectsAnnual")]
    [InlineData(1.0, 1.0, 1.0, null, "Cost.HorizonYears")]
    [InlineData(1.0, 1.0, 1.0, 0, "Cost.HorizonYears")]
    [InlineData(1.0, 1.0, 1.0, 31, "Cost.HorizonYears")]
    [InlineData(2e12, 1.0, 1.0, 3, "Cost.OneTime")]
    public void TestTC3_AnInvalidAmountNamesTheField(double? oneTime, double? annual, double? side, int? horizon,
        string field)
    {
        var ex = Assert.Throws<InvalidParameterException>(() => TreatmentCost.FromRequest(Request(
            (decimal?)oneTime, (decimal?)annual, (decimal?)side, horizon)));

        Assert.Equal(field, ex.ParameterName);
    }

    /// <summary>TC4 — a one-time cost of zero needs no horizon, and none is kept.</summary>
    [Fact]
    public void TestTC4_AZeroOneTimeCostNeedsNoHorizon()
    {
        var cost = TreatmentCost.FromRequest(Request(oneTime: 0, horizon: null));

        Assert.Null(cost.HorizonYears);
        Assert.Equal(25_000m, cost.AnnualizedTotal);

        Assert.Null(TreatmentCost.FromRequest(Request(oneTime: 0, horizon: 99)).HorizonYears);
    }

    /// <summary>A stored row with no cost block reads as "not declared", never as zero.</summary>
    [Fact]
    public void TestTC5_AStoredRowWithoutTheBlockIsNotDeclared()
    {
        Assert.Null(TreatmentCost.FromStored(null));
        Assert.Null(TreatmentCost.FromStored(new MitigationEconomics()));

        var stored = TreatmentCost.FromStored(new MitigationEconomics
            { CostOneTime = 0, CostAnnual = 0, CostSideEffectsAnnual = 0 });
        Assert.NotNull(stored);
        Assert.Equal(0m, stored.AnnualizedTotal);
    }
}
