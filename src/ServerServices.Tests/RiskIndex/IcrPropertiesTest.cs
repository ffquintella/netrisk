using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.RiskIndex.Engine;
using Xunit;
using static ServerServices.Tests.RiskIndex.IcrFixtures;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The properties of S39 §4.6 over generated nodes (S40 §12.1): bounded, monotone in R, invariant under replication,
/// tail-preserving (ICR* ≥ 56·R with the defaults), exactly decomposable (Euler), and the path attribution summing to the
/// change. The generator is seeded, so a failure reproduces.
/// </summary>
[TestSubject(typeof(IcrNodeCalculator))]
public class IcrPropertiesTest
{
    private static readonly RiskIndexCategory[] Scored =
        [RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl, RiskIndexCategory.Gov];

    private static IcrNodeInput RandomNode(Random random, string prefix = "")
    {
        var categories = new List<IcrCategoryInput>();
        foreach (var category in Scored)
        {
            var count = random.Next(0, 12);
            var objects = new List<IcrCategoryObject>();
            for (var i = 0; i < count; i++)
            {
                var population = category switch
                {
                    RiskIndexCategory.Gov => random.Next(2) == 0 ? RiskIndexPopulation.Scenarios : RiskIndexPopulation.Assets,
                    RiskIndexCategory.Ctl => random.Next(2) == 0 ? RiskIndexPopulation.Scenarios : RiskIndexPopulation.Assessments,
                    RiskIndexCategory.Reg => RiskIndexPopulation.Scenarios,
                    RiskIndexCategory.Ame => RiskIndexPopulation.Targets,
                    _ => RiskIndexPopulation.Assets
                };
                var presumed = category is RiskIndexCategory.Exp && random.Next(5) == 0;
                objects.Add(Obj(Host($"{prefix}{category}-{i}"), population, Math.Round(random.NextDouble(), 3),
                    M(random.Next(1, 6)), presumed ? IcrMembership.Presumed : IcrMembership.Assessed));
            }

            categories.Add(new IcrCategoryInput { Category = category, Objects = objects });
        }

        return new IcrNodeInput { Categories = categories };
    }

    private static double? Headline(IcrNodeInput node) => IcrNodeCalculator.Compute(node, Balanced, 0).Unfloored;

    [Fact]
    public void TestTheHeadlineIsBoundedAndKeepsTheTail()
    {
        var random = new Random(4201);
        for (var n = 0; n < 300; n++)
        {
            var node = RandomNode(random);
            var icr = Headline(node);
            if (icr is null) continue;

            Assert.InRange(icr.Value, 0, 100);
            var maxR = node.Categories.SelectMany(c => c.Objects).Max(o => o.R);
            Assert.True(icr.Value >= 100 * Balanced.Tau * Balanced.TauG * maxR - 1e-9,
                $"case {n}: {icr} < 56·{maxR}");
        }
    }

    [Fact]
    public void TestWorseningAnObjectNeverLowersTheHeadline()
    {
        var random = new Random(4202);
        for (var n = 0; n < 300; n++)
        {
            var node = RandomNode(random);
            var before = Headline(node);
            if (before is null) continue;

            var category = node.Categories.Where(c => c.Objects.Count > 0).ElementAt(0);
            var index = random.Next(category.Objects.Count);
            var worse = node with
            {
                Categories = node.Categories.Select(c => c != category ? c : c with
                {
                    Objects = c.Objects.Select((o, i) => i == index ? o with { R = Math.Min(1, o.R + random.NextDouble() * 0.5) } : o).ToList()
                }).ToList()
            };

            Assert.True(Headline(worse) >= before - 1e-9, $"case {n}");
        }
    }

    [Fact]
    public void TestDuplicatingEveryObjectDoesNotMoveTheHeadline()
    {
        var random = new Random(4203);
        for (var n = 0; n < 200; n++)
        {
            var node = RandomNode(random);
            var doubled = node with
            {
                Categories = node.Categories.Select(c => c with
                {
                    Objects = c.Objects.Concat(c.Objects.Select(o => o with { Key = Host(o.Key.Ref + "-copy") })).ToList()
                }).ToList()
            };

            var (a, b) = (Headline(node), Headline(doubled));
            Assert.Equal(a.HasValue, b.HasValue);
            if (a.HasValue) Assert.Equal(a.Value, b!.Value, 9);
        }
    }

    [Fact]
    public void TestEulerContributionsSumToTheHeadline()
    {
        var random = new Random(4204);
        var p = Balanced with { TopContributions = 10_000 };
        for (var n = 0; n < 200; n++)
        {
            var result = IcrNodeCalculator.Compute(RandomNode(random), p, 0);
            if (result.Unfloored is null) continue;

            Assert.Equal(result.Unfloored.Value, result.TopContributions.Sum(c => c.Points), 9);
            Assert.Equal(result.Unfloored.Value, result.CategoryContributions.Values.Sum(), 9);
            foreach (var category in result.Categories.Where(c => c.Status == IcrCategoryStatus.Available))
                Assert.Equal(category.Value, category.Parts.Sum(part => part.Part), 9);
        }
    }

    [Fact]
    public void TestTheAttributionSharesSumToTheChange()
    {
        var random = new Random(4205);
        var resolved = 0;
        for (var n = 0; n < 60; n++)
        {
            var before = RandomNode(random);
            var after = RandomNode(random);
            var result = IcrChangeAttribution.Attribute(before, after, Balanced);
            if (result.Change is null) continue;

            var total = result.CoverageEffect + result.Entries.Sum(e => e.Total) + result.Unattributed;
            Assert.Equal(result.Change.Value, total, 9);
            if (Math.Abs(result.Unattributed) <= Balanced.MaximumResidual) resolved++;
        }

        Assert.True(resolved >= 50, $"only {resolved} of the generated changes were resolved within residuo_max");
    }

    [Fact]
    public void TestTheBandIsReadFromTheDisplayedIntegerSoSeventyIsNeverMedium()
    {
        foreach (var value in new[] { 69.5, 69.9, 70.0, 70.4 })
        {
            var node = new IcrNodeInput
            {
                // One object: M_p is its own R, so C_EXP and the headline are 100·R.
                Categories = [Exp([Obj(Host("A"), RiskIndexPopulation.Assets, value / 100, 1)])]
            };

            var result = IcrNodeCalculator.Compute(node, Balanced, 0);
            Assert.Equal(70, result.Displayed);
            Assert.Equal(2, result.BandIndex);
        }
    }

    [Fact]
    public void TestDisplayStatesFollowCoverage()
    {
        var empty = IcrNodeCalculator.Compute(new IcrNodeInput(), Balanced, 0);
        Assert.Equal(IcrDisplayState.NoData, empty.DisplayState);
        Assert.Null(empty.Displayed);

        var objects = Enumerable.Range(0, 10).Select(i => Obj(Host($"H{i}"), RiskIndexPopulation.Assets, 0.5, 1,
            i < 7 ? IcrMembership.Presumed : IcrMembership.Assessed)).ToList();
        var indicative = IcrNodeCalculator.Compute(new IcrNodeInput { Categories = [Exp(objects)] }, Balanced, 0);
        Assert.Equal(IcrDisplayState.Indicative, indicative.DisplayState);
        Assert.Equal(-1, indicative.BandIndex);
    }

    [Fact]
    public void TestACategoryWithInventoryButNothingAssessedIsUnavailableAndOneWithoutInventoryIsNotApplicable()
    {
        var node = new IcrNodeInput
        {
            Categories =
            [
                Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.5, 1)]),
                new IcrCategoryInput
                {
                    Category = RiskIndexCategory.Gov,
                    Objects = [Obj(Host("A"), RiskIndexPopulation.Assets, 0, 1, IcrMembership.InventoryOnly)]
                },
                new IcrCategoryInput { Category = RiskIndexCategory.Reg }
            ],
            CriticalProcessWeight = 0
        };

        var result = IcrNodeCalculator.Compute(node, Balanced, 0);

        Assert.Equal(IcrCategoryStatus.Unavailable, result.Categories.Single(c => c.Category == RiskIndexCategory.Gov).Status);
        Assert.Equal(IcrCategoryStatus.NotApplicable, result.Categories.Single(c => c.Category == RiskIndexCategory.Reg).Status);
        Assert.Equal(50.0, result.Unfloored!.Value, 9);
        Assert.True(result.NoCriticalProcess);
    }

    [Fact]
    public void TestACriticalProcessWithoutScenariosMakesRegUnavailableNotNotApplicable()
    {
        var node = new IcrNodeInput
        {
            Categories = [Exp([Obj(Host("A"), RiskIndexPopulation.Assets, 0.5, 1)]), new IcrCategoryInput { Category = RiskIndexCategory.Reg }],
            CriticalProcessWeight = 1,
            CriticalProcessWeightCovered = 0
        };

        var result = IcrNodeCalculator.Compute(node, Balanced, 0);

        Assert.Equal(IcrCategoryStatus.Unavailable, result.Categories.Single(c => c.Category == RiskIndexCategory.Reg).Status);
        Assert.Equal(0.5, result.Coverage, 9);
        Assert.Equal(IcrDisplayState.WithInterval, result.DisplayState);
    }

    [Fact]
    public void TestTheGateAFloorRaisesTheDisplayedValueButNotTheUnflooredOne()
    {
        var node = IcrFixtures.UnitX() with { GateAFloorTriggered = true };

        var result = IcrNodeCalculator.Compute(node, Balanced, 0);

        Assert.Equal(70, result.Floored);
        Assert.Equal(70, result.Displayed);
        Assert.InRange(result.Unfloored!.Value, 63.68, 63.70);
    }
}
