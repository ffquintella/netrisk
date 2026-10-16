using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.RiskIndex.Engine;
using Xunit;
using static ServerServices.Tests.RiskIndex.IcrFixtures;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// "What moved" by Aumann–Shapley path attribution (S39 §9.4, §11.3; S40 §12.2 <c>ChangeAttributionTest</c>): Example C,
/// objects entering and leaving, the argmax switch the Euler difference gets wrong, a population that enters, a
/// category leaving <c>K(S)</c>, reattribution, and the shares summing to the change.
/// </summary>
[TestSubject(typeof(IcrChangeAttribution))]
public class ChangeAttributionTest
{
    private static void Near(double expected, double actual, double tolerance = 0.01) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static double Share(IcrChangeAttributionResult result, IcrObjectKey key, RiskIndexCategory category) =>
        result.Entries.Where(e => e.Key == key && e.Category == category).Sum(e => e.Total);

    [Fact]
    public void TestExampleC()
    {
        var day2 = IcrNodeCalculator.Compute(UnitX(1), Balanced, 0);
        Near(72.00, day2.Categories.Single(c => c.Category == RiskIndexCategory.Exp).Value);
        Near(48.00, day2.Categories.Single(c => c.Category == RiskIndexCategory.Gov).Value);
        Near(67.20, day2.Categories.Single(c => c.Category == RiskIndexCategory.Reg).Value);
        Near(59.98, day2.Unfloored!.Value);
        Assert.Equal(60, day2.Displayed);

        var result = IcrChangeAttribution.Attribute(UnitX(), UnitX(1), Balanced);

        Near(-3.71, result.Change!.Value);
        Near(-2.88, Share(result, Scenario("R1"), RiskIndexCategory.Gov));
        Near(-0.67, Share(result, App("APP-03"), RiskIndexCategory.Exp));
        Near(-0.16, Share(result, Scenario("R1"), RiskIndexCategory.Reg));
        Near(0, Share(result, Host("SRV-01"), RiskIndexCategory.Exp), 1e-9);
        Near(0, Share(result, Host("BKP-04"), RiskIndexCategory.Exp), 1e-9);
        Near(0, Share(result, Scenario("R2"), RiskIndexCategory.Reg), 1e-9);
        Near(0, Share(result, Target("UNIT-X"), RiskIndexCategory.Ame), 1e-9);
        Near(0, result.Unattributed);
        Near(0, result.CoverageEffect, 1e-9);
        Assert.True(result.Segments > 1, "the argmax switches of EXP and GOV split the path");
    }

    [Fact]
    public void TestTheSimpleEulerDifferenceWouldBlameTheUnchangedServer()
    {
        var before = IcrNodeCalculator.Compute(UnitX(), Balanced, 0).Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        var after = IcrNodeCalculator.Compute(UnitX(1), Balanced, 0).Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        double Part(IcrCategoryResult r, IcrObjectKey key) => r.Parts.Where(p => p.Key == key).Sum(p => p.Part);

        Near(72.0, Part(after, Host("SRV-01")) - Part(before, Host("SRV-01")));
        Near(-74.25, Part(after, App("APP-03")) - Part(before, App("APP-03")));
    }

    private static IcrNodeInput Node(params IcrCategoryInput[] categories) => new() { Categories = categories };

    private static IcrCategoryInput Gov(params IcrCategoryObject[] objects) =>
        new() { Category = RiskIndexCategory.Gov, Objects = objects };

    private static IcrCategoryInput Reg(params IcrCategoryObject[] objects) =>
        new() { Category = RiskIndexCategory.Reg, Objects = objects };

    [Fact]
    public void TestAnEnteringObjectThatTakesOverTheFloorReceivesTheFloorEffectContinuously()
    {
        var before = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.5, 1)]));
        var after = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.5, 1),
            Obj(Host("B"), RiskIndexPopulation.Assets, 0.9, 1)]));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        Near(result.Change!.Value, result.Entries.Sum(e => e.Total) + result.Unattributed, 1e-9);
        Assert.True(Share(result, Host("B"), RiskIndexCategory.Exp) > 0);
        Near(0, result.Unattributed, Balanced.MaximumResidual);
    }

    [Fact]
    public void TestALeavingObjectTakesItsShareWithIt()
    {
        var before = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.2, 1),
            Obj(Host("B"), RiskIndexPopulation.Assets, 0.9, 1)]));
        var after = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.2, 1)]));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        Assert.True(result.Change < 0);
        Near(result.Change!.Value, Share(result, Host("B"), RiskIndexCategory.Exp) + Share(result, Host("A"),
            RiskIndexCategory.Exp), Balanced.MaximumResidual);
    }

    [Fact]
    public void TestAPopulationThatEntersIsAttributedToTheObjectsThatBroughtIt()
    {
        // S39 §9.4: one GOV scenario at 0.6 reads 60; the first clean host gives the asset population its quota.
        var before = Node(Gov(Obj(Scenario("R"), RiskIndexPopulation.Scenarios, 0.6, 1)));
        var after = Node(Gov(Obj(Scenario("R"), RiskIndexPopulation.Scenarios, 0.6, 1),
            Obj(Host("H"), RiskIndexPopulation.Assets, 0, 1)));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        Near(result.Change!.Value, result.Entries.Sum(e => e.Total) + result.Unattributed, 1e-9);
        Near(0, result.Unattributed, Balanced.MaximumResidual);
        Assert.Equal(0, Share(result, Scenario("R"), RiskIndexCategory.Gov), 9);
    }

    [Fact]
    public void TestAPresumedObjectThatBecomesAssessedLeavesOneSetAndEntersTheOther()
    {
        var before = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.1, 1),
            Obj(Host("P"), RiskIndexPopulation.Assets, 0.5, 1, IcrMembership.Presumed)]));
        var after = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.1, 1),
            Obj(Host("P"), RiskIndexPopulation.Assets, 0.3, 1)]));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        var memberships = result.Entries.Where(e => e.Key == Host("P")).Select(e => e.Membership).ToHashSet();
        Assert.Contains(IcrMembership.Presumed, memberships);
        Assert.Contains(IcrMembership.Assessed, memberships);
        Near(result.Change!.Value, result.Entries.Sum(e => e.Total) + result.Unattributed, 1e-9);
    }

    [Fact]
    public void TestACategoryLeavingTheHeadlineIsACoverageEffect()
    {
        var reg = Reg(Obj(Scenario("R"), RiskIndexPopulation.Scenarios, 0.5, 1));
        var before = Node(reg, Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.9, 1)]));
        var after = Node(reg, Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.9, 1)]) with { SourceEnabled = false });

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        Near(result.Change!.Value, result.CoverageEffect, 1e-9);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void TestAReattributedObjectIsARestructuringEffectNotARiskEffect()
    {
        var before = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.2, 1)]));
        var after = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.2, 1),
            Obj(Host("M"), RiskIndexPopulation.Assets, 0.8, 1)]));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced, new HashSet<IcrObjectKey> { Host("M") });

        Assert.True(result.Entries.Single(e => e.Key == Host("M")).Reattribution);
        Near(Share(result, Host("M"), RiskIndexCategory.Exp), result.RestructuringEffect, 1e-12);
        Assert.False(result.ByCategory.TryGetValue(RiskIndexCategory.Exp, out var risk) && System.Math.Abs(risk) > 1e-9);
    }

    [Fact]
    public void TestAWeightChangeIsAContextEffect()
    {
        var before = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.6, 1),
            Obj(Host("B"), RiskIndexPopulation.Assets, 0.55, 0.4)]));
        var after = Node(Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.6, 1),
            Obj(Host("B"), RiskIndexPopulation.Assets, 0.55, 1.0)]));

        var result = IcrChangeAttribution.Attribute(before, after, Balanced);

        var b = result.Entries.Where(e => e.Key == Host("B")).ToList();
        Assert.NotEmpty(b);
        Assert.All(b, e => Assert.Equal(0, e.RiskEffect, 12));
        Near(result.Change!.Value, result.Entries.Sum(e => e.Total) + result.Unattributed, 1e-9);
    }

    [Fact]
    public void TestNoDataOnOneDayAttributesNothing()
    {
        var result = IcrChangeAttribution.Attribute(Node(), UnitX(), Balanced);

        Assert.Null(result.Change);
        Assert.Empty(result.Entries);
    }
}
