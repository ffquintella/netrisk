using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The entity closure and set-based node membership (S39 §10.1–§10.3, T246; S40 §12.2 <c>EntityClosureResolverTest</c>):
/// the tree plus the multi-valued <c>organizationUnit</c> EAV link, activities under processes, the processes'
/// applications, divergent links, orphan applications, Unassigned, and scope members.
/// </summary>
[TestSubject(typeof(EntityMap))]
public class EntityClosureResolverTest
{
    private const int Org = 1, UnitX = 10, UnitY = 11, Lab = 12;
    private const int Enrolment = 20, Research = 21, Divergent = 23, Activity = 22;
    private const int Portal = 30, Orphan = 31, Module = 32;

    private static EntityMap Map(params EntityPropertyRow[] extra) => EntityMap.Build(
    [
        new EntityMapRow(Org, "organization", null),
        new EntityMapRow(UnitX, "organizationUnit", Org),
        new EntityMapRow(UnitY, "organizationUnit", Org),
        new EntityMapRow(Lab, "subOrganizationUnit", UnitX),
        new EntityMapRow(Enrolment, "businessProcess", null),
        new EntityMapRow(Research, "businessProcess", null),
        new EntityMapRow(Activity, "activity", Enrolment),
        new EntityMapRow(Divergent, "businessProcess", UnitY),
        new EntityMapRow(Portal, "application", null),
        new EntityMapRow(Module, "applicationModule", Portal),
        new EntityMapRow(Orphan, "application", null)
    ],
    [
        new EntityPropertyRow(Enrolment, "organizationUnit", UnitX.ToString()),
        new EntityPropertyRow(Enrolment, "applications", Portal.ToString()),
        new EntityPropertyRow(Research, "organizationUnit", UnitX.ToString()),
        new EntityPropertyRow(Research, "organizationUnit", UnitY.ToString()),
        new EntityPropertyRow(Divergent, "organizationUnit", UnitX.ToString()),
        new EntityPropertyRow(Enrolment, "criticality", "5"),
        new EntityPropertyRow(Research, "criticality", "3"),
        new EntityPropertyRow(Portal, "internetFacing", "True"),
        .. extra
    ]);

    [Fact]
    public void TestAUnitClosesOverItsSubtreeItsEavProcessesTheirActivitiesAndTheirApplications()
    {
        var closure = Map().Closure(UnitX);

        Assert.Equal(new[] { UnitX, Lab, Enrolment, Activity, Research, Divergent, Portal, Module }.OrderBy(i => i),
            closure.Members.OrderBy(i => i));
        Assert.True(closure.LinkedOnlyThroughEav);
    }

    [Fact]
    public void TestAProcessInSeveralUnitsEntersEachWhole()
    {
        var map = Map();

        Assert.Contains(Research, map.Closure(UnitX).Members);
        Assert.Contains(Research, map.Closure(UnitY).Members);
        Assert.Equal(new[] { UnitY, Research, Divergent }.OrderBy(i => i), map.Closure(UnitY).Members.OrderBy(i => i));
    }

    [Fact]
    public void TestAProcessClosesOverItsActivitiesAndItsApplications()
    {
        var closure = Map().Closure(Enrolment);

        Assert.Equal(new[] { Enrolment, Activity, Portal, Module }.OrderBy(i => i), closure.Members.OrderBy(i => i));
    }

    [Fact]
    public void TestATreeOnlyClosureIsNotMarkedEav() =>
        Assert.False(Map().Closure(Lab).LinkedOnlyThroughEav);

    [Fact]
    public void TestDivergentLinksAndOrphanApplicationsAreReported()
    {
        var map = Map();

        var divergent = Assert.Single(map.DivergentLinks());
        Assert.Equal((Divergent, UnitY), (divergent.ProcessId, divergent.TreeParent));
        Assert.Equal([UnitX], divergent.EavUnits);
        Assert.Equal([Orphan], map.OrphanApplications());
    }

    [Theory]
    [InlineData("9")]
    [InlineData("0")]
    [InlineData("-2")]
    public void TestACriticalityOutsideOneToFiveIsIgnoredAndReported(string value)
    {
        var map = Map(new EntityPropertyRow(UnitY, "criticality", value));

        Assert.Null(map.Criticality(UnitY));
        Assert.Equal([UnitY], map.InvalidCriticality);
        Assert.Equal(5, map.Criticality(Enrolment));
        Assert.True(map.InternetFacing(Portal));
    }

    [Fact]
    public void TestSeveralIdsInOneValueAreAllLinks()
    {
        var map = EntityMap.Build(
            [new EntityMapRow(1, "organizationUnit", null), new EntityMapRow(2, "organizationUnit", null),
                new EntityMapRow(3, "businessProcess", null)],
            [new EntityPropertyRow(3, "organizationUnit", "1,2")]);

        Assert.Contains(3, map.Closure(1).Members);
        Assert.Contains(3, map.Closure(2).Members);
    }

    [Fact]
    public void TestAParentCycleNeitherHangsNorWidensTheClosure()
    {
        var map = EntityMap.Build(
            [new EntityMapRow(1, "organizationUnit", 2), new EntityMapRow(2, "organizationUnit", 1),
                new EntityMapRow(3, "subOrganizationUnit", 1)],
            []);

        Assert.Equal([1], map.Closure(1).Members);
    }

    private static ObjectAttribution Attributed(string reference, int? scope, params int[] entities) =>
        new(new IcrObjectKey(RiskIndexObjectKind.Host, reference), entities, scope);

    [Fact]
    public void TestNodeMembershipIsTheDeduplicatedUnionOverTheClosure()
    {
        var objects = new List<ObjectAttribution>
        {
            Attributed("scenario-on-activity", Activity, Activity),
            Attributed("lab-host", Lab, Lab),
            Attributed("portal-app", Portal, Portal),
            Attributed("shared", UnitY, Enrolment, Research),
            Attributed("nowhere", null),
            Attributed("deleted-entity", 999, 999)
        };

        var nodes = NodeMembership.Resolve(Map(), objects).ToDictionary(n => n.Node.ScopeRef);
        IEnumerable<string> Refs(string node) => nodes[node].Objects.Select(k => k.Ref).OrderBy(r => r);

        Assert.Equal(new[] { "lab-host", "portal-app", "scenario-on-activity", "shared" }, Refs(UnitX.ToString()));
        Assert.Equal(new[] { "portal-app", "scenario-on-activity", "shared" }, Refs(Enrolment.ToString()));
        Assert.Equal(new[] { "shared" }, Refs(Research.ToString()));
        Assert.Equal(new[] { "portal-app" }, Refs(Portal.ToString()));
        Assert.Equal(new[] { "deleted-entity", "nowhere" }, Refs(NodeMembership.UnassignedRef));
        Assert.Equal(6, nodes[NodeMembership.RootRef].Objects.Count);
        Assert.DoesNotContain(Activity.ToString(), nodes.Keys);
        Assert.DoesNotContain(Orphan.ToString(), nodes.Keys.Where(k => nodes[k].Objects.Count > 0));
    }

    [Fact]
    public void TestScopeMembersAreTheScopeEntitiesOfTheNodesObjects()
    {
        var objects = new List<ObjectAttribution>
        {
            Attributed("a", Lab, Lab),
            Attributed("b", UnitY, Enrolment),
            Attributed("c", null, UnitX)
        };

        var unit = NodeMembership.Resolve(Map(), objects).Single(n => n.Node.EntityId == UnitX);

        Assert.Equal(new[] { Lab, UnitY }.OrderBy(i => i), unit.ScopeEntities.OrderBy(i => i));
        Assert.True(unit.HasUnscopedObjects);
        Assert.Contains(Enrolment, unit.Closure);
        Assert.Equal("organizationUnit", unit.Node.EntityType);
    }
}
