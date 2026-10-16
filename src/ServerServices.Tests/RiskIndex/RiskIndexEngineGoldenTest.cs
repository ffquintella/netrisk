using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Xunit;
using static ServerServices.Tests.RiskIndex.IcrFixtures;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The engine against the numbers S39 publishes (S40 §12.1): the canonical case (§4.7), Unit X (§11.1–§11.2), the
/// many-to-many drill-down (§11.4), partial coverage (§11.5) and the presets and tornado (§11.6). Two-decimal values
/// are asserted within 0.01, as S40 §12.1 asks.
/// </summary>
[TestSubject(typeof(IcrNodeCalculator))]
public class RiskIndexEngineGoldenTest
{
    private const double Tolerance = 0.01;

    private static void Near(double expected, double actual, double tolerance = Tolerance) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static IcrCategoryResult Category(IcrNodeResult node, RiskIndexCategory category) =>
        node.Categories.Single(c => c.Category == category);

    private static IcrCategoryResult ExpOf(IEnumerable<IcrCategoryObject> objects, RiskIndexParameters? p = null) =>
        IcrCategoryCalculator.Compute(Exp(objects), p ?? Balanced);

    // ---------- §4.7 ----------

    [Fact]
    public void TestTheCriticalServerIsNotDilutedByTwentyTestMachines()
    {
        var exp = ExpOf(Canonical());

        Near(52.0, exp.PowerMean, 0.05);
        Near(72.0, exp.FloorValue);
        Near(72.0, exp.Value);
        Assert.True(exp.FloorActive);
        Assert.Equal([Host("SRV-0")], exp.HeldBy);
    }

    [Theory]
    [InlineData(200, 30.1)]
    [InlineData(2000, 17.4)]
    public void TestMoreTestMachinesLowerTheMeanButNotTheCategory(int vms, double mean)
    {
        var exp = ExpOf(Canonical(vms));

        Near(mean, exp.PowerMean, 0.05);
        Near(72.0, exp.Value);
    }

    [Fact]
    public void TestDuplicatingTheInventoryDoesNotMoveTheValue()
    {
        Near(ExpOf(Canonical()).Value, ExpOf(Canonical(copies: 2)).Value, 1e-9);
        Near(ExpOf(Canonical()).PowerMean, ExpOf(Canonical(copies: 2)).PowerMean, 1e-9);
    }

    [Fact]
    public void TestFixingTheServerLowersTheCategoryAndFixingTheVmsDoesNot()
    {
        var serverFixed = ExpOf(Canonical(server: 0.35));
        Near(20.5, serverFixed.PowerMean, 0.05);
        Near(28.0, serverFixed.FloorValue);
        Near(28.0, serverFixed.Value);

        var vmsFixed = ExpOf(Canonical(vm: 0));
        Near(72.0, vmsFixed.Value);
        Assert.Equal([Host("SRV-0")], vmsFixed.HeldBy);
    }

    [Fact]
    public void TestTheServerAloneInAnOtherwiseCleanOrganizationReadsMedium()
    {
        var node = new IcrNodeInput
        {
            Categories =
            [
                new IcrCategoryInput { Category = RiskIndexCategory.Reg, Objects = [Obj(Scenario("R"), RiskIndexPopulation.Scenarios, 0, 1)] },
                Exp(Canonical()),
                new IcrCategoryInput { Category = RiskIndexCategory.Ame, Objects = [Obj(Target("E"), RiskIndexPopulation.Targets, 0, 1)] },
                new IcrCategoryInput { Category = RiskIndexCategory.Ctl, Objects = [Obj(Target("E"), RiskIndexPopulation.Assessments, 0, 1)] },
                new IcrCategoryInput { Category = RiskIndexCategory.Gov, Objects = [Obj(Scenario("R"), RiskIndexPopulation.Scenarios, 0, 1)] }
            ]
        };

        var result = IcrNodeCalculator.Compute(node, Balanced);

        Near(50.4, result.Unfloored!.Value);
        Assert.True(result.HeadlineFloorActive);
        Assert.Equal(50, result.Displayed);
        Assert.Equal(1, result.BandIndex);
    }

    [Fact]
    public void TestThePureMeanWithoutFloorsDilutesTheServer()
    {
        var pure = Balanced with { P = 1, Tau = 0, TauG = 0 };
        Near(18.9, ExpOf(Canonical(), pure).Value, 0.05);

        var conservative = Balanced with { P = 8, Tau = 0.90, TauG = 0.85 };
        Near(81.0, ExpOf(Canonical(), conservative).Value);
    }

    // ---------- §11.1–§11.2 ----------

    [Fact]
    public void TestUnitXCategories()
    {
        var node = IcrNodeCalculator.Compute(UnitX(), Balanced);

        var exp = Category(node, RiskIndexCategory.Exp);
        Near(54.53, exp.PowerMean);
        Near(74.25, exp.Value);
        Assert.Equal(0, exp.PresumedIncluded);
        Assert.Equal([App("APP-03")], exp.HeldBy);

        var ame = Category(node, RiskIndexCategory.Ame);
        Near(53.27, ame.PowerMean);
        Near(60.00, ame.Value);

        var reg = Category(node, RiskIndexCategory.Reg);
        Near(66.90, reg.PowerMean);
        Near(67.73, reg.Value);
        Assert.Equal([Scenario("R1")], reg.HeldBy);

        var gov = Category(node, RiskIndexCategory.Gov);
        Near(52.80, gov.PowerMean);
        Near(67.20, gov.Value);

        var ctl = Category(node, RiskIndexCategory.Ctl);
        Near(17.53, ctl.PowerMean);
        Near(20.16, ctl.Value);
        Assert.Equal([Scenario("R1")], ctl.HeldBy);
    }

    [Fact]
    public void TestUnitXPresumedObjectsWouldDiluteSoNoneIsIncluded()
    {
        var exp = UnitX().Categories.Single(c => c.Category == RiskIndexCategory.Exp).Objects;

        IcrCategoryResult With(params string[] assessed) => ExpOf(exp.Select(o =>
            assessed.Contains(o.Key.Ref) ? o with { Membership = IcrMembership.Assessed } : o));

        Near(54.31, With("LEG-05").PowerMean);
        Near(53.91, With("LEG-05", "WS-ADM").PowerMean);
        Near(0, ExpOf(exp).PresumptionPoints, 1e-9);
    }

    [Fact]
    public void TestUnitXHeadlineAndComposition()
    {
        var node = IcrNodeCalculator.Compute(UnitX(), Balanced);

        Near(63.69, node.Unfloored!.Value);
        Assert.Equal(node.Unfloored, node.Floored);
        Assert.Equal(64, node.Displayed);
        Assert.Equal(1, node.BandIndex);
        Assert.Equal("Médio", Balanced.Bands[node.BandIndex].LabelPtBr);
        Assert.False(node.HeadlineFloorActive);

        Near(20.32, node.CategoryContributions[RiskIndexCategory.Reg]);
        Near(22.27, node.CategoryContributions[RiskIndexCategory.Exp]);
        Near(9.00, node.CategoryContributions[RiskIndexCategory.Ame]);
        Near(2.02, node.CategoryContributions[RiskIndexCategory.Ctl]);
        Near(10.08, node.CategoryContributions[RiskIndexCategory.Gov]);
        Near(node.Unfloored.Value, node.CategoryContributions.Values.Sum(), 1e-9);
    }

    [Fact]
    public void TestUnitXCoverageQualityAndInterval()
    {
        var node = IcrNodeCalculator.Compute(UnitX(), Balanced);

        Near(0.914, Category(node, RiskIndexCategory.Exp).Coverage, 0.001);
        Near(0.976, Category(node, RiskIndexCategory.Exp).Freshness, 0.001);
        Near(0.833, Category(node, RiskIndexCategory.Reg).Coverage, 0.001);
        Near(0.974, Category(node, RiskIndexCategory.Gov).Coverage, 0.001);
        Near(1.0, Category(node, RiskIndexCategory.Ame).Coverage, 1e-9);
        Near(1.0, Category(node, RiskIndexCategory.Ctl).Coverage, 1e-9);

        Near(0.920, node.Coverage, 0.001);
        Near(0.993, node.Freshness, 0.001);
        Near(0.914, node.Quality, 0.001);
        Near(63.69, node.IntervalLow!.Value);
        Near(65.42, node.IntervalHigh!.Value);
        Assert.Equal(IcrDisplayState.Point, node.DisplayState);
        Assert.False(node.NoCriticalProcess);
    }

    [Fact]
    public void TestUnitXIndicatorSensitivity()
    {
        var node = IcrNodeCalculator.Compute(UnitX(), Balanced);

        double Delta(IcrObjectKey key, RiskIndexCategory category) =>
            node.Sensitivity.Single(s => s.Key == key && s.Category == category).ChangeIfCleared;

        Near(-4.32, Delta(Scenario("R1"), RiskIndexCategory.Reg));
        Near(-2.88, Delta(Scenario("R1"), RiskIndexCategory.Gov));
        Near(-0.67, Delta(App("APP-03"), RiskIndexCategory.Exp));
        Near(-0.45, Delta(Scenario("R1"), RiskIndexCategory.Ctl));
        Near(-4.50, Delta(Target("APP-03"), RiskIndexCategory.Ame));

        Assert.Equal([Host("SRV-01")],
            node.Sensitivity.Single(s => s.Key == App("APP-03") && s.Category == RiskIndexCategory.Exp).NextHolders);
    }

    [Fact]
    public void TestEulerContributionsSumToTheHeadlineAndOnlyTheFloorHolderCarriesAFlooredCategory()
    {
        var node = IcrNodeCalculator.Compute(UnitX(), Balanced with { TopContributions = 1000 });

        Near(node.Unfloored!.Value, node.TopContributions.Sum(c => c.Points), 1e-9);
        var exp = node.TopContributions.Where(c => c.Category == RiskIndexCategory.Exp).ToList();
        Assert.Equal([App("APP-03")], exp.Select(c => c.Key));
        Assert.True(exp[0].HoldsFloor);
    }

    // ---------- §11.4: many-to-many drill-down ----------

    [Fact]
    public void TestExampleDTheParentIsBelowBothChildrenAndKeepsTheTailGuarantee()
    {
        var unit = UnitX().Categories.Single(c => c.Category == RiskIndexCategory.Exp).Objects;
        IcrCategoryObject Pick(string reference) => unit.First(o => o.Key.Ref == reference);

        var enrolment = ExpOf([Pick("APP-03"), Pick("SRV-01"), Pick("SSC-1"), Pick("LEG-05")]);
        Near(84.13, enrolment.Value);
        Assert.Equal(0, enrolment.PresumedIncluded);
        Near(78.88, ExpOf([Pick("APP-03"), Pick("SRV-01"), Pick("SSC-1"),
            Pick("LEG-05") with { Membership = IcrMembership.Assessed }]).Value);

        var research = ExpOf([Pick("APP-03"), Pick("DB-02"), Pick("BKP-04"), Pick("WS-ADM")]);
        Near(75.71, research.Value);

        var lab = ExpOf(unit.Where(o => o.Key.Ref.StartsWith("TST-")).Append(Pick("WS-ADM")));
        Near(28.00, lab.Value);
        Near(10.00, lab.AssessedValue);
        Near(18.00, lab.PresumptionPoints);
        Assert.Equal(1, lab.PresumedIncluded);

        var whole = ExpOf(unit);
        Near(74.25, whole.Value);
        Assert.True(whole.Value < enrolment.Value && whole.Value < research.Value);
        Assert.True(whole.Value >= 100 * Balanced.Tau * unit.Max(o => o.R) - 1e-9);
    }

    // ---------- §11.5: partial coverage ----------

    [Fact]
    public void TestExampleEMissingCategoriesRenormalizeAndTheIntervalShowsIt()
    {
        var node = IcrNodeCalculator.Compute(UnitX(ameAndCtlSources: false), Balanced);

        Assert.Equal(IcrCategoryStatus.Unavailable, Category(node, RiskIndexCategory.Ame).Status);
        Assert.Equal(IcrCategoryStatus.Unavailable, Category(node, RiskIndexCategory.Ctl).Status);
        Near(70.23, node.Unfloored!.Value);
        Assert.Equal(70, node.Displayed);
        Assert.Equal(2, node.BandIndex);
        Near(0.670, node.Coverage, 0.001);
        Near(52.67, node.IntervalLow!.Value);
        Near(79.40, node.IntervalHigh!.Value);
        Assert.Equal(IcrDisplayState.WithInterval, node.DisplayState);
    }

    // ---------- §11.6: presets and tornado ----------

    [Theory]
    [InlineData("balanced", 63.7, 72.0)]
    [InlineData("register", 65.8, 72.0)]
    [InlineData("ctem", 64.0, 72.0)]
    [InlineData("conservative", 71.7, 81.0)]
    [InlineData("pure", 33.0, 18.9)]
    public void TestPresets(string preset, double unitX, double canonical)
    {
        var p = preset switch
        {
            "register" => Balanced with { Omega = new RiskIndexCategoryWeights { Reg = 0.45, Exp = 0.20, Ame = 0.10, Ctl = 0.05, Gov = 0.20 } },
            "ctem" => Balanced with { Omega = new RiskIndexCategoryWeights { Reg = 0.20, Exp = 0.40, Ame = 0.20, Ctl = 0.10, Gov = 0.10 } },
            "conservative" => Balanced with { P = 8, Tau = 0.90, TauG = 0.85 },
            "pure" => Balanced with { P = 1, Tau = 0, TauG = 0 },
            _ => Balanced
        };

        Near(unitX, IcrNodeCalculator.Compute(UnitX(), p, 0).Unfloored!.Value, 0.05);
        Near(canonical, ExpOf(Canonical(), p).Value, 0.05);
    }

    [Theory]
    [InlineData("tau", 0.64, -7.99)]
    [InlineData("tau", 0.95, 11.94)]
    [InlineData("ctl", 0.08, 0.97)]
    [InlineData("ctl", 0.12, -0.97)]
    [InlineData("exp", 0.24, -0.90)]
    [InlineData("exp", 0.36, 0.90)]
    [InlineData("reg", 0.24, -0.35)]
    [InlineData("reg", 0.36, 0.35)]
    public void TestTornado(string parameter, double value, double delta)
    {
        var baseline = IcrNodeCalculator.Compute(UnitX(), Balanced, 0).Unfloored!.Value;
        var p = parameter == "tau" ? Balanced with { Tau = value } : Balanced with { Omega = Rescale(parameter, value) };

        Near(delta, IcrNodeCalculator.Compute(UnitX(), p, 0).Unfloored!.Value - baseline);
    }

    /// <summary>One ω moved, the others rescaled proportionally to keep Σω = 1 (S39 §11.6).</summary>
    private static RiskIndexCategoryWeights Rescale(string category, double value)
    {
        var o = Balanced.Omega;
        var old = category switch { "ctl" => o.Ctl, "exp" => o.Exp, _ => o.Reg };
        var f = (1 - value) / (1 - old);
        return new RiskIndexCategoryWeights
        {
            Reg = category == "reg" ? value : o.Reg * f,
            Exp = category == "exp" ? value : o.Exp * f,
            Ame = o.Ame * f,
            Ctl = category == "ctl" ? value : o.Ctl * f,
            Gov = o.Gov * f
        };
    }
}
