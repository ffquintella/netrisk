using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Tools.Continuity;
using Tools.ThirdParties;
using Xunit;

namespace Tools.Tests.ThirdParties;

/// <summary>
/// Stage 9.10 (S51 §4.8, §8 C1–C11) — concentration by supplier, cloud and identity. The edge case the methodology names
/// for this stage is C1/C2: <b>a supplier is counted once per dependent critical process, not once per link or per
/// asset</b> — otherwise the metric measures inventory, not concentration.
///
/// The organization:
/// <code>
///   processes  1 Enrolment (5)   2 Payroll (4)   3 Library (2, not critical)   4 Legacy exams (5, inactive)
///   services   10 Portal   11 Identity   12 Hosting   13 Spare (no dependents)
///   BIA        1 → 10, 1 → 11, 2 → 11, 3 → 10, 4 → 10, 10 → 12, 11 → 12   (dependent → provider)
/// </code>
/// </summary>
[TestSubject(typeof(ConcentrationCalculator))]
public class ConcentrationCalculatorTest
{
    private static ContinuityGraph Graph(int? enrolmentRto = 60, int? portalRto = 240, int? payrollRpo = null) => new(
    [
        new ContinuityNode(1, "businessProcess", "Enrolment", true, null, enrolmentRto, null, 5),
        new ContinuityNode(2, "businessProcess", "Payroll", true, null, null, payrollRpo, 4),
        new ContinuityNode(3, "businessProcess", "Library", true, null, null, null, 2),
        new ContinuityNode(4, "businessProcess", "Legacy exams", false, null, null, null, 5),
        new ContinuityNode(10, "itService", "Portal", true, null, portalRto, null),
        new ContinuityNode(11, "itService", "Identity", true, null, null, null),
        new ContinuityNode(12, "itService", "Hosting", true, null, null, null),
        new ContinuityNode(13, "itService", "Spare", true, null, null, null)
    ],
    [(1, 10), (1, 11), (2, 11), (3, 10), (4, 10), (10, 12), (11, 12)]);

    private static SupplierFacts Supplier(int id, params int[] supplied) =>
        new(id, $"Supplier {id}", ThirdPartyStatus.Active, false, false, supplied);

    private static Dictionary<int, SupplierExposure> Compute(IReadOnlyCollection<SupplierFacts> suppliers,
        params (int User, int Sub)[] subprocessing) =>
        ConcentrationCalculator.Compute(Graph(), suppliers, subprocessing);

    /// <summary>
    /// C1 (T205) — linked to the portal, the identity service and Enrolment itself: Enrolment is reached three ways and
    /// counted once; Payroll once. Two, never four (links) nor five (paths).
    /// </summary>
    [Fact]
    public void TestC1_ASupplierIsCountedOncePerDependentCriticalProcess()
    {
        var exposure = Compute([Supplier(100, 10, 11, 1)])[100];

        Assert.Equal(new[] { 1, 2 }, exposure.CriticalProcessIds);
    }

    /// <summary>
    /// C2 (T205) — three links that all lead to Enrolment carry one process; two suppliers sharing the portal each carry
    /// it once — the count is per supplier, not shared out.
    /// </summary>
    [Fact]
    public void TestC2_ManyLinksToOneProcessCountOne()
    {
        var exposures = Compute([Supplier(100, 10, 1), Supplier(101, 10), Supplier(102, 10)]);

        Assert.Equal(new[] { 1 }, exposures[100].CriticalProcessIds);
        Assert.Equal(new[] { 1 }, exposures[101].CriticalProcessIds);
        Assert.Equal(new[] { 1 }, exposures[102].CriticalProcessIds);
    }

    /// <summary>C3 — a non-critical process and an inactive critical one are not counted; the denominator is the active critical.</summary>
    [Fact]
    public void TestC3_OnlyActiveCriticalProcessesCount()
    {
        Assert.Equal(new[] { 1 }, Compute([Supplier(100, 10)])[100].CriticalProcessIds);
        Assert.Equal(2, ConcentrationCalculator.CriticalProcessCount(Graph()));
        Assert.Equal(0.5m, ConcentrationCalculator.Share(1, 2));
        Assert.Null(ConcentrationCalculator.Share(0, 0));
    }

    /// <summary>C4 — the BIA cascade is transitive: hosting carries everything above the portal and the identity service.</summary>
    [Fact]
    public void TestC4_TheCascadeIsTransitive() =>
        Assert.Equal(new[] { 1, 2 }, Compute([Supplier(100, 12)])[100].CriticalProcessIds);

    /// <summary>C5 — prospective and terminated supply nothing; exiting still does.</summary>
    [Theory]
    [InlineData(ThirdPartyStatus.Prospective, 0, false)]
    [InlineData(ThirdPartyStatus.Terminated, 0, false)]
    [InlineData(ThirdPartyStatus.Exiting, 2, true)]
    [InlineData(ThirdPartyStatus.Active, 2, true)]
    public void TestC5_OnlyALiveRelationshipSupplies(ThirdPartyStatus status, int expected, bool inUse)
    {
        var exposure = Compute([new SupplierFacts(100, "S", status, false, false, [11])])[100];

        Assert.Equal(expected, exposure.CriticalProcessIds.Count);
        Assert.Equal(inUse, exposure.InUse);
    }

    /// <summary>
    /// C6 — a cloud with no contract of its own, named as a sub-processor by a live SaaS, carries what the SaaS supplies (the
    /// fourth-party path); once the SaaS is terminated it carries nothing.
    /// </summary>
    [Fact]
    public void TestC6_ASubprocessorCarriesWhatItsUserSupplies()
    {
        var cloud = new SupplierFacts(200, "Cloud", ThirdPartyStatus.Prospective, true, false, []);

        var live = Compute([Supplier(100, 11), cloud], (100, 200))[200];
        Assert.Equal(new[] { 1, 2 }, live.CriticalProcessIds);
        Assert.True(live.ReachedThroughSubprocessing);
        Assert.True(live.InUse);

        var ended = Compute([new SupplierFacts(100, "SaaS", ThirdPartyStatus.Terminated, false, false, [11]), cloud], (100, 200))[200];
        Assert.Empty(ended.CriticalProcessIds);
        Assert.False(ended.InUse);
    }

    /// <summary>C7 — what a cloud carries directly and through a sub-processing user is one set: no process counted twice.</summary>
    [Fact]
    public void TestC7_DirectAndFourthPartyPathsAreOneSet()
    {
        var cloud = new SupplierFacts(200, "Cloud", ThirdPartyStatus.Active, true, false, [10]);

        var exposure = Compute([Supplier(100, 10, 11), cloud], (100, 200))[200];

        Assert.Equal(new[] { 1, 2 }, exposure.CriticalProcessIds);
        Assert.True(exposure.ReachedThroughSubprocessing); // Payroll only through the SaaS
    }

    /// <summary>C8 — sub-processing is transitive and a cycle ends: A uses B, B uses C, C uses A.</summary>
    [Fact]
    public void TestC8_SubprocessingIsTransitiveAndCycleSafe()
    {
        var a = new SupplierFacts(1000, "A", ThirdPartyStatus.Active, false, false, [10]);
        var b = new SupplierFacts(1001, "B", ThirdPartyStatus.Active, false, false, []);
        var c = new SupplierFacts(1002, "C", ThirdPartyStatus.Active, true, false, []);

        var exposures = Compute([a, b, c], (1000, 1001), (1001, 1002), (1002, 1000), (1002, 1002));

        Assert.Equal(new[] { 1 }, exposures[1002].CriticalProcessIds);
        Assert.Equal(new[] { 1 }, exposures[1001].CriticalProcessIds);
        Assert.Equal(new[] { 1 }, exposures[1000].CriticalProcessIds);
    }

    /// <summary>C9 — a supplied service with no declared dependent is reported, so a zero can be told from "not mapped".</summary>
    [Fact]
    public void TestC9_ALinkWithoutDeclaredDependentsIsReported()
    {
        var exposure = Compute([Supplier(100, 13, 10)])[100];

        Assert.Equal(1, exposure.LinksWithoutDeclaredDependents);
        Assert.Equal(new[] { 1 }, exposure.CriticalProcessIds);

        // An entity outside the graph (a data record, an unknown id) contributes nothing and breaks nothing.
        Assert.Empty(Compute([Supplier(100, 999)])[100].CriticalProcessIds);
    }

    /// <summary>
    /// C10 — the contracted-RTO requirement is the tightest of the supplied nodes' own objectives and of their dependents'
    /// (Enrolment's 60 beats the portal's 240), bound to who sets it; absent stays absent; a prospective supplier already has
    /// one, a terminated one has none.
    /// </summary>
    [Fact]
    public void TestC10_TheRequirementIsTheTightestObjective()
    {
        var requirement = ConcentrationCalculator.Compute(Graph(), [Supplier(100, 10)], [])[100].Requirement;
        Assert.Equal((60, 1, "Enrolment"), (requirement.RequiredRtoMinutes, requirement.RtoBindingEntityId, requirement.RtoBindingName));
        Assert.Null(requirement.RequiredRpoMinutes);

        var portalOnly = ConcentrationCalculator.Compute(Graph(enrolmentRto: null), [Supplier(100, 10)], [])[100].Requirement;
        Assert.Equal((240, 10), (portalOnly.RequiredRtoMinutes, portalOnly.RtoBindingEntityId));

        var rpo = ConcentrationCalculator.Compute(Graph(payrollRpo: 15), [Supplier(100, 12)], [])[100].Requirement;
        Assert.Equal((15, 2), (rpo.RequiredRpoMinutes, rpo.RpoBindingEntityId));

        Assert.Null(ConcentrationCalculator.Compute(Graph(enrolmentRto: null, portalRto: null), [Supplier(100, 10)], [])[100]
            .Requirement.RequiredRtoMinutes);

        Assert.Equal(60, ConcentrationCalculator.Compute(Graph(),
            [new SupplierFacts(100, "S", ThirdPartyStatus.Prospective, false, false, [10])], [])[100].Requirement.RequiredRtoMinutes);
        Assert.Null(ConcentrationCalculator.Compute(Graph(),
            [new SupplierFacts(100, "S", ThirdPartyStatus.Terminated, false, false, [10])], [])[100].Requirement.RequiredRtoMinutes);
    }

    /// <summary>C11 — a ring of a thousand sub-processors terminates and still reaches the start from anywhere in it.</summary>
    [Fact]
    public void TestC11_ALargeSubprocessingRingTerminates()
    {
        const int n = 1_000;
        var suppliers = Enumerable.Range(0, n)
            .Select(i => new SupplierFacts(5000 + i, $"R{i}", ThirdPartyStatus.Active, false, false, i == 0 ? [10] : []))
            .ToList();
        var ring = Enumerable.Range(0, n).Select(i => (5000 + i, 5000 + (i + 1) % n)).ToArray();

        var exposure = ConcentrationCalculator.Compute(Graph(), suppliers.Take(3).ToList(), ring.Take(2).ToArray())[5002];
        Assert.Equal(new[] { 1 }, exposure.CriticalProcessIds);

        var whole = ConcentrationCalculator.Compute(Graph(), suppliers, ring);
        Assert.Equal(new[] { 1 }, whole[5000 + n / 2].CriticalProcessIds);
        Assert.All(whole.Values, e => Assert.True(e.InUse));
    }
}
