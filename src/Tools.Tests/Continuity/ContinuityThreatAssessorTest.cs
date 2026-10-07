using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Model.Continuity;
using Tools.Continuity;
using Xunit;

namespace Tools.Tests.Continuity;

/// <summary>
/// Stage 9.3 (S43 §8, T1–T10) — the weighted threat to a node's RTO/RPO, the flag 4 basis.
///
/// The decision under test (2026-10-07): an unverified objective counts as a threat at the configured
/// weight, half of a confirmed one by default — and, by the adopted reading (S43 D16), the node's weight
/// is the highest item weight, so two unverified items do not add up to a confirmed threat (T4).
/// </summary>
[TestSubject(typeof(ContinuityThreatAssessor))]
public class ContinuityThreatAssessorTest
{
    private const decimal Half = 0.5m;

    private static ContinuityNode Process(int id, int? rto = null, int? rpo = null, int? mtpd = null) =>
        new(id, "businessProcess", $"P{id}", true, mtpd, rto, rpo, 5);

    private static ContinuityNode Service(int id, int? rto = null, int? rpo = null) =>
        new(id, "itService", $"S{id}", true, null, rto, rpo);

    /// <summary>Statuses keyed by (node, objective); anything not listed reads Absent.</summary>
    private static Func<int, ContinuityObjective, ObjectiveVerificationStatus> Statuses(
        params (int Node, ContinuityObjective Objective, ObjectiveVerificationStatus Status)[] statuses)
    {
        var map = statuses.ToDictionary(s => (s.Node, s.Objective), s => s.Status);
        return (node, objective) => map.GetValueOrDefault((node, objective), ObjectiveVerificationStatus.Absent);
    }

    private static ContinuityThreatDto Assess(ContinuityGraph graph, int node,
        Func<int, ContinuityObjective, ObjectiveVerificationStatus> statuses, decimal weight = Half) =>
        ContinuityThreatAssessor.Assess(graph, node, statuses, weight);

    /// <summary>T1 (decision 1) — a declared RTO without a test is an unverified threat at weight 0.5.</summary>
    [Fact]
    public void TestT1_AnUnverifiedRtoIsAThreatAtHalfWeight()
    {
        var graph = new ContinuityGraph([Process(1, rto: 240)], []);

        var threat = Assess(graph, 1, Statuses((1, ContinuityObjective.Rto, ObjectiveVerificationStatus.Unverified)));

        var item = Assert.Single(threat.Items);
        Assert.Equal((ContinuityThreatReason.Unverified, ContinuityObjective.Rto, ContinuityThreatClass.NotVerified, Half),
            (item.Reason, item.Objective, item.Class, item.Weight));
        Assert.Equal(Half, threat.ThreatWeight);
        Assert.True(threat.IsThreatened);
        Assert.Equal(Half, threat.UnverifiedWeight);
    }

    /// <summary>T2 — a declared RTO that a test did not meet is a confirmed threat at weight 1.0.</summary>
    [Fact]
    public void TestT2_ANotMetRtoIsAConfirmedThreat()
    {
        var graph = new ContinuityGraph([Process(1, rto: 240)], []);

        var threat = Assess(graph, 1, Statuses((1, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet)));

        Assert.Equal(ContinuityThreatClass.Confirmed, Assert.Single(threat.Items).Class);
        Assert.Equal(1.0m, threat.ThreatWeight);
    }

    /// <summary>T3 — a confirmed RTO threat and an unverified RPO: the node weighs 1.0, the maximum.</summary>
    [Fact]
    public void TestT3_TheNodeWeighsItsHighestItem()
    {
        var graph = new ContinuityGraph([Process(1, rto: 240, rpo: 60)], []);

        var threat = Assess(graph, 1, Statuses(
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet),
            (1, ContinuityObjective.Rpo, ObjectiveVerificationStatus.Unverified)));

        Assert.Equal(2, threat.Items.Count);
        Assert.Equal(1.0m, threat.ThreatWeight);
        Assert.Equal(ContinuityThreatClass.Confirmed, threat.Items[0].Class);
    }

    /// <summary>T4 (the adopted reading, D16) — an unverified RTO and an unverified RPO weigh 0.5, not 1.0:
    /// two unknowns do not make a fact.</summary>
    [Fact]
    public void TestT4_TwoUnverifiedItemsDoNotAddUp()
    {
        var graph = new ContinuityGraph([Process(1, rto: 240, rpo: 60)], []);

        var threat = Assess(graph, 1, Statuses(
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.Unverified),
            (1, ContinuityObjective.Rpo, ObjectiveVerificationStatus.Unverified)));

        Assert.Equal(2, threat.Items.Count);
        Assert.Equal(Half, threat.ThreatWeight);
    }

    /// <summary>T5 — the weight is the parameter: 0.25 makes unverified items 0.25 and leaves confirmed
    /// ones at 1.0; 1.0 makes them equal.</summary>
    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    public void TestT5_TheUnverifiedWeightIsTheParameter(double configured)
    {
        var weight = (decimal)configured;
        var graph = new ContinuityGraph([Process(1, rto: 240, rpo: 60)], []);

        var threat = Assess(graph, 1, Statuses(
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet),
            (1, ContinuityObjective.Rpo, ObjectiveVerificationStatus.Unverified)), weight);

        Assert.Equal(1.0m, threat.Items.Single(i => i.Class == ContinuityThreatClass.Confirmed).Weight);
        Assert.Equal(weight, threat.Items.Single(i => i.Class == ContinuityThreatClass.NotVerified).Weight);
        Assert.Equal(weight, threat.UnverifiedWeight);

        var unverifiedOnly = Assess(graph, 1, Statuses((1, ContinuityObjective.Rto, ObjectiveVerificationStatus.Unverified)), weight);
        Assert.Equal(weight, unverifiedOnly.ThreatWeight);
    }

    /// <summary>T6 — the cascade: P (RTO 240) inherits from its provider S, through S, each case with its class.</summary>
    [Theory]
    [InlineData(240, ObjectiveVerificationStatus.NotMet, ContinuityThreatReason.ProviderNotMet, 1.0)]
    [InlineData(240, ObjectiveVerificationStatus.Unverified, ContinuityThreatReason.ProviderUnverified, 0.5)]
    [InlineData(480, ObjectiveVerificationStatus.Met, ContinuityThreatReason.ProviderExceedsRequirement, 1.0)]
    [InlineData(null, ObjectiveVerificationStatus.Absent, ContinuityThreatReason.ProviderObjectiveAbsent, 0.5)]
    public void TestT6_AProcessInheritsThreatsFromItsProviders(int? serviceRto, ObjectiveVerificationStatus serviceStatus,
        ContinuityThreatReason expected, double weight)
    {
        var graph = new ContinuityGraph([Process(1, rto: 240), Service(2, rto: serviceRto)], [(1, 2)]);

        var threat = Assess(graph, 1, Statuses(
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.Met),
            (2, ContinuityObjective.Rto, serviceStatus)));

        var item = Assert.Single(threat.Items, i => i.Objective == ContinuityObjective.Rto);
        Assert.Equal(expected, item.Reason);
        Assert.Equal((decimal)weight, item.Weight);
        Assert.Equal(2, item.ViaEntityId);
        Assert.Equal("S2", item.ViaName);
    }

    /// <summary>T7 (the methodology's case) — a process with no BIA inherits nothing from a provider that
    /// is not met: absent is not 0, so it requires nothing that could be threatened.</summary>
    [Fact]
    public void TestT7_AProcessWithoutBiaInheritsNothing()
    {
        var graph = new ContinuityGraph([Process(1), Service(2, rto: 60)], [(1, 2)]);

        var threat = Assess(graph, 1, Statuses((2, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet)));

        Assert.Empty(threat.Items);
        Assert.Equal(0m, threat.ThreatWeight);
        Assert.False(threat.IsThreatened);
    }

    /// <summary>T8 — the RPO is not inherited through the MTPD: a process with only an MTPD is not
    /// threatened by a provider with no RPO, though it is for the RTO.</summary>
    [Fact]
    public void TestT8_TheRpoHasNoMtpdFallback()
    {
        var graph = new ContinuityGraph([Process(1, mtpd: 480), Service(2)], [(1, 2)]);

        var threat = Assess(graph, 1, Statuses());

        Assert.DoesNotContain(threat.Items, i => i.Objective == ContinuityObjective.Rpo);
        Assert.Contains(threat.Items, i => i is { Objective: ContinuityObjective.Rto, Reason: ContinuityThreatReason.ProviderObjectiveAbsent });
    }

    /// <summary>T9 — a cycle (and the ring of ten thousand) terminates, repeats no item, and never makes a
    /// node inherit from itself.</summary>
    [Fact]
    public void TestT9_ACycleNeitherLoopsNorRepeats()
    {
        var pair = new ContinuityGraph([Process(1, rto: 240), Service(2, rto: 120)], [(1, 2), (2, 1)]);
        var threat = Assess(pair, 1, Statuses(
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet),
            (2, ContinuityObjective.Rto, ObjectiveVerificationStatus.Unverified)));

        // Node 1 has a threatened RTO of its own, so a walk that revisited the start node would emit a
        // `via 1` item; the assertion can fail only if the self-exclusion is broken.
        Assert.Contains(threat.Items, i => i.Reason == ContinuityThreatReason.NotMet);
        Assert.DoesNotContain(threat.Items, i => i.ViaEntityId == 1);
        Assert.Equal(threat.Items.Count, threat.Items.Select(i => (i.Reason, i.Objective, i.ViaEntityId)).Distinct().Count());

        const int size = 10_000;
        var ring = new ContinuityGraph(Enumerable.Range(0, size).Select(i => Service(i, rto: 60)),
            Enumerable.Range(0, size).Select(i => (i, (i + 1) % size)));
        var ringThreat = Assess(ring, 0, Statuses((0, ContinuityObjective.Rto, ObjectiveVerificationStatus.NotMet)));

        Assert.DoesNotContain(ringThreat.Items, i => i.ViaEntityId == 0);
        Assert.Single(ringThreat.Items);
    }

    /// <summary>T10 — items come out by weight descending, then objective, reason and via id.</summary>
    [Fact]
    public void TestT10_ItemsAreOrderedDeterministically()
    {
        var graph = new ContinuityGraph([Process(1, rto: 240, rpo: 60), Service(3), Service(2)], [(1, 3), (1, 2)]);

        var threat = Assess(graph, 1, Statuses(
            (1, ContinuityObjective.Rpo, ObjectiveVerificationStatus.NotMet),
            (1, ContinuityObjective.Rto, ObjectiveVerificationStatus.Unverified)));

        var order = threat.Items.Select(i => (i.Weight, i.Objective, i.Reason, i.ViaEntityId)).ToList();
        Assert.Equal(order.OrderByDescending(o => o.Weight).ThenBy(o => o.Objective).ThenBy(o => o.Reason)
            .ThenBy(o => o.ViaEntityId ?? 0).ToList(), order);
        Assert.Equal(ContinuityThreatReason.NotMet, threat.Items[0].Reason);
        Assert.Equal([2, 3], threat.Items.Where(i => i is { Reason: ContinuityThreatReason.ProviderObjectiveAbsent, Objective: ContinuityObjective.Rto })
            .Select(i => i.ViaEntityId!.Value));
    }
}
