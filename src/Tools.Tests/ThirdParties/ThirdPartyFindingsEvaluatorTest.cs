using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.ThirdParties;
using Tools.ThirdParties;
using Xunit;

namespace Tools.Tests.ThirdParties;

/// <summary>
/// Stage 9.10 (S51 §4.7, §8 F1–F10) — the gaps in what the register knows about a supplier. Absent is a finding of its
/// own, never read as compliant: an undeclared right to audit is not a granted one, an uncontracted RTO is not 0.
/// </summary>
[TestSubject(typeof(ThirdPartyFindingsEvaluator))]
public class ThirdPartyFindingsEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A supplier with everything declared and nothing wrong.</summary>
    private static ThirdPartyFacts Documented() => new(
        ThirdPartyStatus.Active, true, Now.AddDays(-10), 1, true, true, "Migrate to the second provider in 90 days.",
        Now.AddDays(-30), "Full export, CSV and JSON, within 30 days.", 60, 15, 30, Now.AddYears(1), 99.9m,
        HecvatState.Conforming, 2,
        new ThirdPartyContinuityRequirementDto { RequiredRtoMinutes = 60, RequiredRpoMinutes = 15 });

    private static List<ThirdPartyFindingCode> Codes(ThirdPartyFacts facts) =>
        ThirdPartyFindingsEvaluator.Evaluate(facts, Now).Select(f => f.Code).ToList();

    /// <summary>F1 — fully documented: no finding. A terminated relationship has none whatever is missing.</summary>
    [Fact]
    public void TestF1_FullyDocumentedHasNoFinding()
    {
        Assert.Empty(Codes(Documented()));

        var bare = new ThirdPartyFacts(ThirdPartyStatus.Terminated, null, null, 0, true, null, null, null, null, null, null,
            null, Now.AddYears(-1), null, HecvatState.NotAssessed, 0,
            new ThirdPartyContinuityRequirementDto { RequiredRtoMinutes = 5 });
        Assert.Empty(Codes(bare));
    }

    /// <summary>F2 — each HECVAT state that is not conforming is its finding.</summary>
    [Theory]
    [InlineData(HecvatState.NotAssessed, ThirdPartyFindingCode.HecvatMissing)]
    [InlineData(HecvatState.Voided, ThirdPartyFindingCode.HecvatMissing)]
    [InlineData(HecvatState.Incomplete, ThirdPartyFindingCode.HecvatIncomplete)]
    [InlineData(HecvatState.NonConforming, ThirdPartyFindingCode.HecvatNonConforming)]
    [InlineData(HecvatState.Expired, ThirdPartyFindingCode.HecvatExpired)]
    [InlineData(HecvatState.NotScorable, ThirdPartyFindingCode.HecvatNotScorable)]
    public void TestF2_AHecvatThatIsNotConformingIsAFinding(HecvatState state, ThirdPartyFindingCode code) =>
        Assert.Equal(new[] { code }, Codes(Documented() with { Hecvat = state }));

    /// <summary>F3 — each undeclared or missing declaration is its own finding.</summary>
    [Fact]
    public void TestF3_EachMissingDeclarationIsItsFinding()
    {
        var cases = new (ThirdPartyFacts Facts, ThirdPartyFindingCode Code)[]
        {
            (Documented() with { ProcessesPersonalData = null, ProcessesADataRecord = false }, ThirdPartyFindingCode.PersonalDataUndeclared),
            (Documented() with { SubprocessorsDeclaredAt = null }, ThirdPartyFindingCode.SubprocessorsUndeclared),
            (Documented() with { DataLocationCount = 0 }, ThirdPartyFindingCode.DataLocationMissing),
            (Documented() with { RightToAudit = null }, ThirdPartyFindingCode.RightToAuditUndeclared),
            (Documented() with { RightToAudit = false }, ThirdPartyFindingCode.RightToAuditMissing),
            (Documented() with { ExitPlan = "  " }, ThirdPartyFindingCode.ExitPlanMissing),
            (Documented() with { ExitPlanTestedAt = null }, ThirdPartyFindingCode.ExitPlanUntested),
            (Documented() with { DataPortability = null }, ThirdPartyFindingCode.DataPortabilityMissing),
            (Documented() with { SlaAvailabilityPercent = null }, ThirdPartyFindingCode.SlaUndeclared)
        };

        foreach (var (facts, code) in cases)
            Assert.Equal(new[] { code }, Codes(facts));
    }

    /// <summary>
    /// F4 — sub-processors and data location are asked of whoever processes personal data or a data record; one that
    /// declares it processes neither is not asked.
    /// </summary>
    [Fact]
    public void TestF4_TheLgpdDeclarationsFollowWhatItProcesses()
    {
        var none = Documented() with
        {
            ProcessesPersonalData = false, ProcessesADataRecord = false, SubprocessorsDeclaredAt = null, DataLocationCount = 0
        };
        Assert.Empty(Codes(none));

        Assert.Equal(new[] { ThirdPartyFindingCode.DataLocationMissing },
            Codes(none with { ProcessesADataRecord = true }));
        Assert.Equal(new[] { ThirdPartyFindingCode.SubprocessorsUndeclared, ThirdPartyFindingCode.DataLocationMissing },
            Codes(none with { ProcessesPersonalData = true }));
    }

    /// <summary>F5 — the exit plan must have been exercised only by a supplier of a critical process.</summary>
    [Fact]
    public void TestF5_AnUntestedExitPlanMattersForCriticalSuppliersOnly() =>
        Assert.Empty(Codes(Documented() with { ExitPlanTestedAt = null, DependentCriticalProcessCount = 0 }));

    /// <summary>F6 — the contracted RTO/RPO against the requirement: absent, looser, equal, tighter; no requirement asks nothing.</summary>
    [Fact]
    public void TestF6_TheContractedObjectivesMeetTheRequirement()
    {
        Assert.Equal(new[] { ThirdPartyFindingCode.RtoNotContracted }, Codes(Documented() with { ContractedRtoMinutes = null }));
        Assert.Equal(new[] { ThirdPartyFindingCode.RtoExceedsRequirement }, Codes(Documented() with { ContractedRtoMinutes = 61 }));
        Assert.Empty(Codes(Documented() with { ContractedRtoMinutes = 30 }));
        Assert.Equal(new[] { ThirdPartyFindingCode.RpoNotContracted }, Codes(Documented() with { ContractedRpoMinutes = null }));
        Assert.Equal(new[] { ThirdPartyFindingCode.RpoExceedsRequirement }, Codes(Documented() with { ContractedRpoMinutes = 16 }));

        var noRequirement = Documented() with
        {
            ContractedRtoMinutes = null, ContractedRpoMinutes = null, Requirement = new ThirdPartyContinuityRequirementDto()
        };
        Assert.Empty(Codes(noRequirement));

        var message = ThirdPartyFindingsEvaluator.Evaluate(Documented() with
        {
            ContractedRtoMinutes = 480,
            Requirement = new ThirdPartyContinuityRequirementDto { RequiredRtoMinutes = 60, RtoBindingName = "Enrolment", RequiredRpoMinutes = 15 }
        }, Now).Single().Message;
        Assert.Contains("480", message);
        Assert.Contains("Enrolment", message);
    }

    /// <summary>F7 — FGV NRM §5.2: at most 30 days to fix a medium-or-higher vulnerability; 31 is too slow, none is undeclared.</summary>
    [Theory]
    [InlineData(null, ThirdPartyFindingCode.VulnerabilityFixUndeclared)]
    [InlineData(31, ThirdPartyFindingCode.VulnerabilityFixTooSlow)]
    [InlineData(365, ThirdPartyFindingCode.VulnerabilityFixTooSlow)]
    public void TestF7_TheVulnerabilityFixDeadline(int? days, ThirdPartyFindingCode code) =>
        Assert.Equal(new[] { code }, Codes(Documented() with { VulnerabilityFixDays = days }));

    /// <summary>F7b — thirty days and fewer are within the norm.</summary>
    [Theory]
    [InlineData(30)]
    [InlineData(0)]
    public void TestF7b_WithinTheNorm(int days) => Assert.Empty(Codes(Documented() with { VulnerabilityFixDays = days }));

    /// <summary>F8 — an ended contract is a finding while the supplier is active or exiting, not while prospective.</summary>
    [Fact]
    public void TestF8_AnEndedContractWhileStillSupplying()
    {
        var ended = Documented() with { ContractEnd = Now.AddDays(-1) };

        Assert.Equal(new[] { ThirdPartyFindingCode.ContractExpired }, Codes(ended));
        Assert.Equal(new[] { ThirdPartyFindingCode.ContractExpired }, Codes(ended with { Status = ThirdPartyStatus.Exiting }));
        Assert.Empty(Codes(ended with { Status = ThirdPartyStatus.Prospective }));
        Assert.Empty(Codes(Documented() with { ContractEnd = null }));
    }

    /// <summary>F9 — a bare active supplier collects every finding, in a stable order.</summary>
    [Fact]
    public void TestF9_ABareSupplierCollectsEveryFinding()
    {
        var bare = new ThirdPartyFacts(ThirdPartyStatus.Active, null, null, 0, true, null, null, null, null, null, null, null,
            null, null, HecvatState.NotAssessed, 3,
            new ThirdPartyContinuityRequirementDto { RequiredRtoMinutes = 60, RequiredRpoMinutes = 15 });

        Assert.Equal(new[]
        {
            ThirdPartyFindingCode.HecvatMissing, ThirdPartyFindingCode.PersonalDataUndeclared,
            ThirdPartyFindingCode.DataLocationMissing, ThirdPartyFindingCode.RightToAuditUndeclared,
            ThirdPartyFindingCode.ExitPlanMissing, ThirdPartyFindingCode.DataPortabilityMissing,
            ThirdPartyFindingCode.RtoNotContracted, ThirdPartyFindingCode.RpoNotContracted,
            ThirdPartyFindingCode.VulnerabilityFixUndeclared, ThirdPartyFindingCode.SlaUndeclared
        }, Codes(bare));
    }
}
