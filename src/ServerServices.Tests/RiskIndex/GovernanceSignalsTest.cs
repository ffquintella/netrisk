using System;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Signals;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The governance signals of S39 §6.3 from reliable fields (T245), against the Unit X scenarios of S39 §11.1 and Example C
/// (§11.3), each guard branch, and the SLA reading of an asset.
/// </summary>
[TestSubject(typeof(GovernanceSignals))]
public class GovernanceSignalsTest
{
    private static readonly RiskIndexParameters P = new();
    private static readonly DateTime Now = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime D(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static ReviewFact Qualified(int id, DateTime at) => new() { Id = id, ReviewerId = 99, SubmissionDate = at };

    private static ScenarioGovernance Evaluate(ScenarioFacts facts, DateTime? now = null, bool divergence = false)
    {
        var at = now ?? Now;
        return GovernanceSignals.Compute(facts, ReviewCredit.Compute(facts, P, at), P, at, divergence);
    }

    private static double Signal(ScenarioGovernance g, string id) =>
        g.Signals.Where(s => s.Id == id).Select(s => s.Value).DefaultIfEmpty(0).Single();

    /// <summary>R1 of Unit X: accepted before the ceiling existed, expired 2026-09-20 with no successor; escalated in the
    /// 2026-08-25 campaign; two open tasks from July, one 12 days overdue.</summary>
    private static ScenarioFacts R1(params ReviewFact[] extraReviews) => new()
    {
        RiskId = 1, Inherent = 8.8, Residual = 8.4, SubmissionDate = D(1, 10), Owner = 1, SubmittedBy = 2, Manager = 3,
        AppetiteCeiling = 5.0, ResidualUpdatedAt = D(10, 4),
        Reviews = [Qualified(1, D(8, 21)), .. extraReviews],
        Acceptances =
        [
            new AcceptanceFact
            {
                Id = 1, CreatedAt = D(3, 20), ExpiresAt = D(9, 20), Status = RiskAcceptanceStatus.Expired,
                AuthorizingManagerId = 50, CreatedById = 50
            }
        ],
        CampaignItems =
        [
            new CampaignItemFact
            {
                Id = 1, CampaignId = 1, CampaignStatus = RiskReviewCampaignStatus.Completed, CampaignDueDate = D(8, 31),
                CampaignCreatedAt = D(8, 1), Decision = RiskReviewDecision.Escalated, DecidedAt = D(8, 25), DecidedById = 7,
                CreatedAt = D(8, 1)
            }
        ],
        Tasks =
        [
            new TaskFact { Id = 1, Status = MitigationTaskStatus.InProgress, CreatedAt = D(7, 2), DueDate = D(9, 23) },
            new TaskFact { Id = 2, Status = MitigationTaskStatus.Open, CreatedAt = D(7, 3), DueDate = D(12, 1) }
        ]
    };

    [Fact]
    public void TestUnitXR1()
    {
        var g = Evaluate(R1());

        Assert.Equal(0.5, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(1.0, Signal(g, GovernanceSignalIds.AcceptanceExpired));
        Assert.Equal(0.7, Signal(g, GovernanceSignalIds.CampaignEscalated));
        Assert.Equal(0.4, Signal(g, GovernanceSignalIds.TaskOverdue), 9);
        Assert.Equal(1.0, g.Factors[RiskIndexFactors.GovAppetite]);
        Assert.Equal(0.84, g.R, 9);
        Assert.True(g.AboveCeiling);
        Assert.False(g.HasValidAcceptance);
    }

    [Fact]
    public void TestExampleCAQualifiedReviewEndsTheExpiredAcceptanceAndTheEscalation()
    {
        var next = new DateTime(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc);
        var g = Evaluate(R1(Qualified(2, new DateTime(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc))), next);

        Assert.Equal(0, Signal(g, GovernanceSignalIds.AcceptanceExpired));
        Assert.Equal(0, Signal(g, GovernanceSignalIds.CampaignEscalated));
        Assert.Equal(0.5, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(0.84 * IcrMath.Mra([0.5, 13.2 / 30 * 0.8], P.Rho, P.KSignals), g.R, 3);
        Assert.InRange(g.R, 0.46, 0.47);
    }

    [Fact]
    public void TestUnitXR2PendingInAnOverdueCampaign()
    {
        var facts = new ScenarioFacts
        {
            RiskId = 2, Inherent = 8, Residual = 4, SubmissionDate = D(1, 1), AppetiteCeiling = 5.0,
            Reviews = [Qualified(1, D(5, 8))],
            CampaignItems =
            [
                new CampaignItemFact
                {
                    Id = 1, CampaignId = 2, CampaignStatus = RiskReviewCampaignStatus.Overdue, CampaignDueDate = D(9, 1),
                    CampaignCreatedAt = D(8, 1), Decision = RiskReviewDecision.Pending, CreatedAt = D(8, 1)
                }
            ]
        };

        var g = Evaluate(facts);

        Assert.Equal(1.0, Signal(g, GovernanceSignalIds.CampaignPending));
        Assert.Equal(0, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(0.32, g.R, 9);
    }

    [Fact]
    public void TestUnitXR3AboveTheCeilingUntreatedAndNeverReviewed()
    {
        var g = Evaluate(new ScenarioFacts { RiskId = 3, Inherent = 6, SubmissionDate = D(4, 1), AppetiteCeiling = 5.0 });

        Assert.Equal(1.0, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(67.0 / 120, Signal(g, GovernanceSignalIds.ReviewNoCredit), 9);
        Assert.Equal(0.6, g.R, 9);
    }

    [Fact]
    public void TestUnitXR4DivergenceIsScaledByTheDeclaredSeverity()
    {
        var g = Evaluate(new ScenarioFacts { RiskId = 4, Inherent = 4.8, Residual = 3, SubmissionDate = D(1, 1),
            Reviews = [Qualified(1, D(9, 1))], AppetiteCeiling = 5.0 }, divergence: true);

        Assert.Equal(0.8, Signal(g, GovernanceSignalIds.Divergence));
        Assert.Equal(0.192, g.R, 9);
    }

    private static ScenarioFacts Lapsed(DateTime end, bool revoked, double score = 8, double? ceiling = 5) => new()
    {
        RiskId = 5, Inherent = score, Residual = score, SubmissionDate = D(1, 1), AppetiteCeiling = ceiling,
        Reviews = [Qualified(1, D(1, 15))],
        Acceptances =
        [
            new AcceptanceFact
            {
                Id = 1, CreatedAt = D(2, 1), ExpiresAt = revoked ? D(12, 1) : end, RevokedAt = revoked ? end : null,
                Status = revoked ? RiskAcceptanceStatus.Revoked : RiskAcceptanceStatus.Expired, AuthorizingManagerId = 50
            }
        ]
    };

    [Fact]
    public void TestARevokedAcceptanceWithoutANewDecisionIsHalfTheExpiredSignal() =>
        Assert.Equal(0.5, Signal(Evaluate(Lapsed(D(9, 1), revoked: true)), GovernanceSignalIds.AcceptanceExpired));

    [Fact]
    public void TestALapsedAcceptanceHasAGracePeriod() =>
        Assert.Equal(0, Signal(Evaluate(Lapsed(D(10, 1), revoked: false)), GovernanceSignalIds.AcceptanceExpired));

    [Fact]
    public void TestALapsedAcceptanceOfAScenarioWithinTheCeilingNeedsNoNewDecision() =>
        Assert.Equal(0, Signal(Evaluate(Lapsed(D(9, 1), false, score: 4)), GovernanceSignalIds.AcceptanceExpired));

    [Fact]
    public void TestALapsedAcceptanceWithoutAnyCeilingStillNeedsADecision() =>
        Assert.Equal(1.0, Signal(Evaluate(Lapsed(D(9, 1), false, ceiling: null)), GovernanceSignalIds.AcceptanceExpired));

    private static ScenarioFacts Accepted(double residual, double? snapshot) => new()
    {
        RiskId = 6, Inherent = 8, Residual = residual, SubmissionDate = D(1, 1), AppetiteCeiling = 5.0,
        Reviews = [Qualified(1, D(9, 1))],
        Acceptances =
        [
            new AcceptanceFact
            {
                Id = 1, CreatedAt = D(9, 1), ExpiresAt = D(10, 20), Status = RiskAcceptanceStatus.Active,
                ResidualScoreSnapshot = snapshot, AuthorizingManagerId = 50
            }
        ]
    };

    [Fact]
    public void TestAValidAcceptanceAboveTheCurrentCeiling()
    {
        var g = Evaluate(Accepted(6, 6));

        Assert.Equal(0.5, Signal(g, GovernanceSignalIds.AcceptanceBeforeAppetite));
        Assert.Equal(0, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.True(g.AcceptedAboveCeiling);
        Assert.True(g.AcceptanceExpiringSoon);
    }

    [Theory]
    [InlineData(4.0, 4.4, 0)]
    [InlineData(5.0, 4.0, 1.0 / 3)]
    [InlineData(6.0, 4.0, 1.0)]
    public void TestAcceptanceDrift(double residual, double snapshot, double expected) =>
        Assert.Equal(expected, Signal(Evaluate(Accepted(residual, snapshot)), GovernanceSignalIds.AcceptanceDrift), 9);

    [Fact]
    public void TestACountersignatureOutstandingForAWeek()
    {
        var facts = Accepted(4, null) with
        {
            Reviews = [Qualified(1, D(9, 1)), new ReviewFact
            {
                Id = 2, ReviewerId = 50, SubmissionDate = D(9, 1), SavedWithAcceptance = true, RequiresCountersignature = true
            }]
        };

        Assert.Equal(0.5, Signal(Evaluate(facts), GovernanceSignalIds.CounterSignature));
    }

    private static ScenarioFacts Requested(DateTime at, bool fromAcceptance) => new()
    {
        RiskId = 7, Inherent = 4, Residual = 3, SubmissionDate = D(1, 1), Reviews = [Qualified(1, D(8, 1))],
        ReviewRequestedAt = at, ReviewRequestFromAcceptance = fromAcceptance
    };

    [Fact]
    public void TestARequestedReviewUnansweredForTwoWeeks()
    {
        Assert.Equal(1.0, Signal(Evaluate(Requested(D(9, 15), false)), GovernanceSignalIds.ReviewRequested));
        Assert.Equal(0, Signal(Evaluate(Requested(D(9, 25), false)), GovernanceSignalIds.ReviewRequested));
        Assert.Equal(0, Signal(Evaluate(Requested(D(7, 15), false)), GovernanceSignalIds.ReviewRequested));
        Assert.Equal(0, Signal(Evaluate(Requested(D(9, 15), true)), GovernanceSignalIds.ReviewRequested));
    }

    [Fact]
    public void TestTheReasonAnAcceptanceWritesIsRecognised()
    {
        Assert.True(RegisterFactsReader.IsAcceptanceRequestReason("A risk acceptance was revoked."));
        Assert.True(RegisterFactsReader.IsAcceptanceRequestReason("The risk acceptance 'Acceptance of R-1' expired on 2026-09-20."));
        Assert.False(RegisterFactsReader.IsAcceptanceRequestReason("Mandatory reassessment (KRI): CPU"));
        Assert.False(RegisterFactsReader.IsAcceptanceRequestReason(null));
    }

    private static ScenarioFacts Mitigation(params MitigationTaskStatus[] statuses) => new()
    {
        RiskId = 8, Inherent = 6, Residual = 4, SubmissionDate = D(1, 1), Reviews = [Qualified(1, D(9, 1))],
        CampaignItems =
        [
            new CampaignItemFact
            {
                Id = 1, CampaignId = 3, CampaignStatus = RiskReviewCampaignStatus.Completed, CampaignDueDate = D(9, 1),
                CampaignCreatedAt = D(8, 1), Decision = RiskReviewDecision.MitigationRequested,
                DecidedAt = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc), CreatedAt = D(8, 1)
            }
        ],
        Tasks = statuses.Select((s, i) => new TaskFact
        {
            Id = i + 1, Status = s, CreatedAt = new DateTime(2026, 8, 20, 10, 0, 1, DateTimeKind.Utc)
        }).ToList()
    };

    [Fact]
    public void TestAMitigationDecisionWhoseTasksWereAllCancelled()
    {
        Assert.Equal(0.5, Signal(Evaluate(Mitigation(MitigationTaskStatus.Cancelled, MitigationTaskStatus.Cancelled)),
            GovernanceSignalIds.CampaignMitigation));
        Assert.Equal(0, Signal(Evaluate(Mitigation(MitigationTaskStatus.Cancelled, MitigationTaskStatus.Open)),
            GovernanceSignalIds.CampaignMitigation));
    }

    [Fact]
    public void TestWithoutACeilingTheAppetitePartLeavesTheCoverage()
    {
        var g = Evaluate(new ScenarioFacts { RiskId = 9, Inherent = 9, SubmissionDate = D(1, 1) });

        Assert.False(g.AppetiteConfigured);
        Assert.Equal(0, Signal(g, GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(1 - 1.0 / 4.4, GovernanceSignals.CoverageEffect(P, false), 9);
        Assert.Equal(1, GovernanceSignals.CoverageEffect(P, true));
    }

    [Fact]
    public void TestCompletingTheTreatmentAfterTheLastResidualPassHalvesTheBreach()
    {
        var facts = new ScenarioFacts
        {
            RiskId = 10, Inherent = 8, Residual = 7, SubmissionDate = D(1, 1), AppetiteCeiling = 5, ResidualUpdatedAt = D(10, 1),
            Reviews = [Qualified(1, D(9, 1))],
            Tasks = [new TaskFact { Id = 1, Status = MitigationTaskStatus.Completed, CreatedAt = D(9, 1), CompletedAt = D(10, 3) }]
        };

        Assert.Equal(0.5, Signal(Evaluate(facts), GovernanceSignalIds.AppetiteBreach));
        Assert.Equal(1.0, Signal(Evaluate(facts with { ResidualUpdatedAt = D(10, 4) }), GovernanceSignalIds.AppetiteBreach));
    }

    [Fact]
    public void TestSlaOfAnAsset()
    {
        // SRV-01 of Unit X: a critical CVE 6 days past a 15-day deadline.
        var due = Now.AddDays(-6);
        Assert.Equal(0.38, GovernanceSignals.Sla(P, [new GovernanceSignals.SlaFinding(4, due.AddDays(-15), due)], Now), 9);
        Assert.Equal(0, GovernanceSignals.Sla(P, [new GovernanceSignals.SlaFinding(4, Now, Now.AddDays(5))], Now));
        Assert.Equal(0, GovernanceSignals.Sla(P, [], Now));
    }
}
