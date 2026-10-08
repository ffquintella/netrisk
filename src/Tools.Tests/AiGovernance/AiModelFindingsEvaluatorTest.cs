using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.AiGovernance;
using Tools.AiGovernance;
using Xunit;

namespace Tools.Tests.AiGovernance;

/// <summary>
/// Stage 9.12 (S53 §4.7, §8 FI1–FI7) — the gaps in a model's governance. Absent is a finding, never compliant; a model in
/// use with no recorded evaluation is <see cref="AiModelFindingCode.NotEvaluated"/> (T215); a proposed model waits for its
/// pilot for the use findings; a retired one has none.
/// </summary>
[TestSubject(typeof(AiModelFindingsEvaluator))]
public class AiModelFindingsEvaluatorTest
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static AiModelEvaluationDto Evaluated() => AiModelEvaluator.Evaluate("1", null, AiModelRiskTier.Minimal,
        AiHumanOversight.NoReview, 90, [new AiReadingFacts(1, AiModelMetric.Drift, 0.1m, "1", Now.AddDays(-1), false)], Now);

    private static AiModelEvaluationDto NotEvaluated(AiModelRiskTier? tier = AiModelRiskTier.Minimal) =>
        AiModelEvaluator.Evaluate("1", null, tier, AiHumanOversight.NoReview, 90, [], Now);

    /// <summary>A complete record in production with a current evaluation.</summary>
    private static AiModelFacts Complete(AiModelStatus status = AiModelStatus.Production) => new(status, AiModelSource.InHouse,
        null, 2, AiModelRiskTier.Minimal, AiHumanOversight.NoReview, Now.AddDays(-3), 0, 1, Evaluated());

    private static List<AiModelFindingCode> Codes(AiModelFacts facts) =>
        AiModelFindingsEvaluator.Evaluate(facts).Select(f => f.Code).ToList();

    /// <summary>FI1 — a complete record in use, evaluated, has no finding.</summary>
    [Fact]
    public void TestFI1_ACompleteRecordHasNoFinding() => Assert.Empty(Codes(Complete()));

    /// <summary>FI2 (T215) — in use with no recorded evaluation: NotEvaluated, naming every required metric.</summary>
    [Fact]
    public void TestFI2_InUseWithNoEvaluationIsAFinding()
    {
        var findings = AiModelFindingsEvaluator.Evaluate(Complete() with { Evaluation = NotEvaluated(AiModelRiskTier.Limited) });

        var finding = Assert.Single(findings);
        Assert.Equal(AiModelFindingCode.NotEvaluated, finding.Code);
        Assert.Contains("accuracy, drift", finding.Message);
        Assert.Contains("not evaluated", finding.Message);
    }

    /// <summary>FI3 — absent is a finding: owner, tier, oversight, data; each named once, in order.</summary>
    [Fact]
    public void TestFI3_AbsentIsAFinding()
    {
        var bare = Complete() with { OwnerId = null, RiskTier = null, HumanOversight = null, DataDeclaredAt = null };

        Assert.Equal(
            [AiModelFindingCode.OwnerMissing, AiModelFindingCode.RiskTierUndeclared, AiModelFindingCode.HumanOversightUndeclared,
             AiModelFindingCode.DataUndeclared],
            Codes(bare));
    }

    /// <summary>FI4 — no review of a high-tier or undeclared-tier model's outputs is a finding; of a limited one, not.</summary>
    [Theory]
    [InlineData(AiModelRiskTier.High, true)]
    [InlineData(null, true)]
    [InlineData(AiModelRiskTier.Limited, false)]
    [InlineData(AiModelRiskTier.Minimal, false)]
    public void TestFI4_NoReviewOfAHighRiskModelIsAFinding(AiModelRiskTier? tier, bool finding)
    {
        var facts = Complete() with { RiskTier = tier, Evaluation = Evaluated() };
        Assert.Equal(finding, Codes(facts).Contains(AiModelFindingCode.HumanOversightAbsent));
        Assert.DoesNotContain(AiModelFindingCode.HumanOversightAbsent,
            Codes(facts with { HumanOversight = AiHumanOversight.Sampled }));
    }

    /// <summary>FI5 — vendor, data catalogue and the register.</summary>
    [Fact]
    public void TestFI5_VendorDataAndRegister()
    {
        Assert.Equal([AiModelFindingCode.VendorUnregistered], Codes(Complete() with { Source = AiModelSource.Vendor }));
        Assert.Empty(Codes(Complete() with { Source = AiModelSource.Vendor, ThirdPartyId = 4 }));
        Assert.Empty(Codes(Complete() with { Source = AiModelSource.OpenSource }));

        var uncatalogued = AiModelFindingsEvaluator.Evaluate(Complete() with { UncataloguedDataCount = 2 });
        Assert.Equal(AiModelFindingCode.DataNotCatalogued, Assert.Single(uncatalogued).Code);
        Assert.Contains("2 data record(s)", uncatalogued[0].Message);

        Assert.Equal([AiModelFindingCode.NoRiskRegistered], Codes(Complete() with { LinkedRiskCount = 0 }));
    }

    /// <summary>FI6 — a proposed model is not in use: no register or evaluation finding yet; the record findings still apply.</summary>
    [Fact]
    public void TestFI6_AProposedModelWaitsForItsPilot()
    {
        var proposed = Complete(AiModelStatus.Proposed) with { LinkedRiskCount = 0, Evaluation = NotEvaluated(), OwnerId = null };

        Assert.Equal([AiModelFindingCode.OwnerMissing], Codes(proposed));
        Assert.False(AiModelFindingsEvaluator.IsInUse(AiModelStatus.Proposed));
        Assert.True(AiModelFindingsEvaluator.IsInUse(AiModelStatus.Pilot));
        Assert.True(AiModelFindingsEvaluator.IsInUse(AiModelStatus.Production));
        Assert.False(AiModelFindingsEvaluator.IsInUse(AiModelStatus.Retired));
    }

    /// <summary>FI7 — a retired model has no finding; incomplete and stale evaluations name what is missing or old.</summary>
    [Fact]
    public void TestFI7_RetiredIncompleteAndStale()
    {
        Assert.Empty(Codes(Complete(AiModelStatus.Retired) with
            { OwnerId = null, RiskTier = null, LinkedRiskCount = 0, Evaluation = NotEvaluated() }));

        var incomplete = AiModelEvaluator.Evaluate("1", null, AiModelRiskTier.Limited, AiHumanOversight.NoReview, 90,
            [new AiReadingFacts(1, AiModelMetric.Drift, 0.1m, "1", Now.AddDays(-1), false)], Now);
        var finding = Assert.Single(AiModelFindingsEvaluator.Evaluate(Complete() with { RiskTier = AiModelRiskTier.Limited, Evaluation = incomplete }));
        Assert.Equal(AiModelFindingCode.EvaluationIncomplete, finding.Code);
        Assert.Contains("accuracy", finding.Message);

        var stale = AiModelEvaluator.Evaluate("1", null, AiModelRiskTier.Minimal, AiHumanOversight.NoReview, 90,
            [new AiReadingFacts(1, AiModelMetric.Drift, 0.1m, "1", Now.AddDays(-100), false)], Now);
        Assert.Equal([AiModelFindingCode.EvaluationStale], Codes(Complete() with { Evaluation = stale }));
    }
}
