using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// Stage 9.1 (S41 §8, G1–G7) — the typed "serves" graph the chain's inferred queries and the coverage
/// metric walk.
///
/// The fixture is one small organisation:
/// <code>
///   objective 1
///     └─ process 10 (strategicObjectives = 1, applications = 40)
///          ├─ activity 11 (Parent = 10)
///          ├─ service 20 (processes = 10, applications = 30, data = 50)
///          │    ├─ application 30 ── module 31 (parentApplication = 30)
///          │    └─ data 50
///          └─ application 40 ── module 41 (parentApplication = 40)
///   person 60 (not a chain node)
/// </code>
/// </summary>
[TestSubject(typeof(RiskChainGraph))]
public class RiskChainGraphTest
{
    private static KeyValuePair<string, string> P(string type, string value) => new(type, value);

    private static RiskChainNode Node(int id, string definition, int? parent = null,
        params KeyValuePair<string, string>[] properties) =>
        new(id, definition, parent, properties.Append(P("name", $"{definition}-{id}")));

    private static List<RiskChainNode> Organisation() =>
    [
        Node(1, "strategicObjective"),
        Node(10, "businessProcess", null, P("strategicObjectives", "1"), P("applications", "40")),
        Node(11, "activity", 10),
        Node(20, "itService", null, P("processes", "10"), P("applications", "30"), P("data", "50")),
        Node(30, "application"),
        Node(31, "applicationModule", 30, P("parentApplication", "30")),
        Node(40, "application"),
        Node(41, "applicationModule", 40, P("parentApplication", "40")),
        Node(50, "organizationData"),
        Node(60, "person")
    ];

    /// <summary>G1 — every kind of node below an objective is reached, at its shortest depth.</summary>
    [Fact]
    public void TestBelowAnObjectiveReachesEveryNodeOfItsChain()
    {
        var graph = new RiskChainGraph(Organisation());

        var below = graph.Below(1);

        Assert.Equal(1, below[10]); // process
        Assert.Equal(2, below[11]); // activity of the process
        Assert.Equal(2, below[20]); // service serving the process
        Assert.Equal(3, below[30]); // application of the service
        Assert.Equal(3, below[50]); // data of the service
        Assert.Equal(2, below[40]); // application of the process, no service in between
        Assert.Equal(4, below[31]); // module of the service's application
        Assert.Equal(3, below[41]); // module of the process's application

        Assert.DoesNotContain(1, below.Keys);
        Assert.DoesNotContain(60, below.Keys);
        Assert.Equal(8, below.Count);
    }

    /// <summary>G2 — a missing middle link does not break the chain: an application used directly by a
    /// process, with no service at all, still reaches the objective.</summary>
    [Fact]
    public void TestAnApplicationWithoutAServiceStillReachesTheObjective()
    {
        var graph = new RiskChainGraph(
        [
            Node(90, "strategicObjective"),
            Node(80, "businessProcess", null, P("strategicObjectives", "90"), P("applications", "70")),
            Node(70, "application")
        ]);

        var below = graph.Below(90);

        Assert.Equal(1, below[80]);
        Assert.Equal(2, below[70]);
    }

    /// <summary>G3 — a reference to an id that does not exist, or that is not a number, is dropped
    /// without an exception. Entity saves do not check references, so this is stored data.</summary>
    [Fact]
    public void TestADanglingReferenceIsDropped()
    {
        var graph = new RiskChainGraph(
        [
            Node(1, "strategicObjective"),
            Node(10, "businessProcess", null, P("strategicObjectives", "999"), P("strategicObjectives", "abc"),
                P("strategicObjectives", "1"), P("applications", ""))
        ]);

        Assert.Equal([1], graph.DirectlyAbove(10).ToList());
        Assert.Equal([10], graph.Below(1).Keys.ToList());
        Assert.Empty(graph.Below(999));
    }

    /// <summary>G4 — a reference to a node of the wrong type is dropped: <c>processes</c> naming an
    /// application does not make the service serve that application.</summary>
    [Fact]
    public void TestAReferenceToTheWrongTypeIsDropped()
    {
        var graph = new RiskChainGraph(
        [
            Node(21, "itService", null, P("processes", "30"), P("data", "40")),
            Node(30, "application"),
            Node(40, "application")
        ]);

        Assert.Empty(graph.DirectlyAbove(21));
        Assert.Empty(graph.DirectlyBelow(21));
        Assert.DoesNotContain(21, graph.Below(30).Keys);
    }

    /// <summary>G5 — cyclic and self-referencing data terminates: a Parent loop between two activities,
    /// an activity that is its own parent, and a service that names itself.</summary>
    [Fact]
    public void TestCyclesAndSelfReferencesTerminate()
    {
        var graph = new RiskChainGraph(
        [
            Node(10, "businessProcess"),
            Node(100, "activity", 101),
            Node(101, "activity", 100),
            Node(102, "activity", 102),
            Node(103, "activity", 10),
            Node(20, "itService", null, P("processes", "20"), P("processes", "10"))
        ]);

        Assert.Empty(graph.Below(100));
        Assert.Empty(graph.Below(101));
        Assert.Empty(graph.Below(102));
        Assert.Empty(graph.DirectlyAbove(102));

        var below = graph.Below(10);
        Assert.Equal(2, below.Count);
        Assert.Equal(1, below[103]);
        Assert.Equal(1, below[20]);
        Assert.DoesNotContain(20, graph.DirectlyAbove(20));
    }

    /// <summary>G6 — inference only goes up: a service's risks count for the process it serves, never
    /// the other way round.</summary>
    [Fact]
    public void TestBelowAServiceDoesNotContainTheProcessItServes()
    {
        var graph = new RiskChainGraph(Organisation());

        var below = graph.Below(20);

        Assert.DoesNotContain(10, below.Keys);
        Assert.DoesNotContain(1, below.Keys);
        Assert.Equal(new[] { 30, 31, 50 }, below.Keys.OrderBy(k => k).ToArray());
    }

    /// <summary>G7 — the "via" of an inferred match is deterministic: the shallowest node, then the
    /// lowest id, whatever order the links arrive in.</summary>
    [Fact]
    public void TestViaIsTheShallowestThenTheLowestId()
    {
        var graph = new RiskChainGraph(Organisation());
        var below = graph.Below(1);

        // 20 and 40 are both at depth 2; 30 is deeper; 11 is at depth 2 too and has the lowest id.
        Assert.Equal((20, 2), RiskChainGraph.PickVia([30, 40, 20], below));
        Assert.Equal((20, 2), RiskChainGraph.PickVia([20, 30, 40], below));
        Assert.Equal((11, 2), RiskChainGraph.PickVia([40, 11, 20], below));
        Assert.Equal((10, 1), RiskChainGraph.PickVia([31, 10], below));

        // Nothing below the queried node: no via.
        Assert.Null(RiskChainGraph.PickVia([60, 999], below));
        Assert.Null(RiskChainGraph.PickVia([], below));
    }

    [Fact]
    public void TestTheLevelOfEachChainTypeAndOfANonChainType()
    {
        Assert.Equal(DAL.Enums.RiskChainLevel.Objective, RiskChainSchema.LevelOf("strategicObjective"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Process, RiskChainSchema.LevelOf("businessProcess"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Process, RiskChainSchema.LevelOf("activity"));
        Assert.Equal(DAL.Enums.RiskChainLevel.ItService, RiskChainSchema.LevelOf("itService"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Data, RiskChainSchema.LevelOf("organizationData"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Data, RiskChainSchema.LevelOf("organizationDataGroup"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Asset, RiskChainSchema.LevelOf("application"));
        Assert.Equal(DAL.Enums.RiskChainLevel.Asset, RiskChainSchema.LevelOf("applicationModule"));

        foreach (var outside in new[] { "organization", "person", "team", "organizationUnit",
                     "subOrganizationUnit", "securityClassificationLevel", "", null })
            Assert.Null(RiskChainSchema.LevelOf(outside));
    }
}
