using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.TreatmentEconomics;
using Tools.TreatmentEconomics;
using Xunit;

namespace Tools.Tests.TreatmentEconomics;

/// <summary>
/// Stage 9.6 (S47 §4.7, §8 PD1–PD17) — Gate D: portfolio selection under budget, people, dependencies and deadline.
/// The case a naive optimizer gets wrong first is PD1/PD2 (T181): a tail or systemic treatment with a moderate expected
/// loss is preserved ahead of a higher-E[L] one, instead of being ranked out.
/// </summary>
[TestSubject(typeof(PortfolioSelector))]
public class PortfolioSelectorTest
{
    private static readonly HashSet<int> NoneCompleted = [];

    /// <summary>
    /// A candidate whose first-year and annualized cost are both <paramref name="cost"/> (an annual cost), with Gate C
    /// computed from <paramref name="benefit"/>; E[L] before is twice the benefit.
    /// </summary>
    private static PortfolioCandidate C(int id, decimal? cost, double? benefit, bool systemic = false, bool tail = false,
        bool gateA = false, int[]? prerequisites = null, decimal? effort = null, int? duration = null,
        TreatmentOption? option = TreatmentOption.Reduce, bool? aboveAppetite = null)
    {
        var treatmentCost = cost is null ? null : new TreatmentCost(0, cost.Value, 0, null);
        var gate = GateC.Evaluate(new GateCInput
        {
            Option = option,
            Cost = treatmentCost,
            QuantitativeAnalysis = benefit is not null,
            ExpectedLossBefore = benefit is null ? null : benefit * 2,
            ExpectedLossAfter = benefit,
            ResidualMedianRecorded = benefit is not null,
            GateAHolds = gateA
        });

        return new PortfolioCandidate
        {
            MitigationId = id, RiskId = 100 + id, RiskSubject = $"Risk {100 + id}", Option = option,
            Cost = treatmentCost, EffortPersonDays = effort, DurationDays = duration,
            Prerequisites = prerequisites ?? [], GateC = gate, GateA = gateA, Systemic = systemic, Tail = tail,
            AboveAppetite = aboveAppetite
        };
    }

    private static PortfolioSelectionDto Select(decimal budget, params PortfolioCandidate[] candidates) =>
        PortfolioSelector.Select(new PortfolioConstraints(budget, null, null), candidates, NoneCompleted);

    private static PortfolioItemDto Item(PortfolioSelectionDto selection, int id) =>
        selection.Items.Single(i => i.MitigationId == id);

    private static List<int> SelectedIds(PortfolioSelectionDto selection) => selection.Items
        .Where(i => i.Status == PortfolioItemStatus.Selected).OrderBy(i => i.SelectionOrder).Select(i => i.MitigationId)
        .ToList();

    /// <summary>
    /// PD1 (T181) — a systemic treatment (flag 6) with a moderate expected loss is selected over a higher-E[L],
    /// higher-ratio one when the budget fits one; without the flag, the pure economic ranking picks the other. The
    /// control proves the test discriminates.
    /// </summary>
    [Fact]
    public void TestPD1_ASystemicRiskAtModerateExpectedLossIsPreserved()
    {
        var economic = C(1, cost: 100, benefit: 1_000);
        var systemic = C(2, cost: 100, benefit: 150, systemic: true);

        var withFlag = Select(100, economic, systemic);
        Assert.Equal([2], SelectedIds(withFlag));
        Assert.Equal(PortfolioTier.Protected, Item(withFlag, 2).Tier);
        Assert.Equal(PortfolioItemStatus.OverBudget, Item(withFlag, 1).Status);
        Assert.False(withFlag.ProtectedShortfall);

        var withoutFlag = Select(100, economic, C(2, cost: 100, benefit: 150));
        Assert.Equal([1], SelectedIds(withoutFlag));
    }

    /// <summary>
    /// PD2 (T181) — a tail treatment (flag 8) is preserved even when its Gate C fails: the expected loss understates a
    /// low-probability catastrophic risk, which is why the methodology says to preserve it. In the economic tier the
    /// same failing treatment is excluded.
    /// </summary>
    [Fact]
    public void TestPD2_ATailRiskIsPreservedEvenWhenGateCFails()
    {
        var tail = C(2, cost: 100, benefit: 50, tail: true);
        Assert.Equal(GateCOutcome.Fails, tail.GateC.Outcome);

        var selection = Select(100, C(1, cost: 100, benefit: 1_000), tail);

        Assert.Equal([2], SelectedIds(selection));
        Assert.Contains(Item(selection, 2).Reasons, r => r.Contains("flag 8"));
        Assert.Contains(Item(selection, 2).Reasons, r => r.Contains("Gate C fails"));

        var economic = Select(1_000, C(2, cost: 100, benefit: 50));
        Assert.Equal(PortfolioItemStatus.FailsGateC, Item(economic, 2).Status);
    }

    /// <summary>PD3 — Gate A comes first: its treatment is selected before anything, even failing Gate C with the worst ratio.</summary>
    [Fact]
    public void TestPD3_GateAIsSelectedFirstWhateverTheEconomics()
    {
        var selection = Select(100, C(1, cost: 100, benefit: 1_000), C(2, 100, 1_000, systemic: true),
            C(3, cost: 100, benefit: 10, gateA: true));

        Assert.Equal([3], SelectedIds(selection));
        Assert.Equal(PortfolioTier.Mandatory, Item(selection, 3).Tier);
        Assert.False(selection.GateAShortfall);
        Assert.True(selection.ProtectedShortfall);
    }

    /// <summary>PD4 — a Gate A treatment that does not fit is listed and raises the shortfall; it never disappears.</summary>
    [Fact]
    public void TestPD4_AGateATreatmentOverBudgetRaisesTheShortfall()
    {
        var selection = Select(100, C(3, cost: 500, benefit: 10, gateA: true), C(1, cost: 100, benefit: 1_000));

        Assert.Equal(PortfolioItemStatus.OverBudget, Item(selection, 3).Status);
        Assert.True(selection.GateAShortfall);
        Assert.Equal([1], SelectedIds(selection));
        Assert.Equal(2, selection.Considered);
    }

    /// <summary>PD5 — no monetary cost: not assessable, never selected; a protected one without cost raises the shortfall.</summary>
    [Fact]
    public void TestPD5_NoMonetaryCostIsNeverSelected()
    {
        var selection = Select(1_000_000, C(1, cost: null, benefit: 1_000), C(2, cost: null, benefit: 10, tail: true));

        Assert.Empty(SelectedIds(selection));
        Assert.All(selection.Items, i => Assert.Equal(PortfolioItemStatus.NotAssessable, i.Status));
        Assert.Contains(Item(selection, 1).Reasons, r => r.Contains("not a zero cost"));
        Assert.True(selection.ProtectedShortfall);
        Assert.Equal(0m, selection.BudgetUsed);
    }

    /// <summary>PD6 — in the economic tier a failing or not-assessable Gate C excludes, with the Gate C sentence.</summary>
    [Fact]
    public void TestPD6_TheEconomicTierRequiresGateCToPass()
    {
        var selection = Select(1_000, C(1, cost: 100, benefit: 50), C(2, cost: 100, benefit: null));

        Assert.Equal(PortfolioItemStatus.FailsGateC, Item(selection, 1).Status);
        Assert.Equal(PortfolioItemStatus.NotAssessable, Item(selection, 2).Status);
        Assert.Empty(SelectedIds(selection));
    }

    /// <summary>PD7 — the economic tier is filled by benefit/cost ratio, a zero-cost benefit first, until the budget runs out.</summary>
    [Fact]
    public void TestPD7_TheEconomicTierIsGreedyByRatio()
    {
        var selection = Select(150,
            C(1, cost: 100, benefit: 500), // ratio 5
            C(2, cost: 50, benefit: 200), // ratio 4
            C(3, cost: 100, benefit: 300), // ratio 3
            C(4, cost: 0, benefit: 10)); // free

        Assert.Equal([4, 1, 2], SelectedIds(selection));
        Assert.Equal(PortfolioItemStatus.OverBudget, Item(selection, 3).Status);
        Assert.Equal(150m, selection.BudgetUsed);
        Assert.Equal(710, selection.ExpectedReduction, 6);
    }

    /// <summary>PD8 — the people capacity is a second budget.</summary>
    [Fact]
    public void TestPD8_PeopleCapacityIsRespected()
    {
        var selection = PortfolioSelector.Select(new PortfolioConstraints(1_000, 10, null),
        [
            C(1, cost: 100, benefit: 500, effort: 8),
            C(2, cost: 100, benefit: 400, effort: 5),
            C(3, cost: 100, benefit: 300, effort: 2)
        ], NoneCompleted);

        Assert.Equal([1, 3], SelectedIds(selection));
        Assert.Equal(PortfolioItemStatus.OverPeopleCapacity, Item(selection, 2).Status);
        Assert.Equal(10m, selection.PeopleUsed);
    }

    /// <summary>PD9 — the deadline is checked on the critical path: a dependent ends after its prerequisite.</summary>
    [Fact]
    public void TestPD9_TheDeadlineFollowsTheCriticalPath()
    {
        var selection = PortfolioSelector.Select(new PortfolioConstraints(1_000, null, 30),
        [
            C(10, cost: 100, benefit: 500, duration: 20),
            C(11, cost: 100, benefit: 500, duration: 15, prerequisites: [10]),
            C(12, cost: 100, benefit: 500, duration: 25)
        ], NoneCompleted);

        Assert.Equal(20, Item(selection, 10).CompletionDay);
        Assert.Equal(35, Item(selection, 11).CompletionDay);
        Assert.Equal(PortfolioItemStatus.MissesDeadline, Item(selection, 11).Status);
        Assert.Equal([10, 12], SelectedIds(selection).OrderBy(i => i));
    }

    /// <summary>
    /// PD10 — a candidate takes its prerequisites with it, and the prerequisite inherits its tier (so a failing Gate C
    /// does not exclude it); when the bundle does not fit, neither is funded — the prerequisite is not funded alone.
    /// </summary>
    [Fact]
    public void TestPD10_APrerequisiteRidesInItsDependentsBundle()
    {
        PortfolioCandidate[] candidates =
        [
            C(20, cost: 100, benefit: 50, tail: true, prerequisites: [21]),
            C(21, cost: 100, benefit: 10)
        ];

        var fits = Select(200, candidates);
        Assert.Equal([21, 20], SelectedIds(fits));
        Assert.Equal(20, Item(fits, 21).SelectedFor);
        Assert.Equal(PortfolioTier.Protected, Item(fits, 21).Tier);

        var tooSmall = Select(150, candidates);
        Assert.Empty(SelectedIds(tooSmall));
        Assert.Equal(PortfolioItemStatus.OverBudget, Item(tooSmall, 20).Status);
        Assert.Contains("#21", Item(tooSmall, 20).Reasons.Single());
        Assert.Equal(PortfolioItemStatus.FailsGateC, Item(tooSmall, 21).Status);
        Assert.True(tooSmall.ProtectedShortfall);
    }

    /// <summary>PD11 — a prerequisite that is excluded, or outside the portfolio and not completed, blocks; a completed one satisfies.</summary>
    [Fact]
    public void TestPD11_PrerequisitesBlockOrSatisfy()
    {
        var excluded = Select(1_000, C(1, cost: 100, benefit: 500, prerequisites: [2]), C(2, cost: null, benefit: 100));
        Assert.Equal(PortfolioItemStatus.BlockedByDependency, Item(excluded, 1).Status);

        var outside = Select(1_000, C(1, cost: 100, benefit: 500, prerequisites: [99]));
        Assert.Equal(PortfolioItemStatus.BlockedByDependency, Item(outside, 1).Status);
        Assert.Contains("#99", Item(outside, 1).Reasons.Single());

        var completed = PortfolioSelector.Select(new PortfolioConstraints(1_000, null, 10),
            [C(1, cost: 100, benefit: 500, prerequisites: [99], duration: 5)], new HashSet<int> { 99 });
        Assert.Equal(PortfolioItemStatus.Selected, Item(completed, 1).Status);
        Assert.Equal(5, Item(completed, 1).CompletionDay);
    }

    /// <summary>PD12 — the members of a cycle are excluded, and what depends on them is blocked; nothing loops.</summary>
    [Fact]
    public void TestPD12_ACycleIsExcluded()
    {
        var selection = PortfolioSelector.Select(new PortfolioConstraints(1_000, null, 100),
        [
            C(1, cost: 10, benefit: 500, prerequisites: [2], duration: 1),
            C(2, cost: 10, benefit: 500, prerequisites: [3], duration: 1),
            C(3, cost: 10, benefit: 500, prerequisites: [1], duration: 1),
            C(4, cost: 10, benefit: 500, prerequisites: [1], duration: 1),
            C(5, cost: 10, benefit: 500, duration: 1)
        ], NoneCompleted);

        foreach (var id in new[] { 1, 2, 3 })
            Assert.Equal(PortfolioItemStatus.DependencyCycle, Item(selection, id).Status);
        Assert.Equal(PortfolioItemStatus.BlockedByDependency, Item(selection, 4).Status);
        Assert.Equal([5], SelectedIds(selection));
    }

    /// <summary>PD13 — the result does not depend on the order the candidates arrive in.</summary>
    [Fact]
    public void TestPD13_InputOrderIsIrrelevant()
    {
        PortfolioCandidate[] candidates =
        [
            C(1, 100, 500), C(2, 50, 200), C(3, 100, 300, systemic: true), C(4, 80, 900, prerequisites: [2]),
            C(5, 70, 50, gateA: true), C(6, 60, 600)
        ];

        var forward = Select(300, candidates);
        var backward = Select(300, candidates.Reverse().ToArray());

        Assert.Equal(SelectedIds(forward), SelectedIds(backward));
        Assert.Equal(forward.Items.Select(i => (i.MitigationId, i.Status)), backward.Items.Select(i => (i.MitigationId, i.Status)));
    }

    /// <summary>PD14 — "accept" has nothing to fund; with Gate A it is not a legitimate treatment and raises the shortfall.</summary>
    [Fact]
    public void TestPD14_AcceptIsNotFunded()
    {
        var plain = Select(1_000, C(1, cost: null, benefit: null, option: TreatmentOption.Accept, systemic: true));
        Assert.Equal(PortfolioItemStatus.NotApplicable, Item(plain, 1).Status);
        Assert.False(plain.ProtectedShortfall);

        var gateA = Select(1_000, C(1, cost: null, benefit: null, option: TreatmentOption.Accept, gateA: true));
        Assert.Equal(PortfolioItemStatus.NotAssessable, Item(gateA, 1).Status);
        Assert.True(gateA.GateAShortfall);
    }

    /// <summary>PD15 — Gate B: a candidate above the appetite that is not funded has to be escalated.</summary>
    [Fact]
    public void TestPD15_AboveAppetiteAndNotSelectedRequiresEscalation()
    {
        var selection = Select(100,
            C(1, cost: 100, benefit: 1_000, aboveAppetite: true),
            C(2, cost: 100, benefit: 500, aboveAppetite: true),
            C(3, cost: 100, benefit: 400, aboveAppetite: false));

        Assert.False(Item(selection, 1).RequiresEscalation);
        Assert.True(Item(selection, 2).RequiresEscalation);
        Assert.False(Item(selection, 3).RequiresEscalation);
        Assert.Equal(1, selection.EscalationsRequired);
    }

    /// <summary>PD16 — a missing effort or duration only matters when the portfolio has that constraint.</summary>
    [Fact]
    public void TestPD16_MissingEstimatesOnlyMatterUnderTheirConstraint()
    {
        var unconstrained = Select(1_000, C(1, cost: 100, benefit: 500));
        Assert.Equal(PortfolioItemStatus.Selected, Item(unconstrained, 1).Status);

        var people = PortfolioSelector.Select(new PortfolioConstraints(1_000, 10, null), [C(1, 100, 500)], NoneCompleted);
        Assert.Equal(PortfolioItemStatus.NotAssessable, Item(people, 1).Status);
        Assert.Contains(Item(people, 1).Reasons, r => r.Contains("effort"));

        var deadline = PortfolioSelector.Select(new PortfolioConstraints(1_000, null, 10), [C(1, 100, 500)], NoneCompleted);
        Assert.Equal(PortfolioItemStatus.NotAssessable, Item(deadline, 1).Status);
        Assert.Contains(Item(deadline, 1).Reasons, r => r.Contains("duration"));
    }

    /// <summary>PD17 — the totals: budget used, selected count, a mandatory selection without a known benefit counted apart.</summary>
    [Fact]
    public void TestPD17_TheSummaryAddsUp()
    {
        var selection = Select(1_000, C(1, cost: 100, benefit: 500), C(2, cost: 200, benefit: null, gateA: true),
            C(3, cost: 50, benefit: 10));

        Assert.Equal(3, selection.Considered);
        Assert.Equal(2, selection.Selected);
        Assert.Equal(300m, selection.BudgetUsed);
        Assert.Equal(500, selection.ExpectedReduction, 6);
        Assert.Equal(1, selection.SelectedWithUnknownBenefit);
        Assert.Equal([2, 1], SelectedIds(selection));
        Assert.Equal(2, selection.Items.First().MitigationId);
    }
}
