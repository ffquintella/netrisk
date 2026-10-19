using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Material.Icons;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T253 (S40 §5.4, §5.5.1, §10.4, §10.8): the monthly summary's labels, marks and table ("EXP 74", null as "—", never 0),
/// and the contributing categories' rows — fixed order, disabled left out, unavailable kept with its reason, the weight's
/// renormalization, who holds the category, and the meter widths.
/// </summary>
[TestSubject(typeof(MonthlySummaryFormatter))]
public class MonthlyAndCategoriesFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly IReadOnlyList<RiskIndexBandDto> Bands =
    [
        new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Médio", "Medium", 1, 3, 31), new("b2", "Alto", "High", 2, 5, 70)
    ];

    private static RiskIndexMonthlySummaryDto Summary() => new(RiskIndexTabGroup.Devices,
        [RiskIndexCategory.Exp, RiskIndexCategory.Ctl, RiskIndexCategory.Gov],
        [
            new RiskIndexMonthDto("2026-09", false, true,
                new Dictionary<RiskIndexCategory, double?> { [RiskIndexCategory.Exp] = 73.5, [RiskIndexCategory.Ctl] = null, [RiskIndexCategory.Gov] = 20.4 }),
            new RiskIndexMonthDto("2026-10", true, false,
                new Dictionary<RiskIndexCategory, double?> { [RiskIndexCategory.Exp] = 74.49, [RiskIndexCategory.Ctl] = 0, [RiskIndexCategory.Gov] = null })
        ], RiskIndexProvenance.OfficialDerived);

    [Fact]
    public void TestTheMonthsCarryThePartialAndProfileChangeMarks()
    {
        Assert.Equal(new[] { "set 26†", "out 26*" }, MonthlySummaryFormatter.MonthLabels(Summary(), PtBr));
        Assert.Equal("* mês parcial · † troca de perfil", MonthlySummaryFormatter.Legend(Summary(), RiskIndexTestText.Portuguese));
        Assert.Equal("", MonthlySummaryFormatter.Legend(Summary() with { Months = [] }, RiskIndexTestText.Portuguese));
    }

    /// <summary>§10.4, ui-standard §2.6: each bar reads "EXP 74" without its colour; a month with no value has no bar.</summary>
    [Fact]
    public void TestEachBarIsLabelledWithItsCodeAndNullIsNoBar()
    {
        var series = MonthlySummaryFormatter.Series(Summary());

        Assert.Equal(new[] { "EXP", "CTL", "GOV" }, series.Select(s => s.Code));
        Assert.Equal(new double?[] { null, 0 }, series[1].Values);
        Assert.Equal("EXP 74", MonthlySummaryFormatter.DataLabel(RiskIndexCategory.Exp, 73.5, PtBr));
        Assert.Equal("EXP 74", MonthlySummaryFormatter.DataLabel(RiskIndexCategory.Exp, 74.49, PtBr));
        Assert.Equal("", MonthlySummaryFormatter.DataLabel(RiskIndexCategory.Ctl, null, PtBr));
        Assert.Equal("CTL 0", MonthlySummaryFormatter.DataLabel(RiskIndexCategory.Ctl, 0, PtBr));
    }

    [Fact]
    public void TestTheTableIsNewestFirstWithADashForNoValue()
    {
        var rows = MonthlySummaryFormatter.TableRows(Summary(), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(("out 26*", "mês parcial", "74", "0", "—"), (rows[0].Month, rows[0].Note, rows[0].Exp, rows[0].Ctl, rows[0].Gov));
        Assert.Equal(("—", "a média mistura versões", "20"), (rows[1].Ctl, rows[1].Note, rows[1].Gov));
        Assert.Equal("—", rows[0].Reg); // not a series of the tab
        Assert.True(MonthlySummaryFormatter.HasValues(Summary()));
        Assert.False(MonthlySummaryFormatter.HasValues(Summary() with
        {
            Months = [new RiskIndexMonthDto("2026-10", true, false, new Dictionary<RiskIndexCategory, double?> { [RiskIndexCategory.Exp] = null })]
        }));
    }

    // --- contributing categories (§5.4) --------------------------------------------------------------------------------

    private static RiskIndexHeldByDto HeldBy(string label, bool tie = false, int count = 1, string? permission = null) =>
        new(new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Scenario, "1"), label, permission), 0.8, null, tie, count);

    [Fact]
    public void TestTheRowsAreInTheFixedOrderWithoutTheDisabledCategory()
    {
        IReadOnlyList<RiskIndexCategoryDto> categories =
        [
            TrendFormatterTest.Category(RiskIndexCategory.Gov, RiskIndexCategoryState.Available, 67),
            TrendFormatterTest.Category(RiskIndexCategory.Ter, RiskIndexCategoryState.Disabled),
            TrendFormatterTest.Category(RiskIndexCategory.Reg, RiskIndexCategoryState.Available, 68),
            TrendFormatterTest.Category(RiskIndexCategory.Ame, RiskIndexCategoryState.NotApplicable)
        ];

        var rows = CategoryRowFormatter.Rows(categories, Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Equal(new[] { "REG", "AME", "GOV" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { "Cenários do registro", "Ameaça e eventos", "Governança e tratamento" }, rows.Select(r => r.Name));
        Assert.True(CategoryRowFormatter.ShowsTerNote(categories));
        Assert.False(CategoryRowFormatter.ShowsTerNote([TrendFormatterTest.Category(RiskIndexCategory.Ter, RiskIndexCategoryState.Available)]));
    }

    [Fact]
    public void TestAnAvailableCategoryShowsValueBandWeightPointsHolderAndCoverage()
    {
        var c = TrendFormatterTest.Category(RiskIndexCategory.Exp, RiskIndexCategoryState.Available, 74.4, "b2", 0.3, 0.4, 22.4,
            HeldBy("APP-03"));

        var row = CategoryRowFormatter.Row(c, Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Equal(("74", 0.744, "Alto", MaterialIconKind.AlertOctagonOutline, "b5"),
            (row.ValueText, Math.Round(row.Fraction, 3), row.BandLabel, row.BandGlyph, row.BandClass));
        Assert.Equal(("0,30 → 0,40", "22", "APP-03 (piso)", "83%"), (row.WeightText, row.PointsText, row.HeldByText, row.CoverageText));
        Assert.Equal("0,5 pontos de presunção", row.CoverageHint);
        Assert.Null(row.StateText);
    }

    [Fact]
    public void TestTheHolderIsTheMeanATieOrARedactedObject()
    {
        Assert.Equal("média", CategoryRowFormatter.HeldBy(TrendFormatterTest.Category(RiskIndexCategory.Reg, RiskIndexCategoryState.Available),
            RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("empate entre 3 objetos", CategoryRowFormatter.HeldBy(TrendFormatterTest.Category(RiskIndexCategory.Reg,
            RiskIndexCategoryState.Available, heldBy: HeldBy("R1", tie: true, count: 3)), RiskIndexTestText.Portuguese, PtBr));

        var redacted = CategoryRowFormatter.Row(TrendFormatterTest.Category(RiskIndexCategory.Reg, RiskIndexCategoryState.Available,
            heldBy: HeldBy("Cenário #1", permission: "riskmanagement")), Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);
        Assert.Equal(("Cenário #1 (piso)", "Requer a permissão riskmanagement"), (redacted.HeldByText, redacted.HeldByHint));
    }

    [Theory]
    [InlineData(null, "M39", "Indisponível — requer M39")]
    [InlineData("no_incident_intake", null, "Indisponível — intake de incidentes inativo")]
    [InlineData("source_disabled", null, "Indisponível — nenhuma fonte habilitada no perfil")]
    [InlineData("something_new", null, "Indisponível")]
    public void TestAnUnavailableCategoryKeepsItsLineWithTheReason(string? reason, string? requires, string expected)
    {
        var row = CategoryRowFormatter.Row(TrendFormatterTest.Category(RiskIndexCategory.Ame, RiskIndexCategoryState.Unavailable,
            effective: 0, points: null, reason: reason, requires: requires), Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Equal(("—", "—", 0.0, expected), (row.ValueText, row.PointsText, row.Fraction, row.StateText));
        Assert.Equal("0,30 → 0,00", row.WeightText);
        Assert.Equal(MaterialIconKind.CloudOffOutline, row.StateGlyph);
        Assert.False(row.IsAvailable);
    }

    [Fact]
    public void TestANotApplicableCategoryIsDimmedAndSaysSo()
    {
        var row = CategoryRowFormatter.Row(TrendFormatterTest.Category(RiskIndexCategory.Reg, RiskIndexCategoryState.NotApplicable),
            Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.True(row.IsNotApplicable);
        Assert.Equal(("não se aplica a este tipo de nó", MaterialIconKind.Cancel), (row.StateText, row.StateGlyph));
    }

    [Fact]
    public void TestOnlyRegCarriesTheSeals()
    {
        IReadOnlyList<RiskIndexSealDto> seals = [new("scenarios_above_appetite", 2, new Dictionary<string, string>(), "seal=above_appetite")];

        var reg = CategoryRowFormatter.Row(TrendFormatterTest.Category(RiskIndexCategory.Reg, RiskIndexCategoryState.Available, seals: seals),
            Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);
        var gov = CategoryRowFormatter.Row(TrendFormatterTest.Category(RiskIndexCategory.Gov, RiskIndexCategoryState.Available, seals: seals),
            Bands, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Single(reg.Seals);
        Assert.Empty(gov.Seals);
    }

    [Theory]
    [InlineData("b0", 0)]
    [InlineData("b12", 12)]
    [InlineData("x1", null)]
    [InlineData("b", null)]
    [InlineData(null, null)]
    public void TestTheServerBandKeyReadsAsItsPosition(string? key, int? expected) =>
        Assert.Equal(expected, CategoryRowFormatter.BandIndex(key));

    [Fact]
    public void TestTheMeterAndTheSegmentsFitTheirBar()
    {
        Assert.Equal(70, MeterGeometry.Fill(0.5, 140));
        Assert.Equal(140, MeterGeometry.Fill(1.4, 140));
        Assert.Equal(0, MeterGeometry.Fill(double.NaN, 140));

        var widths = MeterGeometry.Segments([9000, 1, 0, 999], 400);
        Assert.Equal(400, widths.Sum(), 0);
        Assert.True(widths[1] >= 1.9, $"a one-object segment stays visible: {widths[1]}");
        Assert.Equal(0, widths[2]);
        Assert.Equal(new double[] { 0, 0 }, MeterGeometry.Segments([0, 0], 400));
    }
}
