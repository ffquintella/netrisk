using System;
using System.Collections.Generic;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Continuity;
using Tools.Continuity;
using Xunit;

namespace Tools.Tests.Continuity;

/// <summary>
/// Stage 9.3 (S43 §8, V1–V14) — whether a declared RTO or RPO is verified by a restoration test.
///
/// The case the methodology names is V1: a declared RTO with no test is <b>unverified</b>, not met —
/// the difference between the metric measuring something and measuring the intention.
/// </summary>
[TestSubject(typeof(RestorationVerification))]
public class RestorationVerificationTest
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static RestorationTestFacts Test(int id, int daysAgo, RestorationTestOutcome outcome = RestorationTestOutcome.Succeeded,
        int? rto = null, int? rpo = null, bool voided = false, double extraSeconds = 0) =>
        new(id, Now.AddDays(-daysAgo).AddSeconds(-extraSeconds), outcome, rto, rpo, voided);

    private static ObjectiveVerificationDto Rto(int? declared, IEnumerable<RestorationTestFacts> tests, int validity = 365) =>
        RestorationVerification.Evaluate(ContinuityObjective.Rto, declared, tests, Now, validity);

    private static ObjectiveVerificationDto Rpo(int? declared, IEnumerable<RestorationTestFacts> tests, int validity = 365) =>
        RestorationVerification.Evaluate(ContinuityObjective.Rpo, declared, tests, Now, validity);

    /// <summary>V1 (the methodology's case, T160) — declared RTO, no test: unverified, never met.</summary>
    [Fact]
    public void TestV1_ADeclaredRtoWithoutATestIsUnverifiedNotMet()
    {
        var result = Rto(240, []);

        Assert.Equal(ObjectiveVerificationStatus.Unverified, result.Status);
        Assert.Equal(VerificationReason.NoTest, result.Reason);
        Assert.NotEqual(ObjectiveVerificationStatus.Met, result.Status);
        Assert.Null(result.TestId);
    }

    /// <summary>V2 — no declared RTO is absent, whatever successful tests exist: never 0, never infinite.</summary>
    [Fact]
    public void TestV2_AnUndeclaredObjectiveIsAbsentEvenWithTests()
    {
        var result = Rto(null, [Test(1, 10, rto: 30)]);

        Assert.Equal(ObjectiveVerificationStatus.Absent, result.Status);
        Assert.Null(result.Reason);
        Assert.Null(result.DeclaredMinutes);
    }

    /// <summary>V3 — a measure under the declared value is met, and so is one exactly equal to it.</summary>
    [Theory]
    [InlineData(180)]
    [InlineData(240)]
    public void TestV3_AMeasureWithinTheDeclaredValueIsMet(int achieved)
    {
        var result = Rto(240, [Test(1, 10, rto: achieved)]);

        Assert.Equal(ObjectiveVerificationStatus.Met, result.Status);
        Assert.Equal(achieved, result.AchievedMinutes);
        Assert.Equal(1, result.TestId);
    }

    /// <summary>V4 — a measure above the declared value is not met, because it was exceeded.</summary>
    [Fact]
    public void TestV4_AMeasureAboveTheDeclaredValueIsNotMet()
    {
        var result = Rto(240, [Test(1, 10, rto: 241)]);

        Assert.Equal(ObjectiveVerificationStatus.NotMet, result.Status);
        Assert.Equal(VerificationReason.Exceeded, result.Reason);
    }

    /// <summary>V5 — a failed test with no measure is not met for the RTO and the RPO alike.</summary>
    [Fact]
    public void TestV5_AFailedTestIsNotMetForBothObjectives()
    {
        RestorationTestFacts[] tests = [Test(1, 5, RestorationTestOutcome.Failed)];

        Assert.Equal((ObjectiveVerificationStatus.NotMet, VerificationReason.RestorationFailed),
            (Rto(240, tests).Status, Rto(240, tests).Reason));
        Assert.Equal((ObjectiveVerificationStatus.NotMet, VerificationReason.RestorationFailed),
            (Rpo(60, tests).Status, Rpo(60, tests).Reason));
    }

    /// <summary>V6 — the most recent test decides, not the best: an old pass does not hide a recent
    /// failure, and a recent pass does clear an old failure.</summary>
    [Fact]
    public void TestV6_TheMostRecentTestDecides()
    {
        Assert.Equal(ObjectiveVerificationStatus.NotMet,
            Rto(240, [Test(1, 100, rto: 60), Test(2, 10, RestorationTestOutcome.Failed)]).Status);

        Assert.Equal(ObjectiveVerificationStatus.Met,
            Rto(240, [Test(1, 100, RestorationTestOutcome.Failed), Test(2, 10, rto: 60)]).Status);
    }

    /// <summary>V7 — 365 days exactly is still valid; one second more is stale.</summary>
    [Fact]
    public void TestV7_TheValidityBoundIsInclusive()
    {
        Assert.Equal(ObjectiveVerificationStatus.Met, Rto(240, [Test(1, 365, rto: 60)]).Status);

        var stale = Rto(240, [Test(1, 365, rto: 60, extraSeconds: 1)]);
        Assert.Equal(ObjectiveVerificationStatus.Unverified, stale.Status);
        Assert.Equal(VerificationReason.Stale, stale.Reason);
        Assert.Equal(1, stale.TestId);
    }

    /// <summary>V8 — voided tests are ignored: only voided ones is no test; a voided recent one gives way
    /// to the earlier valid one.</summary>
    [Fact]
    public void TestV8_VoidedTestsAreIgnored()
    {
        var onlyVoided = Rto(240, [Test(1, 10, rto: 60, voided: true)]);
        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.NoTest),
            (onlyVoided.Status, onlyVoided.Reason));

        var earlier = Rto(240, [Test(1, 30, rto: 60), Test(2, 5, RestorationTestOutcome.Failed, voided: true)]);
        Assert.Equal(ObjectiveVerificationStatus.Met, earlier.Status);
        Assert.Equal(1, earlier.TestId);
    }

    /// <summary>V9 — a test that measured only the RPO leaves the RTO "not measured" and decides the RPO.</summary>
    [Fact]
    public void TestV9_ATestOfOneObjectiveDoesNotVerifyTheOther()
    {
        RestorationTestFacts[] tests = [Test(1, 10, rpo: 15)];

        var rto = Rto(240, tests);
        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.NotMeasured), (rto.Status, rto.Reason));
        Assert.Equal(ObjectiveVerificationStatus.Met, Rpo(30, tests).Status);
    }

    /// <summary>V10 — an older test that measured the RTO still decides it when a newer one measured only the RPO.</summary>
    [Fact]
    public void TestV10_TheLatestTestThatMeasuredTheObjectiveDecides()
    {
        var result = Rto(240, [Test(1, 60, rto: 300), Test(2, 5, rpo: 10)]);

        Assert.Equal(ObjectiveVerificationStatus.NotMet, result.Status);
        Assert.Equal(1, result.TestId);
    }

    /// <summary>V11 — two tests at the same instant: the higher id decides.</summary>
    [Fact]
    public void TestV11_ATieOnTheDateGoesToTheHigherId()
    {
        var result = Rto(240, [Test(7, 10, rto: 60), Test(3, 10, rto: 500)]);

        Assert.Equal(7, result.TestId);
        Assert.Equal(ObjectiveVerificationStatus.Met, result.Status);
    }

    /// <summary>V12 — an RPO of 0 achieved as 0 is met: 0 is a value, not absent.</summary>
    [Fact]
    public void TestV12_ZeroIsAValue()
    {
        Assert.Equal(ObjectiveVerificationStatus.Met, Rpo(0, [Test(1, 10, rpo: 0)]).Status);
        Assert.Equal(ObjectiveVerificationStatus.NotMet, Rpo(0, [Test(1, 10, rpo: 1)]).Status);
    }

    /// <summary>V13 — the validity is the parameter, not a constant: with 30 days, 30 exactly is valid
    /// and 30 days and a second is stale.</summary>
    [Fact]
    public void TestV13_TheValidityComesFromTheParameter()
    {
        Assert.Equal(ObjectiveVerificationStatus.Met, Rto(240, [Test(1, 30, rto: 60)], validity: 30).Status);

        var stale = Rto(240, [Test(1, 30, rto: 60, extraSeconds: 1)], validity: 30);
        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.Stale), (stale.Status, stale.Reason));
        Assert.Equal(30, stale.ValidityDays);
    }

    /// <summary>V14 — a 400-day-old test is met under 1 095 days and stale under 365; ValidUntil is the
    /// test date plus the validity.</summary>
    [Fact]
    public void TestV14_TheSameTestUnderTwoValidities()
    {
        RestorationTestFacts[] tests = [Test(1, 400, rto: 60)];

        var long_ = Rto(240, tests, validity: 1_095);
        Assert.Equal(ObjectiveVerificationStatus.Met, long_.Status);
        Assert.Equal(tests[0].TestedAt.AddDays(1_095), long_.ValidUntil);

        var short_ = Rto(240, tests, validity: 365);
        Assert.Equal(ObjectiveVerificationStatus.Unverified, short_.Status);
        Assert.Equal(tests[0].TestedAt.AddDays(365), short_.ValidUntil);
    }
}
