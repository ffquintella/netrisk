using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using Model.Continuity;
using Tools.Continuity;
using Xunit;

namespace Tools.Tests.Continuity;

/// <summary>
/// Stage 9.3 (S43 §8, C1–C13) — the continuity dependency graph and its cascade.
///
/// The cases the methodology names are here: a cyclic cascade does not recurse forever (C7–C9, the ring
/// of ten thousand proving the walk is iterative), and a node without a BIA is absent — it imposes no
/// requirement of 0 (C4) and it is not an infinite objective that conflicts (C5).
/// </summary>
[TestSubject(typeof(ContinuityGraph))]
public class ContinuityGraphTest
{
    private static ContinuityNode Process(int id, int? rto = null, int? rpo = null, int? mtpd = null, int? crit = null) =>
        new(id, "businessProcess", $"P{id}", true, mtpd, rto, rpo, crit);

    private static ContinuityNode Service(int id, int? rto = null, int? rpo = null, int? mtpd = null) =>
        new(id, "itService", $"S{id}", true, mtpd, rto, rpo);

    private static ContinuityGraph Graph(IEnumerable<ContinuityNode> nodes, params (int Dependent, int Provider)[] edges) =>
        new(nodes, edges);

    /// <summary>C1 — P → S1 → S2: dependents and providers with their depths.</summary>
    [Fact]
    public void TestC1_DependentsAndProvidersCarryTheirDepth()
    {
        var graph = Graph([Process(1), Service(2), Service(3)], (1, 2), (2, 3));

        Assert.Equal(new Dictionary<int, int> { [2] = 1, [1] = 2 }, graph.Dependents(3));
        Assert.Equal(new Dictionary<int, int> { [2] = 1, [3] = 2 }, graph.Providers(1));
        Assert.Empty(graph.Dependents(1));
        Assert.Empty(graph.Providers(3));
    }

    /// <summary>C2 — the requirement is the transitive minimum, with the dependent that sets it.</summary>
    [Fact]
    public void TestC2_TheRequirementIsTheTransitiveMinimum()
    {
        var graph = Graph([Process(1, rto: 240), Service(2, rto: 480), Service(3, rto: 120)], (1, 2), (2, 3));

        Assert.Equal((240, 1), graph.Requirement(3, ContinuityObjective.Rto));
        Assert.Equal((240, 1), graph.Requirement(2, ContinuityObjective.Rto));
        Assert.Null(graph.Requirement(1, ContinuityObjective.Rto));
    }

    /// <summary>C3 — a provider looser than its dependents conflicts, bound to the dependent; a tighter
    /// one does not.</summary>
    [Fact]
    public void TestC3_ALooserProviderConflicts()
    {
        var graph = Graph([Process(1, rto: 240), Service(2, rto: 480), Service(3, rto: 120)], (1, 2), (2, 3));

        var conflict = Assert.Single(graph.Cascade(2).Conflicts);
        Assert.Equal((ContinuityObjective.Rto, 480, 240, 1), (conflict.Objective, conflict.DeclaredMinutes,
            conflict.RequiredMinutes, conflict.BindingEntityId));
        Assert.Equal("P1", conflict.BindingName);

        Assert.True(graph.HasConflict(2, ContinuityObjective.Rto));
        Assert.False(graph.HasConflict(3, ContinuityObjective.Rto));
        Assert.Empty(graph.Cascade(3).Conflicts);
    }

    /// <summary>C4 (the methodology's case) — a dependent with no BIA imposes no requirement: not 0, which
    /// would make every provider conflict.</summary>
    [Fact]
    public void TestC4_ADependentWithoutBiaImposesNothing()
    {
        var graph = Graph([Process(1), Service(2, rto: 60)], (1, 2));

        Assert.Null(graph.Requirement(2, ContinuityObjective.Rto));
        Assert.False(graph.HasConflict(2, ContinuityObjective.Rto));
        Assert.Empty(graph.Cascade(2).Conflicts);
    }

    /// <summary>C5 (the methodology's case) — a provider with no RTO under a requirement is "requirement
    /// without objective": not a conflict (absent is not infinite), and not met.</summary>
    [Fact]
    public void TestC5_AProviderWithoutAnObjectiveUnderARequirementIsNeitherConflictNorMet()
    {
        var graph = Graph([Process(1, rto: 240), Service(2)], (1, 2));

        Assert.False(graph.HasConflict(2, ContinuityObjective.Rto));
        Assert.True(graph.HasRequirementWithoutObjective(2, ContinuityObjective.Rto));

        var cascade = graph.Cascade(2);
        Assert.Empty(cascade.Conflicts);
        Assert.Equal([ContinuityObjective.Rto], cascade.ObjectivesWithoutValueUnderRequirement);
    }

    /// <summary>C6 — a dependent with only an MTPD limits its providers' RTO by it; the RPO has no such
    /// fallback.</summary>
    [Fact]
    public void TestC6_TheMtpdLimitsTheRtoButNotTheRpo()
    {
        var graph = Graph([Process(1, mtpd: 480), Service(2, rto: 600, rpo: 600)], (1, 2));

        Assert.Equal((480, 1), graph.Requirement(2, ContinuityObjective.Rto));
        Assert.True(graph.HasConflict(2, ContinuityObjective.Rto));
        Assert.Null(graph.Requirement(2, ContinuityObjective.Rpo));
        Assert.False(graph.HasConflict(2, ContinuityObjective.Rpo));
    }

    /// <summary>C7 (the methodology's case) — P ↔ S terminates; each is the other's cycle member and
    /// requirement, and neither is its own dependent.</summary>
    [Fact]
    public void TestC7_ATwoNodeCycleTerminates()
    {
        var graph = Graph([Process(1, rto: 240), Service(2, rto: 120)], (1, 2), (2, 1));

        Assert.Equal([2], graph.CycleMembers(1));
        Assert.Equal([1], graph.CycleMembers(2));
        Assert.False(graph.Dependents(1).ContainsKey(1));
        Assert.False(graph.Providers(2).ContainsKey(2));

        Assert.Equal((120, 2), graph.Requirement(1, ContinuityObjective.Rto));
        Assert.Equal((240, 1), graph.Requirement(2, ContinuityObjective.Rto));
        Assert.True(graph.HasConflict(1, ContinuityObjective.Rto));
        Assert.False(graph.HasConflict(2, ContinuityObjective.Rto));
    }

    /// <summary>C8 — a three-node cycle with a tail: only the three are members.</summary>
    [Fact]
    public void TestC8_OnlyTheCycleNodesAreMembers()
    {
        // 1 → 2 → 3 → 1, and 4 → 1 (a tail depending on the cycle), 3 → 5 (the cycle depending on a leaf).
        var graph = Graph([Service(1), Service(2), Service(3), Process(4), Service(5)],
            (1, 2), (2, 3), (3, 1), (4, 1), (3, 5));

        Assert.Equal([2, 3], graph.CycleMembers(1));
        Assert.Empty(graph.CycleMembers(4));
        Assert.Empty(graph.CycleMembers(5));
        Assert.Equal([1, 2, 3, 4], graph.Dependents(5).Keys.OrderBy(k => k));
    }

    /// <summary>
    /// C9 — a ring of ten thousand nodes: the walk terminates without a <see cref="StackOverflowException"/>
    /// (an unbounded walk would not), and every sampled node has the other 9 999 as
    /// cycle members.
    /// </summary>
    [Fact]
    public void TestC9_ATenThousandNodeRingTerminates()
    {
        const int size = 10_000;
        var nodes = Enumerable.Range(0, size).Select(i => Service(i, rto: 60));
        var edges = Enumerable.Range(0, size).Select(i => (i, (i + 1) % size)).ToArray();

        var graph = Graph(nodes, edges);
        var clock = Stopwatch.StartNew();

        foreach (var sample in new[] { 0, 1, size / 2, size - 1 })
        {
            Assert.Equal(size - 1, graph.CycleMembers(sample).Count);
            Assert.Equal(size - 1, graph.Dependents(sample)[(sample + 1) % size]);
        }

        Assert.Empty(graph.Cascade(0).Conflicts);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The ring took {clock.Elapsed}.");
    }

    /// <summary>C10 — a diamond: the shared node appears once, at the shorter depth.</summary>
    [Fact]
    public void TestC10_ADiamondCountsTheSharedNodeOnce()
    {
        // 1 → 2, 1 → 3, 2 → 4, 3 → 4, and 1 → 4 directly.
        var graph = Graph([Process(1), Service(2), Service(3), Service(4)], (1, 2), (1, 3), (2, 4), (3, 4), (1, 4));

        var providers = graph.Providers(1);
        Assert.Equal(3, providers.Count);
        Assert.Equal(1, providers[4]);
        Assert.Single(graph.Cascade(1).Providers, n => n.EntityId == 4);
    }

    /// <summary>C11 — an edge to a node that is not loaded, and a self-edge, are dropped without an exception.</summary>
    [Fact]
    public void TestC11_UnknownAndSelfEdgesAreDropped()
    {
        var graph = Graph([Process(1, rto: 60)], (1, 999), (999, 1), (1, 1));

        Assert.Empty(graph.Providers(1));
        Assert.Empty(graph.Dependents(1));
        Assert.Empty(graph.Dependents(999));
    }

    /// <summary>C12 — the impact: the smallest MTPD among the dependents, and how many of them are
    /// critical processes.</summary>
    [Fact]
    public void TestC12_TheImpactNamesTheTightestMtpdAndTheCriticalDependents()
    {
        var graph = Graph(
        [
            Process(1, mtpd: 480, crit: 4), Process(2, mtpd: 120, crit: 5), Process(3, mtpd: 60, crit: 2),
            Service(4, mtpd: 30), Service(10)
        ], (1, 10), (2, 10), (3, 10), (4, 10));

        var cascade = graph.Cascade(10);

        Assert.Equal(30, cascade.TightestDependentMtpd!.Minutes);
        Assert.Equal(4, cascade.TightestDependentMtpd.BindingEntityId);
        Assert.Equal(2, cascade.CriticalDependentCount);
    }

    /// <summary>C13 — the binding dependent is the smallest value, then the shallowest, then the lowest
    /// id; the lists come out in depth then id order.</summary>
    [Fact]
    public void TestC13_TiesAreBrokenDeterministically()
    {
        // 5 and 3 both require 120 of 10 directly; 1 requires 120 through 3 (deeper).
        var graph = Graph([Process(1, rto: 120), Process(3, rto: 120), Process(5, rto: 120), Service(10)],
            (5, 10), (3, 10), (1, 3));

        Assert.Equal((120, 3), graph.Requirement(10, ContinuityObjective.Rto));
        Assert.Equal([3, 5, 1], graph.Cascade(10).Dependents.Select(n => n.EntityId));
        Assert.Equal([1, 1, 2], graph.Cascade(10).Dependents.Select(n => n.Depth));
    }
}
