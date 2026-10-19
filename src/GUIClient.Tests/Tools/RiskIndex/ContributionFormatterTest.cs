using System;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T255 (S40 §5.8, S39 §4.8, R-1): the contributions read as points and share of the category, the branch ("piso:
/// sustentado"), the next object to hold the floor, the change if the reading cleared as a whole number, and the read-only
/// decision state — "Requer a permissão riskmanagement" for a scenario the caller may not read (§11.3).
/// </summary>
[TestSubject(typeof(ContributionFormatter))]
public class ContributionFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static RiskIndexObjectLabelDto Label(RiskIndexObjectKind kind, string reference, string label, string? permission = null) =>
        new(new RiskIndexObjectRef(kind, reference), label, permission);

    private static RiskIndexContributionRowDto Row(RiskIndexObjectLabelDto obj, double points, double share,
        RiskIndexContributionBranch branch = RiskIndexContributionBranch.HeldBy, double? sensitivity = -4.4,
        RiskIndexObjectLabelDto? next = null, RiskIndexDecisionStateDto? decision = null,
        RiskIndexCategory category = RiskIndexCategory.Reg) =>
        new(obj, category, points, share, branch, sensitivity, next, decision);

    [Fact]
    public void TestTheRowsAreThePointsLargestFirstWithBranchAndSensitivity()
    {
        var contributions = new RiskIndexContributionsDto(new RiskIndexNodeRef("org", "Org", "organization"), 0.514,
        [
            Row(Label(RiskIndexObjectKind.Application, "30", "APP-03"), 2.4, 0.1, RiskIndexContributionBranch.Mean, -0.6,
                category: RiskIndexCategory.Exp),
            Row(Label(RiskIndexObjectKind.Scenario, "1", "R1 — vazamento"), 20.2, 1.0, next: Label(RiskIndexObjectKind.Scenario, "2", "R2"))
        ]);

        var rows = ContributionFormatter.Rows(contributions, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(["R1 — vazamento", "APP-03"], rows.Select(r => r.Object));
        var r1 = rows[0];
        Assert.Equal(("REG", "20", "100%", "piso: sustentado", "−4", "R2"),
            (r1.Category, r1.Points, r1.Share, r1.Branch, r1.Sensitivity, r1.NextHolder));
        Assert.Equal(("EXP", "2", "10%", "média", "−1", "—"),
            (rows[1].Category, rows[1].Points, rows[1].Share, rows[1].Branch, rows[1].Sensitivity, rows[1].NextHolder));
        Assert.Equal(20.2, r1.PointsValue);
        Assert.Equal((int)RiskIndexCategory.Reg, r1.CategoryOrder);
        Assert.Equal("Peso efetivo do registro: 51% da manchete é sustentado pela severidade de cenários (REG + GOV cenários + CTL.P).",
            ContributionFormatter.RegisterWeight(contributions, RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestAPresumptionAndAMissingSensitivityReadAsSuch()
    {
        var row = ContributionFormatter.Row(Row(Label(RiskIndexObjectKind.Host, "5", "LEG-05"), 0, 0,
            RiskIndexContributionBranch.Presumption, sensitivity: null), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(("presunção", "—", "0"), (row.Branch, row.Sensitivity, row.Points));
    }

    /// <summary>§5.8 and §11.3: the decision state and the names of scenarios need riskmanagement.</summary>
    [Fact]
    public void TestWithoutTheRegisterPermissionTheScenarioAndItsDecisionAreRedacted()
    {
        var row = ContributionFormatter.Row(Row(Label(RiskIndexObjectKind.Scenario, "1", "Scenario #1", "riskmanagement"), 20, 1,
                next: Label(RiskIndexObjectKind.Scenario, "2", "Scenario #2", "riskmanagement")),
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Cenário #1", row.Object);
        Assert.True(row.IsRedacted);
        Assert.Equal("Requer a permissão riskmanagement", row.ObjectHint);
        Assert.Equal(("Cenário #2", "Requer a permissão riskmanagement"), (row.NextHolder, row.NextHolderHint));
        Assert.Equal("Requer a permissão riskmanagement", row.Decision);
    }

    [Fact]
    public void TestADecisionStateIsShownReadOnlyAndAnObjectWithoutOneShowsADash()
    {
        var state = new RiskIndexDecisionStateDto("expired", new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), "keep",
            new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc), 2, 1, [], "requires_m43", null);

        var withState = ContributionFormatter.Row(Row(Label(RiskIndexObjectKind.Scenario, "1", "R1"), 20, 1, decision: state),
            RiskIndexTestText.Portuguese, PtBr);
        var host = ContributionFormatter.Row(Row(Label(RiskIndexObjectKind.Host, "7", "Host #7", "hosts"), 1, 1),
            RiskIndexTestText.Portuguese, PtBr);
        var readable = ContributionFormatter.Row(Row(Label(RiskIndexObjectKind.Scenario, "1", "R1"), 20, 1),
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("aceite expired até 20/09/2026; última decisão de campanha: keep em 25/08/2026; " +
                     "2 tarefa(s) aberta(s), 1 vencida(s)", withState.Decision);
        Assert.Equal("—", host.Decision);
        Assert.Equal("—", readable.Decision);
        Assert.Equal("—", ContributionFormatter.DecisionText(state with
        {
            AcceptanceStateKey = null, LastCampaignDecisionKey = null, OpenTasks = 0
        }, RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestTheR1NoticeSaysItIsNotAnOrderOfTreatment()
    {
        Assert.Contains("Não são ordem de tratamento", RiskIndexTestText.Portuguese(ContributionFormatter.R1NoticeKey));
        Assert.Contains("not an order of treatment", RiskIndexTestText.English(ContributionFormatter.R1NoticeKey));
    }

    [Fact]
    public void TestEveryKeyTheContributionsComputeExistsInTheThreeResourceFiles()
    {
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = ContributionFormatter.ComputedKeys().Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}
