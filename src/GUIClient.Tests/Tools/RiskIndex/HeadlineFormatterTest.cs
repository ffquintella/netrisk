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
/// T251 (S40 §5.2–§5.2.3, §10.8, §12.4): the headline card's wording against the real pt-BR and en resources — the integer
/// and its band, the 7-day line and why it has no arrow, quality and coverage, the interval, the indicative and empty
/// states, the branch, the unfloored value of full mode, seals and chips, and the card's accessible name.
/// </summary>
[TestSubject(typeof(HeadlineFormatter))]
public class HeadlineFormatterTest
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly TextLookup PtText = RiskIndexTestText.Portuguese;
    private static readonly Dictionary<string, string> NoArgs = new();

    private static readonly IReadOnlyList<RiskIndexBandDto> ThreeBands =
    [
        new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Médio", "Medium", 1, 3, 31), new("b2", "Alto", "High", 2, 5, 70)
    ];

    private static RiskIndexHeadlineDto Headline(int? displayed = 64, int? band = 1,
        RiskIndexDisplayState state = RiskIndexDisplayState.Point, RiskIndexDeltaDto? delta = null, double? low = 53.2,
        double? high = 79.4, bool intervalOnCard = false, bool meanBranch = true, RiskIndexCategory? heldBy = null,
        double? unfloored = 63.6, IReadOnlyList<RiskIndexFloorReasonDto>? floors = null) =>
        new(displayed, unfloored, displayed, band is null ? null : "b" + band, band, state, low, high, intervalOnCard, delta,
            floors ?? [], meanBranch, heldBy);

    private static RiskIndexDeltaDto Worse(int points) =>
        new(points, RiskIndexDeltaDirection.Worse, new DateOnly(2026, 9, 28), null, null);

    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TestAPointValueShowsItsBandWithTheRampStepClass()
    {
        var headline = Headline();

        Assert.True(HeadlineFormatter.ShowsBand(headline, ThreeBands));
        Assert.Equal("b3", HeadlineFormatter.BandClass(headline, ThreeBands));
        Assert.Equal("Médio", HeadlineFormatter.BandLabel(HeadlineFormatter.BandAt(ThreeBands, 1)!, Pt));
        Assert.Equal("Medium", HeadlineFormatter.BandLabel(HeadlineFormatter.BandAt(ThreeBands, 1)!, En));
    }

    [Theory]
    [InlineData(RiskIndexDisplayState.Indicative)]
    [InlineData(RiskIndexDisplayState.NoData)]
    public void TestIndicativeAndEmptyValuesAreNeverClassified(RiskIndexDisplayState state)
    {
        var headline = Headline(state: state);

        Assert.False(HeadlineFormatter.ShowsBand(headline, ThreeBands));
        Assert.Null(HeadlineFormatter.BandClass(headline, ThreeBands));
    }

    [Fact]
    public void TestABandIndexTheProfileDoesNotHaveShowsNoBand()
    {
        Assert.False(HeadlineFormatter.ShowsBand(Headline(band: 3), ThreeBands));
        Assert.False(HeadlineFormatter.ShowsBand(Headline(band: null), ThreeBands));
        Assert.False(HeadlineFormatter.ShowsBand(Headline(displayed: null), ThreeBands));
    }

    [Theory]
    [InlineData(0, "Baixo")]
    [InlineData(30, "Baixo")]
    [InlineData(31, "Médio")]
    [InlineData(69, "Médio")]
    [InlineData(70, "Alto")]
    [InlineData(100, "Alto")]
    public void TestAValueFallsInTheBandOfItsInclusiveLowerBound(int value, string label)
    {
        Assert.Equal(label, HeadlineFormatter.BandOf(value, ThreeBands)!.LabelPtBr);
    }

    [Theory]
    [InlineData(52.5, "53")]
    [InlineData(79.49, "79")]
    [InlineData(20.5, "21")]
    public void TestBoundsRoundHalfAwayFromZeroLikeTheDisplayedValue(double value, string text)
    {
        Assert.Equal(text, HeadlineFormatter.Bound(value, Pt));
    }

    [Fact]
    public void TestSignedPointsUseATrueMinus()
    {
        Assert.Equal("+4", HeadlineFormatter.SignedPoints(4, Pt));
        Assert.Equal("−5", HeadlineFormatter.SignedPoints(-5, Pt));
        Assert.Equal("0", HeadlineFormatter.SignedPoints(0, Pt));
    }

    // ---------- the 7-day line ------------------------------------------------------------------------------------

    [Fact]
    public void TestAWorseMoveHasAnUpArrowAndSaysWorse()
    {
        var delta = HeadlineFormatter.Delta(Headline(delta: Worse(4)), RiskIndexProvenance.Official, 3, PtText, Pt)!;

        Assert.Equal("+4 (pior) em 7 dias", delta.Text);
        Assert.Equal(("worse", MaterialIconKind.ArrowTopRightThick), (delta.CssClass, delta.Glyph!.Value));
    }

    [Fact]
    public void TestABetterMoveHasADownArrowAndSaysBetter()
    {
        var delta = HeadlineFormatter.Delta(
            Headline(delta: new RiskIndexDeltaDto(-5, RiskIndexDeltaDirection.Better, new DateOnly(2026, 9, 28), null, null)),
            RiskIndexProvenance.Official, 3, PtText, Pt)!;

        Assert.Equal("−5 (melhor) em 7 dias", delta.Text);
        Assert.Equal(("better", MaterialIconKind.ArrowBottomRightThick), (delta.CssClass, delta.Glyph!.Value));
    }

    [Fact]
    public void TestASmallMoveSaysStableAndNamesTheThresholdWhenKnown()
    {
        var headline = Headline(delta: new RiskIndexDeltaDto(2, RiskIndexDeltaDirection.Stable, new DateOnly(2026, 9, 28),
            RiskIndexDeltaSuppressedReason.BelowThreshold, null));

        Assert.Equal("estável em 7 dias (variação menor que 3)",
            HeadlineFormatter.Delta(headline, RiskIndexProvenance.Official, 3, PtText, Pt)!.Text);
        Assert.Equal("estável em 7 dias (variação abaixo do limiar da seta)",
            HeadlineFormatter.Delta(headline, RiskIndexProvenance.Official, null, PtText, Pt)!.Text);
        Assert.Equal("neutral", HeadlineFormatter.Delta(headline, RiskIndexProvenance.Official, 3, PtText, Pt)!.CssClass);
    }

    [Fact]
    public void TestNoComparisonSaysWhyAndHasNoArrow()
    {
        var discontinuity = HeadlineFormatter.Delta(Headline(delta: new RiskIndexDeltaDto(null, null, new DateOnly(2026, 9, 28),
            RiskIndexDeltaSuppressedReason.Discontinuity, new DateOnly(2026, 10, 2))), RiskIndexProvenance.Official, 3, PtText, Pt)!;
        var missing = HeadlineFormatter.Delta(Headline(delta: new RiskIndexDeltaDto(null, null, null,
            RiskIndexDeltaSuppressedReason.MissingBaseline, null)), RiskIndexProvenance.Official, 3, PtText, Pt)!;

        Assert.Equal("sem comparação: descontinuidade em 02/10/2026", discontinuity.Text);
        Assert.Equal("sem comparação: sem snapshot entre t−10 e t−7", missing.Text);
        Assert.Null(discontinuity.Glyph);
        Assert.Null(missing.Glyph);
    }

    [Fact]
    public void TestPartialIndicativeEmptyAndUncomparedValuesHaveNoDeltaLine()
    {
        Assert.Null(HeadlineFormatter.Delta(Headline(delta: Worse(4)), RiskIndexProvenance.PartialRestricted, 3, PtText, Pt));
        Assert.Null(HeadlineFormatter.Delta(Headline(delta: Worse(4), state: RiskIndexDisplayState.Indicative),
            RiskIndexProvenance.Official, 3, PtText, Pt));
        Assert.Null(HeadlineFormatter.Delta(Headline(delta: Worse(4), state: RiskIndexDisplayState.NoData),
            RiskIndexProvenance.Official, 3, PtText, Pt));
        Assert.Null(HeadlineFormatter.Delta(Headline(delta: null), RiskIndexProvenance.OfficialDerived, 3, PtText, Pt));
    }

    // ---------- quality, interval, indicative, branch, floor --------------------------------------------------------

    [Fact]
    public void TestQualityAndCoverageReadAsTheSpecWritesThem()
    {
        var quality = HeadlineFormatter.Quality(new RiskIndexQualitySummaryDto(0.92, 0.99, 0.91, "high", []), PtText, Pt);

        Assert.Equal(("Alta", "0,91", "Cobertura 92%", "Frescor 99%"),
            (quality.BandText, quality.ValueText, quality.CoverageText, quality.FreshnessText));
        Assert.Equal("Média", HeadlineFormatter.Quality(new RiskIndexQualitySummaryDto(0.6, 0.6, 0.6, "medium", []), PtText, Pt).BandText);
        Assert.Equal("Low", HeadlineFormatter.Quality(new RiskIndexQualitySummaryDto(0.2, 0.2, 0.2, "low", []),
            RiskIndexTestText.English, En).BandText);
    }

    [Fact]
    public void TestTheIntervalIsOnTheCardOnlyWhenTheServerSaysSo()
    {
        var onCard = HeadlineFormatter.Interval(Headline(state: RiskIndexDisplayState.PointWithInterval, intervalOnCard: true),
            PtText, Pt)!;
        var inDetail = HeadlineFormatter.Interval(Headline(), PtText, Pt)!;

        Assert.Equal((true, "Intervalo [53–79]"), (onCard.OnCard, onCard.Text));
        Assert.Equal((false, "Intervalo de ignorância: no detalhe", "Intervalo [53–79]"),
            (inDetail.OnCard, inDetail.Text, inDetail.Hint));
        Assert.Null(HeadlineFormatter.Interval(Headline(low: null, high: null), PtText, Pt));
        Assert.Null(HeadlineFormatter.Interval(Headline(state: RiskIndexDisplayState.Indicative), PtText, Pt));
    }

    [Fact]
    public void TestAnIndicativeValueSaysItIsNotClassifiedAndWhy()
    {
        var indicative = HeadlineFormatter.Indicative(
            Headline(displayed: 47, band: null, state: RiskIndexDisplayState.Indicative, low: 20.4, high: 80.6), 0.31, 0.40,
            PtText, Pt)!.Value;

        Assert.Equal("ICR indicativo: 47 · intervalo [20–81]", indicative.Text);
        Assert.Equal("cobertura de 31%, abaixo de 40%: o valor não é classificado", indicative.Reason);
        Assert.Null(HeadlineFormatter.Indicative(Headline(), 0.92, 0.40, PtText, Pt));
    }

    [Fact]
    public void TestTheBranchSaysWhichRuleSetTheHeadline()
    {
        Assert.Equal("Manchete pela média ponderada (piso τ_G inativo)", HeadlineFormatter.Branch(Headline(), PtText, Pt));
        Assert.Equal("Manchete sustentada por EXP (piso τ_G × C_k)",
            HeadlineFormatter.Branch(Headline(meanBranch: false, heldBy: RiskIndexCategory.Exp), PtText, Pt));
        Assert.Null(HeadlineFormatter.Branch(Headline(state: RiskIndexDisplayState.NoData), PtText, Pt));
    }

    [Fact]
    public void TestTheUnflooredValueShowsOnlyInFullModeWhileAFloorHolds()
    {
        var floor = new RiskIndexFloorReasonDto("A",
            new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Scenario, "1"), "R1", null), "2");
        var floored = Headline(displayed: 70, band: 2, unfloored: 63.6, floors: [floor]);

        Assert.Equal("piso: Portão A — R1 (flag 2) · sem piso 64 (Médio)",
            HeadlineFormatter.Unfloored(floored, RiskIndexMode.Full, ThreeBands, PtText, Pt, Pt));
        Assert.Null(HeadlineFormatter.Unfloored(floored, RiskIndexMode.Interim, ThreeBands, PtText, Pt, Pt));
        Assert.Null(HeadlineFormatter.Unfloored(Headline(), RiskIndexMode.Full, ThreeBands, PtText, Pt, Pt));
    }

    // ---------- seals and chips -----------------------------------------------------------------------------------

    [Fact]
    public void TestSealsCarryTheirCountAndAnUnknownSealReadsAsItsKey()
    {
        var seals = HeadlineFormatter.Seals(
        [
            new RiskIndexSealDto("scenarios_above_appetite", 2, NoArgs, "seal=above_appetite"),
            new RiskIndexSealDto("appetite_not_configured", null, NoArgs, null),
            new RiskIndexSealDto("future_seal", 1, NoArgs, null)
        ], PtText, Pt);

        Assert.Equal(["2 cenário(s) acima do teto de apetite sem aceite válido", "Teto de apetite não configurado", "future_seal"],
            seals);
    }

    [Fact]
    public void TestChipsShownElsewhereAreLeftOutAndUnavailableCategoriesAreNamed()
    {
        var chips = HeadlineFormatter.Chips(
        [
            new RiskIndexChipDto("headline_floor", NoArgs, null, null),
            new RiskIndexChipDto("data_quality", NoArgs, null, null),
            new RiskIndexChipDto("closure_through_eav", NoArgs, null, null),
            new RiskIndexChipDto("no_critical_process", NoArgs, null, null),
            new RiskIndexChipDto("partial_visibility", NoArgs, null, null)
        ],
        [
            Category(RiskIndexCategory.Ctl, RiskIndexCategoryState.Unavailable, 0.15),
            Category(RiskIndexCategory.Ame, RiskIndexCategoryState.Unavailable, 0.15),
            Category(RiskIndexCategory.Ter, RiskIndexCategoryState.Unavailable, 0),
            Category(RiskIndexCategory.Exp, RiskIndexCategoryState.Available, 0.3)
        ], PtText, Pt);

        Assert.Equal(["no_critical_process", "partial_visibility", "category_unavailable"], chips.Select(c => c.Key));
        Assert.Equal(["Nenhum processo declarado crítico", "Parcial — visibilidade restrita", "AME e CTL indisponíveis"],
            chips.Select(c => c.Text));
    }

    [Fact]
    public void TestOneUnavailableCategoryIsSingularAndAnUnknownChipReadsAsItsKey()
    {
        var chips = HeadlineFormatter.Chips([new RiskIndexChipDto("future_chip", NoArgs, null, null)],
            [Category(RiskIndexCategory.Ame, RiskIndexCategoryState.Unavailable, 0.2)], PtText, Pt);

        Assert.Equal(["future_chip", "AME indisponível"], chips.Select(c => c.Text));
        Assert.Equal("AME, CTL e GOV", HeadlineFormatter.JoinList(["AME", "CTL", "GOV"], PtText));
        Assert.Equal("AME, CTL and GOV", HeadlineFormatter.JoinList(["AME", "CTL", "GOV"], RiskIndexTestText.English));
    }

    private static RiskIndexCategoryDto Category(RiskIndexCategory category, RiskIndexCategoryState state, double weight) =>
        new(category, state, null, null, null, weight, 0, null, null, null, null, null, null, null, []);

    // ---------- accessibility --------------------------------------------------------------------------------------

    [Fact]
    public void TestTheCardsAccessibleNameIsTheSpecsSentence()
    {
        Assert.Equal("Índice consolidado de risco 64 de 100, faixa Médio, piorou 4 pontos em 7 dias, cobertura 92 %",
            HeadlineFormatter.AccessibleName(Headline(delta: Worse(4)), ThreeBands, 0.92, RiskIndexProvenance.Official, PtText, Pt, Pt));
        Assert.Equal("Consolidated risk index 64 of 100, band Medium, worsened 4 points in 7 days, coverage 92 %",
            HeadlineFormatter.AccessibleName(Headline(delta: Worse(4)), ThreeBands, 0.92, RiskIndexProvenance.Official,
                RiskIndexTestText.English, En, En));
    }

    [Fact]
    public void TestTheAccessibleNameLeavesOutAnArrowTheCardDoesNotShow()
    {
        Assert.Equal("Índice consolidado de risco 64 de 100, faixa Médio, cobertura 92 %",
            HeadlineFormatter.AccessibleName(Headline(delta: Worse(4)), ThreeBands, 0.92, RiskIndexProvenance.PartialRestricted,
                PtText, Pt, Pt));
        Assert.Equal("Índice consolidado de risco 64 de 100, faixa Médio, estável em 7 dias, cobertura 92 %",
            HeadlineFormatter.AccessibleName(Headline(delta: new RiskIndexDeltaDto(1, RiskIndexDeltaDirection.Stable, null,
                RiskIndexDeltaSuppressedReason.BelowThreshold, null)), ThreeBands, 0.92, RiskIndexProvenance.Official, PtText, Pt, Pt));
    }

    [Fact]
    public void TestIndicativeAndEmptyCardsHaveTheirOwnAccessibleName()
    {
        Assert.Equal("Índice consolidado de risco indicativo 47 de 100, não classificado, cobertura 31 %",
            HeadlineFormatter.AccessibleName(Headline(displayed: 47, band: null, state: RiskIndexDisplayState.Indicative),
                ThreeBands, 0.31, RiskIndexProvenance.Official, PtText, Pt, Pt));
        Assert.Equal("Índice consolidado de risco: sem dados",
            HeadlineFormatter.AccessibleName(Headline(displayed: null, band: null, state: RiskIndexDisplayState.NoData),
                ThreeBands, 0, RiskIndexProvenance.Official, PtText, Pt, Pt));
        Assert.Equal("Índice consolidado de risco: sem dados",
            HeadlineFormatter.AccessibleName(null, ThreeBands, 0, RiskIndexProvenance.Official, PtText, Pt, Pt));
    }

    // ---------- every computed key exists ----------------------------------------------------------------------------

    [Fact]
    public void TestEveryKeyTheFormattersBuildAtRunTimeIsInAllThreeResources()
    {
        var keys = HeadlineFormatter.KnownSealKeys.Select(k => "RiskSeal_" + k + "MSG")
            .Concat(HeadlineFormatter.KnownChipKeys.Where(k => k != HeadlineFormatter.CategoryUnavailableChip)
                .Select(k => "RiskChip_" + k + "MSG"))
            .Concat(["RiskChip_category_unavailableMSG", "RiskChip_categories_unavailableMSG"])
            .Concat(new[] { "high", "medium", "low" }.Select(k => "RiskQuality_" + k))
            .Concat(Enum.GetValues<RiskIndexProvenance>().Select(RiskOverviewTopBar.ProvenanceKey))
            .Concat(Enum.GetValues<RiskIndexMode>().SelectMany(m => new[] { RiskOverviewTopBar.ModeKey(m), RiskOverviewTopBar.ModeHintKey(m) }))
            .Concat(Enum.GetValues<RiskIndexProfileStatus>().Select(RiskOverviewTopBar.ProfileStatusKey))
            .Concat(RiskApiCodes.ReadPath.Select(RiskApiErrorText.ResourceKey))
            .Concat(["RiskState_PartialCauseEav", "RiskState_PartialCauseEither"])
            .ToList();

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var declared = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !declared.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }

    /// <summary>The resource prefixes this module introduced (S40 §10.5).</summary>
    private static readonly string[] ModulePrefixes =
    [
        "RiskOverview", "RiskState_", "RiskSeal_", "RiskChip_", "RiskProfile", "RiskApi_", "RiskProvenance_", "RiskMode_",
        "RiskQuality", "NavRiskOverview", "RiskGovernance", "RiskVisibility", "RiskMonetary", "RiskTopRisks", "RiskSource_"
    ];

    [Fact]
    public void TestEveryTemplateHasTheSamePlaceholdersInEveryLanguage()
    {
        var neutral = RiskIndexTestText.Load("Localization.resx");
        var mismatched = new List<string>();

        foreach (var file in RiskIndexTestText.ResourceFiles.Skip(1))
        foreach (var (key, template) in RiskIndexTestText.Load(file).Where(kv => ModulePrefixes.Any(p =>
                     kv.Key.StartsWith(p, StringComparison.Ordinal))))
        {
            if (!neutral.TryGetValue(key, out var reference)) continue;
            if (!Placeholders(reference).SetEquals(Placeholders(template))) mismatched.Add($"{file}: {key}");
        }

        Assert.True(mismatched.Count == 0, "These translations drop or add a placeholder: " + string.Join(", ", mismatched));

        static HashSet<string> Placeholders(string text) =>
            System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();
    }

    [Fact]
    public void TestEveryFormatTemplateFormatsInEveryLanguage()
    {
        foreach (var file in RiskIndexTestText.ResourceFiles)
        foreach (var (key, template) in RiskIndexTestText.Load(file).Where(kv => ModulePrefixes.Any(p =>
                     kv.Key.StartsWith(p, StringComparison.Ordinal))))
        {
            var exception = Record.Exception(() => string.Format(Pt, template, "a", "b", "c", "d"));
            Assert.True(exception is null, $"{file}: {key} is not a valid format string ({exception?.Message})");
        }
    }
}
