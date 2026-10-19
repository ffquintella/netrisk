using System.Linq;
using System.Threading.Tasks;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// One full day for a profile (the snapshot job's entry point, T244–T248): the pre-computed nodes with set-based
/// membership, the interim process and activity rules, the dimension nodes, Unassigned, and "what moved" against D−1.
/// </summary>
[TestSubject(typeof(RiskIndexDayCalculator))]
public class RiskIndexDayCalculatorTest : RiskIndexSeedTestBase
{
    private static readonly RiskIndexParameters P = new();

    private IRiskIndexDayCalculator Calculator => new RiskIndexDayCalculator(GetService<IDalService>());

    private static RiskIndexDayRequest Request(System.DateTime? at = null, RiskIndexPreviousDay? previous = null) =>
        new() { Parameters = P, SnapshotInstant = at ?? Now, Previous = previous };

    private static RiskIndexNodeDay Node(RiskIndexDay day, string reference) =>
        day.Nodes.Single(n => n.Node.ScopeRef == reference);

    [Fact]
    public async Task TestTheDayHasEveryPrecomputedNode()
    {
        var day = await Calculator.ComputeAsync(Request());

        var refs = day.Nodes.Select(n => n.Node.ScopeRef).ToHashSet();
        foreach (var expected in new[] { "org", "1", "10", "12", "20", "30", "40", "u", "Application", "ExternalDomain",
                     RiskIndexSources.V1Device, RiskIndexSources.SscTotal, RiskIndexSources.TenableNessus, RiskIndexSources.Importers })
            Assert.Contains(expected, refs);

        Assert.All(day.Nodes.Where(n => n.Node.ScopeKind == RiskIndexScopeKind.Organization), n => Assert.NotNull(n.Result.Unfloored));
        Assert.Null(Node(day, "org").Change);
    }

    [Fact]
    public async Task TestTheUnitCountsEachObjectOnceAndHasEveryCategory()
    {
        var day = await Calculator.ComputeAsync(Request());
        var unit = Node(day, UnitX.ToString());

        Assert.Contains(new IcrObjectKey(RiskIndexObjectKind.Host, ProcessHost.ToString()), unit.Members.Objects);
        Assert.Contains(new IcrObjectKey(RiskIndexObjectKind.Application, Portal.ToString()), unit.Members.Objects);
        Assert.Single(unit.Members.Objects.Where(k => k.Kind == RiskIndexObjectKind.Scenario));
        Assert.True(unit.Members.ClosureLinkedOnlyThroughEav);
        foreach (var category in new[] { RiskIndexCategory.Reg, RiskIndexCategory.Exp, RiskIndexCategory.Ame, RiskIndexCategory.Ctl, RiskIndexCategory.Gov })
            Assert.Equal(IcrCategoryStatus.Available, unit.Result.Categories.Single(c => c.Category == category).Status);

        // The tail guarantee across the drill-down: the unit keeps 100·τ·R of APP-03 (S39 §10.3).
        var exp = unit.Result.Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        Assert.True(exp.Value >= 100 * P.Tau * 0.928 - 0.01);
        Assert.False(unit.Result.NoCriticalProcess);
    }

    [Fact]
    public async Task TestAnInterimProcessScoresOnlyItsApplicationsInExposure()
    {
        var day = await Calculator.ComputeAsync(Request());
        var process = Node(day, Enrolment.ToString());

        var exp = process.Result.Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        Assert.True(exp.Partial);
        Assert.Equal("requires M39", exp.Note);
        Assert.Equal([new IcrObjectKey(RiskIndexObjectKind.Application, Portal.ToString())], exp.Parts.Select(p => p.Key));
        Assert.Contains(new IcrObjectKey(RiskIndexObjectKind.Host, ProcessHost.ToString()), process.Members.Objects);
    }

    [Fact]
    public async Task TestDimensionNodesHoldAssetsOnly()
    {
        var day = await Calculator.ComputeAsync(Request());

        var device = Node(day, RiskIndexSources.V1Device);
        Assert.Equal(IcrCategoryStatus.NotApplicable, device.Result.Categories.Single(c => c.Category == RiskIndexCategory.Reg).Status);
        Assert.Equal(IcrCategoryStatus.NotApplicable, device.Result.Categories.Single(c => c.Category == RiskIndexCategory.Ame).Status);
        Assert.All(device.Members.Objects, k => Assert.Equal(RiskIndexObjectKind.Host, k.Kind));

        var applications = Node(day, nameof(RiskIndexAssetClass.Application));
        Assert.Equal(RiskIndexScopeKind.AssetClass, applications.Node.ScopeKind);
        Assert.All(applications.Members.Objects, k => Assert.NotEqual(RiskIndexObjectKind.Scenario, k.Kind));
    }

    [Fact]
    public async Task TestUnassignedHoldsTheObjectsWithoutAttributionAndItsShareIsPublished()
    {
        var day = await Calculator.ComputeAsync(Request());

        var unassigned = Node(day, NodeMembership.UnassignedRef);
        Assert.Contains(new IcrObjectKey(RiskIndexObjectKind.UnassignedGroup, "findings:sarif:77"), unassigned.Members.Objects);
        Assert.True(unassigned.Members.HasUnscopedObjects);
        Assert.Equal(0.7 / 7.45, day.UnassignedWeightFraction, 6);
        Assert.Contains(Invalid, day.InvalidCriticality);
    }

    [Fact]
    public async Task TestWhatMovedWhenTheHighFindingOfTheApplicationIsFixed()
    {
        var first = await Calculator.ComputeAsync(Request());

        await using (var db = OpenContext())
        {
            var high = await db.Vulnerabilities.SingleAsync(v => v.RuleId == "sql-injection");
            high.LifecycleStatus = FindingStatus.Mitigated;
            await db.SaveChangesAsync();
        }

        var second = await Calculator.ComputeAsync(Request(Now.AddDays(1),
            new RiskIndexPreviousDay { Objects = first.Objects }));
        var change = Node(second, "org").Change!;

        Assert.NotNull(change.Change);
        Assert.Equal(change.Change!.Value, change.CoverageEffect + change.Entries.Sum(e => e.Total) + change.Unattributed, 9);
        Assert.True(change.Entries.Where(e => e.Key == new IcrObjectKey(RiskIndexObjectKind.Application, Portal.ToString()) &&
                                              e.Category == RiskIndexCategory.Exp).Sum(e => e.Total) < 0);
        Assert.Equal(0, change.RestructuringEffect);
    }

    [Fact]
    public async Task TestAnObjectMovedBetweenEntitiesIsRestructuringNotRisk()
    {
        var first = await Calculator.ComputeAsync(Request());

        await using (var db = OpenContext())
        {
            var host = await db.Hosts.SingleAsync(h => h.Id == LabHost);
            host.EntityId = Invalid;
            await db.SaveChangesAsync();
        }

        var second = await Calculator.ComputeAsync(Request(Now, new RiskIndexPreviousDay { Objects = first.Objects }));
        // The host leaves Unit X's closure (Lab is in it, the other unit is not): its share is restructuring.
        var unit = Node(second, UnitX.ToString()).Change!;

        Assert.Contains(unit.Entries, e => e.Reattribution && e.Key.Ref == LabHost.ToString());
        Assert.DoesNotContain(unit.Entries, e => !e.Reattribution && e.Key.Ref == LabHost.ToString());
        Assert.NotEqual(0, unit.RestructuringEffect);
    }

    [Fact]
    public void TestTheServiceIsRegisteredByAddRiskIndex()
    {
        var services = new ServiceCollection();
        services.AddSingleton(GetService<IDalService>());
        services.AddRiskIndex();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<RiskIndexDayCalculator>(provider.GetRequiredService<IRiskIndexDayCalculator>());
    }
}
