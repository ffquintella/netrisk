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
/// T255 (S40 §5.7, S39 §9.4–§9.5): "O que mudou" lists the attribution in whole points ordered by magnitude — the 100
/// largest lines and the rest summed in one — names objects under the §11.3 redaction, and says how much of a period that
/// crosses a profile change is the configuration's.
/// </summary>
[TestSubject(typeof(ChangesFormatter))]
public class ChangesFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly DateOnly From = new(2026, 10, 5), To = new(2026, 10, 6);

    private static RiskIndexObjectLabelDto Label(string reference, string label, string? permission = null) =>
        new(new RiskIndexObjectRef(RiskIndexObjectKind.Scenario, reference), label, permission);

    private static RiskIndexAttributionRowDto Row(RiskIndexAttributionEffect effect, double points, double? percent,
        RiskIndexObjectLabelDto? obj = null, RiskIndexCategory? category = RiskIndexCategory.Reg, bool reattribution = false,
        string? author = null) =>
        new(effect, category, obj, "RiskEffect_" + effect, points, (int)Math.Round(points, MidpointRounding.AwayFromZero), percent,
            author, reattribution);

    private static RiskIndexChangesDto Changes(IReadOnlyList<RiskIndexAttributionRowDto> rows, double residual = 0,
        IReadOnlyList<DateOnly>? markers = null) =>
        new(new RiskIndexNodeRef("org", "Org", "organization"), From, To, -3.6, -4, rows, [], residual, markers ?? []);

    [Fact]
    public void TestTheLinesAreOrderedByMagnitudeInWholePoints()
    {
        var view = ChangesFormatter.Format(Changes(
        [
            Row(RiskIndexAttributionEffect.Risk, -0.6, 17, Label("2", "APP-03")),
            Row(RiskIndexAttributionEffect.Risk, -2.8, 78, Label("1", "R1 — vazamento")),
            Row(RiskIndexAttributionEffect.Coverage, 0.2, -5.5, category: null)
        ]), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(["R1 — vazamento", "APP-03", "categoria que entrou ou saiu"], view.Rows.Select(r => r.Origin));
        Assert.Equal(["−3", "−1", "0"], view.Rows.Select(r => r.Points));
        Assert.Equal(["78%", "17%", "−6%"], view.Rows.Select(r => r.Percent));
        Assert.Equal(["Risco", "Risco", "Cobertura"], view.Rows.Select(r => r.Effect));
        Assert.Equal(["REG", "REG", "—"], view.Rows.Select(r => r.Category));
        Assert.Null(view.Rest);
        Assert.Equal("05/10/2026 → 06/10/2026 · ICR −4 · ICR* −3,6", view.Period);
        Assert.True(view.Rows[0].HasObject);
        Assert.False(view.Rows[2].HasObject);
    }

    /// <summary>§5.7: the 100 largest lines, and the rest summed in one line with their count.</summary>
    [Fact]
    public void TestBeyondTheHundredLargestTheRestIsSummed()
    {
        var rows = Enumerable.Range(1, 103)
            .Select(i => Row(RiskIndexAttributionEffect.Risk, -i / 10.0, -i / 10.0, Label(i.ToString(CultureInfo.InvariantCulture), "o" + i)))
            .ToList();

        var view = ChangesFormatter.Format(Changes(rows), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(ChangesFormatter.MaxRows, view.Rows.Count);
        Assert.Equal("o103", view.Rows[0].Origin);
        Assert.NotNull(view.Rest);
        Assert.Equal("demais 3 linhas, somadas", view.Rest!.Origin);
        Assert.Equal("Demais", view.Rest.Effect);
        Assert.Equal("−1", view.Rest.Points); // 0.3 + 0.2 + 0.1 = 0.6
        Assert.Equal("−1%", view.Rest.Percent);
        Assert.False(view.Rest.HasObject);
    }

    [Fact]
    public void TestTheRestHasNoShareWhenALineHasNone()
    {
        var rows = Enumerable.Range(1, 101)
            .Select(i => Row(RiskIndexAttributionEffect.Risk, 1, i == 1 ? null : 1.0, Label(i.ToString(CultureInfo.InvariantCulture), "o")))
            .ToList();
        rows[0] = rows[0] with { Points = 0.01, PointsRounded = 0 };

        var view = ChangesFormatter.Format(Changes(rows), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("—", view.Rest!.Percent);
    }

    /// <summary>S39 §9.5: a period across a profile change shows the version's jump as the configuration effect.</summary>
    [Fact]
    public void TestAPeriodAcrossAProfileChangeShowsTheConfigurationShare()
    {
        var crossing = Changes([Row(RiskIndexAttributionEffect.Configuration, -2.4, 66.7, category: null)],
            markers: [new DateOnly(2026, 10, 6)]);

        var note = ChangesFormatter.Format(crossing, RiskIndexTestText.Portuguese, PtBr).ConfigurationNote;

        Assert.Equal("O período atravessa troca de perfil (06/10/2026): o salto de versão entra como efeito de configuração, " +
                     "−2 ponto(s) (67% da variação).", note);
        Assert.Null(ChangesFormatter.Format(Changes([]), RiskIndexTestText.Portuguese, PtBr).ConfigurationNote);

        var noConfigurationLine = Changes([], markers: [new DateOnly(2026, 10, 6)]);
        Assert.Contains("0 ponto(s) (0% da variação)",
            ChangesFormatter.ConfigurationNote(noConfigurationLine, RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestARedactedObjectReattributionAndAuthorAreNamed()
    {
        var view = ChangesFormatter.Format(Changes(
        [
            Row(RiskIndexAttributionEffect.Restructuring, -1, 25, Label("9", "Scenario #9", "riskmanagement"), reattribution: true),
            Row(RiskIndexAttributionEffect.Context, 0.6, -15, Label("3", "R3"), author: "M. Souza")
        ]), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Cenário #9 — entrou ou saiu por reatribuição", view.Rows[0].Origin);
        Assert.Equal("Requer a permissão riskmanagement", view.Rows[0].OriginHint);
        Assert.Equal("Reestruturação", view.Rows[0].Effect);
        Assert.Equal("R3 (por M. Souza)", view.Rows[1].Origin);
        Assert.Null(view.Rows[1].OriginHint);
    }

    [Fact]
    public void TestAVisibleResidualAndTheEventsAreListed()
    {
        var changes = Changes([], residual: 0.7) with
        {
            Events = [new RiskIndexChangeEventDto("source_stale", Label("4", "BKP-04"), new Dictionary<string, string>())]
        };

        var view = ChangesFormatter.Format(changes, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Resíduo numérico da atribuição: 0,7 ponto(s).", view.ResidualNote);
        Assert.Equal(["source_stale: BKP-04"], view.Events);
        Assert.Null(ChangesFormatter.Format(Changes([], residual: 0.2), RiskIndexTestText.Portuguese, PtBr).ResidualNote);
    }

    [Fact]
    public void TestThePeriodsAskForTheRightStart()
    {
        Assert.Null(ChangesFormatter.From(ChangesPeriod.PreviousSnapshot, To));
        Assert.Equal(new DateOnly(2026, 9, 29), ChangesFormatter.From(ChangesPeriod.Days7, To));
        Assert.Equal(new DateOnly(2026, 9, 6), ChangesFormatter.From(ChangesPeriod.Days30, To));
    }

    [Fact]
    public void TestEveryKeyTheChangesComputeExistsInTheThreeResourceFiles()
    {
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = ChangesFormatter.ComputedKeys().Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}
