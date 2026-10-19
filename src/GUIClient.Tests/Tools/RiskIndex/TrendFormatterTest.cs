using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T252 (S40 §5.3, §10.4, §10.8): the trend's pure half against the real resource text — windows per provenance, the line
/// through gaps, shapes of indicative days, band areas per profile stretch, markers and their hints, and the table that
/// carries the same information as the chart.
/// </summary>
[TestSubject(typeof(TrendFormatter))]
public class TrendFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly DateOnly D = new(2026, 10, 5);

    private static readonly IReadOnlyList<RiskIndexBandDto> ThreeBands =
    [
        new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Médio", "Medium", 1, 3, 31), new("b2", "Alto", "High", 2, 5, 70)
    ];

    private static RiskIndexTrendPointDto Point(DateOnly date, int? displayed, RiskIndexDisplayState state = RiskIndexDisplayState.Point,
        bool gap = false, double? low = null, double? high = null, IReadOnlyList<RiskIndexCategory>? unavailable = null,
        int? delta = null, int? floored = null, IReadOnlyList<RiskIndexAttributionRowDto>? movers = null) =>
        new(date, displayed, displayed + 0.2, floored ?? displayed, displayed - 1.0, state, low, high, unavailable ?? [], gap, false,
            delta, movers ?? []);

    private static RiskIndexTrendDto Trend(IReadOnlyList<RiskIndexTrendPointDto> points,
        IReadOnlyList<RiskIndexBandSegmentDto>? segments = null, IReadOnlyList<RiskIndexMarkerDto>? markers = null,
        IReadOnlyList<RiskIndexAnnotationDto>? annotations = null, IReadOnlyList<RiskIndexDateRange>? floors = null) =>
        new(new RiskIndexNodeRef("org", "Organization", "organization"), 90, RiskIndexCategory.Icr, points,
            segments ?? [new RiskIndexBandSegmentDto(D.AddDays(-2), D, 3, ThreeBands)], markers ?? [], annotations ?? [],
            floors ?? [], [], null, null);

    [Theory]
    [InlineData(RiskIndexProvenance.Official, true, true, true)]
    [InlineData(RiskIndexProvenance.OfficialDerived, true, true, false)]
    [InlineData(RiskIndexProvenance.PartialRestricted, false, false, false)]
    [InlineData(RiskIndexProvenance.UnofficialPreview, false, false, false)]
    public void TestTheWindowsFollowTheProvenance(RiskIndexProvenance provenance, bool has30, bool has90, bool has365)
    {
        Assert.Equal(has30, TrendFormatter.AllowsWindow(30, provenance));
        Assert.Equal(has90, TrendFormatter.AllowsWindow(90, provenance));
        Assert.Equal(has365, TrendFormatter.AllowsWindow(365, provenance));
        Assert.False(TrendFormatter.AllowsWindow(60, provenance));
        Assert.Equal(has30, TrendFormatter.HasTrend(provenance));
    }

    [Fact]
    public void TestADerivedNodeOffers365DisabledWithItsReasonAndFallsBackTo90()
    {
        var options = TrendFormatter.WindowOptions(RiskIndexProvenance.OfficialDerived, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(new[] { "30 dias", "90 dias", "365 dias" }, options.Select(o => o.Label));
        Assert.False(options[2].IsEnabled);
        Assert.Equal("Disponível só para nós pré-calculados", options[2].DisabledReason);
        Assert.Null(options[0].DisabledReason);
        Assert.Equal(90, TrendFormatter.EffectiveWindow(365, RiskIndexProvenance.OfficialDerived));
        Assert.Equal(365, TrendFormatter.EffectiveWindow(365, RiskIndexProvenance.Official));
    }

    [Fact]
    public void TestTheSeriesAreTheHeadlineThenTheCarriedCategoriesInTheFixedOrder()
    {
        IReadOnlyList<RiskIndexCategoryDto> categories =
        [
            Category(RiskIndexCategory.Gov, RiskIndexCategoryState.Available), Category(RiskIndexCategory.Reg, RiskIndexCategoryState.Unavailable),
            Category(RiskIndexCategory.Ame, RiskIndexCategoryState.NotApplicable), Category(RiskIndexCategory.Ter, RiskIndexCategoryState.Disabled),
            Category(RiskIndexCategory.Exp, RiskIndexCategoryState.Available)
        ];

        var options = TrendFormatter.SeriesOptions(categories, RiskIndexTestText.Portuguese);

        Assert.Equal(new[] { RiskIndexCategory.Icr, RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Gov },
            options.Select(o => o.Series));
        Assert.Equal(new[] { "ICR", "REG", "EXP", "GOV" }, options.Select(o => o.Label));
    }

    internal static RiskIndexCategoryDto Category(RiskIndexCategory category, RiskIndexCategoryState state, double? value = 60,
        string? band = "b1", double weight = 0.3, double effective = 0.3, double? points = 18, RiskIndexHeldByDto? heldBy = null,
        string? reason = null, string? requires = null, IReadOnlyList<RiskIndexSealDto>? seals = null) =>
        new(category, state, state == RiskIndexCategoryState.Available ? value : null,
            state == RiskIndexCategoryState.Available && value is { } v ? (int)Math.Round(v, MidpointRounding.AwayFromZero) : null,
            state == RiskIndexCategoryState.Available ? band : null, weight, effective, points, heldBy, 0.83, 1, 0.5, reason, requires,
            seals ?? []);

    /// <summary>S39 §9.3: the line repeats the value through a gap (only the point changes) and breaks only without data.</summary>
    [Fact]
    public void TestTheLineIsContinuousThroughAGapAndBrokenOnlyWithoutData()
    {
        var trend = Trend([
            Point(D.AddDays(-2), 60), Point(D.AddDays(-1), 60, RiskIndexDisplayState.NoData, gap: true),
            Point(D, null, RiskIndexDisplayState.NoData)
        ]);

        Assert.Equal(new double?[] { 60.2, 60.2, null }, trend.Points.Select(TrendFormatter.LineValue));
        Assert.Equal(new[] { (D.AddDays(-1), 60.2) }, TrendFormatter.Gaps(trend));
    }

    [Fact]
    public void TestIndicativeDaysAndUnavailableCategoriesCarryAWhisker()
    {
        var trend = Trend([
            Point(D.AddDays(-2), 47, RiskIndexDisplayState.Indicative, low: 20, high: 81),
            Point(D.AddDays(-1), 55, RiskIndexDisplayState.PointWithInterval, low: 40, high: 71, unavailable: [RiskIndexCategory.Ctl, RiskIndexCategory.Ame]),
            Point(D, 58, RiskIndexDisplayState.PointWithInterval, low: 50, high: 66)
        ]);

        Assert.Equal(new[] { D.AddDays(-2) }, TrendFormatter.Indicative(trend).Select(i => i.Date));
        Assert.Equal(new[] { new TrendWhisker(D.AddDays(-2), 20, 81), new TrendWhisker(D.AddDays(-1), 40, 71) }, TrendFormatter.Whiskers(trend));
        Assert.Equal("indicativo, sem faixa · intervalo [20–81]",
            TrendFormatter.StateText(trend.Points[0], RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("intervalo [40–71]: AME e CTL indisponíveis",
            TrendFormatter.StateText(trend.Points[1], RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>
    /// §5.3: one area per band per profile stretch — when the bands change at a marker, so do the areas — labelled with the
    /// band in the interface's language and its range; a stretch runs to the next one, so a gap between them stays shaded.
    /// </summary>
    [Fact]
    public void TestTheBandAreasFollowEachProfileStretch()
    {
        IReadOnlyList<RiskIndexBandDto> twoBands = [new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Alto", "High", 1, 5, 50)];
        var trend = Trend([Point(D, 60)], segments:
        [
            new RiskIndexBandSegmentDto(D.AddDays(-9), D.AddDays(-5), 2, ThreeBands),
            new RiskIndexBandSegmentDto(D.AddDays(-3), D, 3, twoBands)
        ]);

        var areas = TrendFormatter.BandAreas(trend, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Equal(5, areas.Count);
        Assert.Equal((D.AddDays(-9), D.AddDays(-3), 0, 31), (areas[0].From, areas[0].ToExclusive, areas[0].Lower, areas[0].Upper));
        Assert.Equal(new[] { "Baixo 0–30", "Médio 31–69", "Alto ≥ 70", "Baixo 0–49", "Alto ≥ 50" }, areas.Select(a => a.Label));
        Assert.Equal((D.AddDays(-3), D.AddDays(1), 50, 100), (areas[4].From, areas[4].ToExclusive, areas[4].Lower, areas[4].Upper));
        Assert.Equal("High ≥ 70", TrendFormatter.BandAreaLabel(ThreeBands, 2, RiskIndexTestText.English, CultureInfo.InvariantCulture,
            CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void TestAMarkerNamesTheVersionsTheDeltaAndTheRestart()
    {
        var marker = new RiskIndexMarkerDto(D, RiskIndexMarkerKind.Profile,
            new Dictionary<string, string> { ["from_version"] = "2", ["to_version"] = "3" }, -3.4);
        var appetite = new RiskIndexMarkerDto(D, RiskIndexMarkerKind.Appetite, new Dictionary<string, string>(), null);

        var marks = TrendFormatter.Markers(Trend([Point(D, 60)], markers: [marker, appetite]), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("05/10/2026 — Perfil v2 → v3 (rebaseline) · Δ da versão −3,4 · Teto de apetite alterado · seta e EWMA reiniciam",
            Assert.Single(marks).Text);
        Assert.Equal("Perfil alterado", TrendFormatter.MarkerText(marker with { Args = new Dictionary<string, string>(), VersionDelta = null },
            RiskIndexTestText.Portuguese, PtBr));
    }

    [Theory]
    [InlineData(RiskIndexMarkerKind.Mode)]
    [InlineData(RiskIndexMarkerKind.Appetite)]
    [InlineData(RiskIndexMarkerKind.EntityMap)]
    [InlineData(RiskIndexMarkerKind.ExternalInput)]
    [InlineData(RiskIndexMarkerKind.Profile)]
    public void TestEveryMarkerKindReadsAsASentenceInBothLanguages(RiskIndexMarkerKind kind)
    {
        var marker = new RiskIndexMarkerDto(D, kind, new Dictionary<string, string>(), null);

        Assert.DoesNotContain("RiskMarker_", TrendFormatter.MarkerText(marker, RiskIndexTestText.Portuguese, PtBr));
        Assert.DoesNotContain("RiskMarker_", TrendFormatter.MarkerText(marker, RiskIndexTestText.English, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TestAVendorAnnotationIsADatedSentenceAtTheFoot()
    {
        var annotation = new RiskIndexAnnotationDto(1, D, 1, "Tenable", "VPR v2", "new model", "ana");

        var mark = Assert.Single(TrendFormatter.Annotations(Trend([Point(D, 60)], annotations: [annotation]), RiskIndexTestText.Portuguese, PtBr));

        Assert.Equal("05/10/2026 — Modelo do fornecedor (Tenable): VPR v2 · new model", mark.Text);
    }

    /// <summary>§5.3 "Dica do ponto": value, floored value when different, Δ, the movers, never the band of an indicative day.</summary>
    [Fact]
    public void TestThePointHintCarriesValueDeltaFloorAndMovers()
    {
        var mover = new RiskIndexAttributionRowDto(RiskIndexAttributionEffect.Risk, RiskIndexCategory.Exp,
            new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Host, "7"), "srv-07", null), "RiskEffect_Risk", 2.4, 2,
            60, null, false);
        var point = Point(D, 64, delta: 3, floored: 70, movers: [mover]);

        var hint = TrendFormatter.PointTooltip(point, [], RiskIndexTestText.Portuguese, PtBr).Split(Environment.NewLine);

        Assert.Equal(new[] { "05/10/2026 — 64", "ICR com piso: 70", "Δ +3 em relação ao dia anterior", "• srv-07 +2" }, hint);
        var gap = TrendFormatter.PointTooltip(Point(D, 64, RiskIndexDisplayState.NoData, gap: true), [], RiskIndexTestText.Portuguese, PtBr);
        Assert.Contains("lacuna: sem snapshot neste dia", gap);
        var indicative = TrendFormatter.PointTooltip(Point(D, 47, RiskIndexDisplayState.Indicative, low: 20, high: 81), [],
            RiskIndexTestText.Portuguese, PtBr);
        Assert.DoesNotContain("Médio", indicative);
        Assert.Contains("indicativo, sem faixa", indicative);
    }

    /// <summary>§10.8: the table repeats every hint — newest first, "—" where there is no value.</summary>
    [Fact]
    public void TestTheTableRepeatsWhatTheHintsSay()
    {
        var marker = new RiskIndexMarkerDto(D, RiskIndexMarkerKind.Mode, new Dictionary<string, string>(), null);
        var trend = Trend([
            Point(D.AddDays(-1), 60, RiskIndexDisplayState.NoData, gap: true), Point(D, 47, RiskIndexDisplayState.Indicative, low: 20, high: 81, delta: -13)
        ], markers: [marker]);

        var rows = TrendFormatter.TableRows(trend, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(new[] { D, D.AddDays(-1) }, rows.Select(r => r.Date));
        Assert.Equal(("47", "−13", "Indicativo", "indicativo, sem faixa · intervalo [20–81]"),
            (rows[0].Unfloored, rows[0].Delta, rows[0].State, rows[0].Interval));
        Assert.Contains("Modo alterado", rows[0].Marker);
        Assert.Equal(("Lacuna (valor repetido)", "lacuna: sem snapshot neste dia", "—"), (rows[1].State, rows[1].Gap, rows[1].Delta));
        Assert.Equal("46,0", rows[0].Ewma);
    }

    [Fact]
    public void TestTheFloorRangesEndAfterTheirLastDay()
    {
        var trend = Trend([Point(D, 60)], floors: [new RiskIndexDateRange(D.AddDays(-3), D.AddDays(-1))]);

        Assert.Equal(new[] { (D.AddDays(-3), D) }, TrendFormatter.FloorRanges(trend));
    }

    [Theory]
    [InlineData(0.84, "+0,8")]
    [InlineData(-3.4, "−3,4")]
    [InlineData(0, "0,0")]
    public void TestASignedDeltaUsesATrueMinus(double value, string expected) =>
        Assert.Equal(expected, TrendFormatter.Signed(value, PtBr));
}
