using System;
using System.Collections.Generic;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex.Signals;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// Treatment credit (S39 §6.1, T245): the review is the expiry date of a residual; only qualified reviews restore it;
/// acceptances and campaign decisions never do (I10), and an acceptance's end only brings the due date forward.
/// </summary>
[TestSubject(typeof(ReviewCredit))]
public class ReviewCreditTest
{
    private static readonly RiskIndexParameters P = new();
    private static readonly DateTime Now = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private const int Owner = 1, Submitter = 2, Manager = 3, Reviewer = 99;

    private static DateTime D(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static ScenarioFacts Risk(double inherent, double? residual, DateTime submitted, params ReviewFact[] reviews) => new()
    {
        RiskId = 1, Inherent = inherent, Residual = residual, SubmissionDate = submitted, Owner = Owner,
        SubmittedBy = Submitter, Manager = Manager, Reviews = reviews
    };

    private static ReviewFact Review(int id, DateTime at, int reviewer = Reviewer) =>
        new() { Id = id, ReviewerId = reviewer, SubmissionDate = at };

    private static AcceptanceFact Acceptance(int id, DateTime created, DateTime expires,
        RiskAcceptanceStatus status = RiskAcceptanceStatus.Active, int? renewedFrom = null) => new()
    {
        Id = id, CreatedAt = created, ExpiresAt = expires, Status = status, RenewedFromId = renewedFrom,
        CreatedById = 50, AuthorizingManagerId = 50
    };

    [Fact]
    public void TestR1TheAcceptanceEndBringsTheDueDateForward()
    {
        var facts = Risk(8.8, 8.4, D(2026, 1, 10), Review(1, D(2026, 8, 21))) with
        {
            Acceptances = [Acceptance(1, D(2026, 3, 20), D(2026, 9, 20), RiskAcceptanceStatus.Expired)]
        };

        var credit = ReviewCredit.Compute(facts, P, Now);

        Assert.Equal(D(2026, 8, 21), credit.ReferenceDate);
        Assert.Equal(90, credit.CadenceDays);
        Assert.Equal(D(2026, 9, 20), credit.Due);
        Assert.True(credit.DueByAcceptance);
        Assert.Equal(15, credit.DelayDays, 9);
        Assert.Equal(1 - 15.0 / 90, credit.Credit, 9);
        Assert.Equal(8.4667, credit.EffectiveResidual, 4);
        Assert.Equal(0.84, credit.SigmaRef, 9);
    }

    [Fact]
    public void TestTheMethodologyExampleAndRenewingDoesNotLowerTheValue()
    {
        // S39 §6.1: d_ref 2026-09-01, T 90, inherent 8, residual 4, accepted until 2026-10-01 → r_eff ≈ 4.18 on 2026-10-05.
        var original = Acceptance(1, D(2026, 7, 1), D(2026, 10, 1), RiskAcceptanceStatus.Expired);
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, D(2026, 9, 1))) with { Acceptances = [original] };
        var before = ReviewCredit.Compute(facts, P, Now);
        Assert.Equal(4.178, before.EffectiveResidual, 3);

        // Renewing until 2026-12-01 writes a review in the same save: neither the review nor the new end moves the due date.
        var renewed = facts with
        {
            Acceptances = [original with { Status = RiskAcceptanceStatus.Renewed },
                Acceptance(2, D(2026, 10, 3), D(2026, 12, 1), renewedFrom: 1)],
            Reviews = [Review(1, D(2026, 9, 1)), Review(2, D(2026, 10, 3), 50) with { SavedWithAcceptance = true }]
        };
        var after = ReviewCredit.Compute(renewed, P, Now);

        Assert.Equal(D(2026, 10, 1), after.Due);
        Assert.Equal(before.EffectiveResidual, after.EffectiveResidual, 12);
        Assert.Equal(ReviewQualification.DerivedFromAcceptance, after.Reviews[1].Qualification);
    }

    [Fact]
    public void TestAcceptingAScenarioWithAnOverdueReviewDoesNotChangeItsRegValue()
    {
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, D(2026, 5, 8)));
        var before = ReviewCredit.Compute(facts, P, Now);
        Assert.Equal(6.667, before.EffectiveResidual, 3);

        // The acceptance writes its review without an audit row here: the window and the user recognise it.
        var accepted = facts with
        {
            Acceptances = [Acceptance(1, Now.AddDays(-1), Now.AddDays(90))],
            Reviews = [Review(1, D(2026, 5, 8)), Review(2, Now.AddDays(-1).AddSeconds(2), 50)]
        };
        var after = ReviewCredit.Compute(accepted, P, Now);

        Assert.Equal(before.EffectiveResidual, after.EffectiveResidual, 12);
        Assert.Equal(ReviewQualification.DerivedFromAcceptance, after.Reviews[1].Qualification);
    }

    [Theory]
    [InlineData(Owner)]
    [InlineData(Submitter)]
    [InlineData(Manager)]
    public void TestASelfReviewDoesNotRestoreCredit(int reviewer)
    {
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, D(2026, 5, 8)), Review(2, D(2026, 8, 30), reviewer));

        var credit = ReviewCredit.Compute(facts, P, Now);

        Assert.Equal(D(2026, 5, 8), credit.ReferenceDate);
        Assert.Equal(ReviewQualification.SelfReview, credit.Reviews[1].Qualification);
        Assert.Equal(6.667, credit.EffectiveResidual, 3);
    }

    [Fact]
    public void TestACampaignDecisionReviewDoesNotRestoreCredit()
    {
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, D(2026, 5, 8)),
            Review(2, D(2026, 9, 30)) with { SavedWithCampaignDecision = true });

        Assert.Equal(D(2026, 5, 8), ReviewCredit.Compute(facts, P, Now).ReferenceDate);
    }

    [Fact]
    public void TestAFutureDatedReviewIsBoundedByItsAuditedCreationAndByTheSnapshot()
    {
        var audited = Risk(8, 4, D(2026, 1, 1), Review(1, D(2027, 1, 1)) with { AuditedAt = D(2026, 6, 1) });
        var credit = ReviewCredit.Compute(audited, P, Now);
        Assert.Equal(D(2026, 6, 1), credit.ReferenceDate);
        Assert.Equal(36, credit.DelayDays, 9);

        var unaudited = Risk(8, 4, D(2026, 1, 1), Review(1, D(2027, 1, 1)));
        Assert.Equal(Now, ReviewCredit.Compute(unaudited, P, Now).ReferenceDate);
    }

    [Fact]
    public void TestWithoutAResidualTheTreatmentIsNotAssessedAndTheInherentCounts()
    {
        var credit = ReviewCredit.Compute(Risk(6.0, null, D(2026, 4, 1)), P, Now);

        Assert.False(credit.TreatmentAssessed);
        Assert.True(credit.ReferenceIsSubmission);
        Assert.Equal(120, credit.CadenceDays);
        Assert.Equal(67, credit.DelayDays, 9);
        Assert.Equal(6.0, credit.EffectiveResidual, 12);
        Assert.Equal(0.6, credit.SigmaR, 12);
    }

    [Fact]
    public void TestAResidualAboveTheInherentIsNotLoweredByAnOverdueReview()
    {
        var credit = ReviewCredit.Compute(Risk(5, 6, D(2025, 1, 1)), P, Now);

        Assert.True(credit.ResidualAboveInherent);
        Assert.Equal(6, credit.EffectiveResidual, 12);
    }

    [Theory]
    [InlineData(0.0, 240)]
    [InlineData(3.99, 240)]
    [InlineData(4.0, 120)]
    [InlineData(7.0, 90)]
    [InlineData(8.8, 90)]
    [InlineData(9.0, 30)]
    [InlineData(10.0, 30)]
    public void TestCadenceFromTheApprovedCopyOnTheRawScore(double score, int days) =>
        Assert.Equal(days, ReviewCredit.Cadence(score, P).Days);

    [Fact]
    public void TestTheCadenceBasisCanBeTheResidual()
    {
        var facts = Risk(9.5, 3.0, D(2026, 1, 1));

        Assert.Equal(30, ReviewCredit.Compute(facts, P, Now).CadenceDays);
        Assert.Equal(240, ReviewCredit.Compute(facts, P with { CadenceBasis = RiskIndexCadenceBasis.ResidualRisk }, Now).CadenceDays);
    }

    [Fact]
    public void TestCreditDecaysLinearlyToTheFloor()
    {
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, Now.AddDays(-180)));

        Assert.Equal(0, ReviewCredit.Compute(facts, P, Now).Credit, 12);
        Assert.Equal(8, ReviewCredit.Compute(facts, P, Now).EffectiveResidual, 12);
        Assert.Equal(0.2, ReviewCredit.Compute(facts, P with { MinimumCredit = 0.2 }, Now).Credit, 12);
    }

    [Fact]
    public void TestAnAcceptanceEndingBeforeTheLastQualifiedReviewIsIgnored()
    {
        var facts = Risk(8, 4, D(2026, 1, 1), Review(1, D(2026, 9, 1))) with
        {
            Acceptances = [Acceptance(1, D(2026, 1, 1), D(2026, 8, 1), RiskAcceptanceStatus.Expired)]
        };

        var credit = ReviewCredit.Compute(facts, P, Now);

        Assert.False(credit.DueByAcceptance);
        Assert.Equal(D(2026, 11, 30), credit.Due);
        Assert.Equal(1, credit.Credit);
    }
}
