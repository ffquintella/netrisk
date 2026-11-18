using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// T278 (S39 D-08, §6.1): the overdue-review list is dated by the rule the index reads. Only a qualified review restarts the
/// cadence; an acceptance's end brings the due date forward and renewing it does not push it back; an earlier
/// next-review date set by the qualified reviewer is honoured, a later one never postpones. The list used to take the
/// latest review of any kind plus the cadence, and read neither acceptances nor <c>NextReview</c>. The levels are keyless,
/// so the pure resolution is tested here and the scoped read in <c>MgmtReviewsOverdueReadInMemoryTest</c>.
/// </summary>
[TestSubject(typeof(MgmtReviewsService))]
public class MgmtReviewsOverdueTest
{
    private static readonly DateTime AsOf = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private const int Submitter = 2, RiskOwner = 3, RiskManager = 4, Reviewer = 7, Authorizer = 8;

    private static readonly List<RiskLevel> Levels =
    [
        new() { Value = 0.0m, Name = "Low", DisplayName = "Low", Color = "yellow" },
        new() { Value = 4.0m, Name = "Medium", DisplayName = "Medium", Color = "orange" },
        new() { Value = 7.0m, Name = "High", DisplayName = "High", Color = "orangered" },
        new() { Value = 9.0m, Name = "Very High", DisplayName = "Very High", Color = "red" }
    ];

    private static readonly List<ReviewLevel> Cadences =
    [
        new() { Id = 1, Name = "Very High", Value = 30 },
        new() { Id = 2, Name = "High", Value = 90 },
        new() { Id = 3, Name = "Medium", Value = 120 },
        new() { Id = 4, Name = "Low", Value = 240 }
    ];

    private static DateTime D(int month, int day, int year = 2026) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static ReviewEvidence Review(int id, DateTime at, int reviewer = Reviewer) =>
        new() { Id = id, ReviewerId = reviewer, SubmissionDate = at };

    /// <summary>An acceptance, any status — the rule reads its end (revocation, else expiry), not its status.</summary>
    private static AcceptanceEvidence Acceptance(DateTime createdAt, DateTime expiresAt, DateTime? revokedAt = null) =>
        new() { CreatedAt = createdAt, End = revokedAt ?? expiresAt, AuthorizingManagerId = Authorizer };

    private static ReviewHistory History(IEnumerable<ReviewEvidence>? reviews = null,
        IEnumerable<AcceptanceEvidence>? acceptances = null, IEnumerable<CampaignDecisionEvidence>? decisions = null) => new()
    {
        SubmissionDate = D(1, 1), SubmittedBy = Submitter, Owner = RiskOwner, Manager = RiskManager,
        Reviews = reviews?.ToList() ?? [], Acceptances = acceptances?.ToList() ?? [], Decisions = decisions?.ToList() ?? []
    };

    /// <summary>A High risk (cadence 90 days) with the given history and next-review dates.</summary>
    private static MgmtReviewsService.OverdueCandidate Candidate(ReviewHistory history,
        Dictionary<int, DateOnly>? nextReviews = null, int riskId = 1) =>
        new(new Risk
            {
                Id = riskId, Subject = "Payment gateway", ReferenceId = "R-1", Status = "Mgmt Reviewed",
                Owner = RiskOwner, Manager = RiskManager, EntityId = 100, Assessment = "", Notes = "",
                RiskCatalogMapping = "", ThreatCatalogMapping = ""
            },
            new RiskScoring { Id = riskId, CalculatedRisk = 8f },
            history, nextReviews ?? new Dictionary<int, DateOnly>());

    private static List<OverdueReview> Overdue(params MgmtReviewsService.OverdueCandidate[] candidates) =>
        MgmtReviewsService.ResolveOverdue(candidates, null, Levels, Cadences, AsOf);

    [Fact]
    public void TestAQualifiedReviewPastItsCadenceIsOverdue()
    {
        var item = Assert.Single(Overdue(Candidate(History([Review(1, D(6, 1))]))));

        Assert.Equal((D(8, 30), ReviewDueBasis.Cadence, 90, 80), (item.DueAt, item.DueBasis, item.CadenceDays, item.DaysOverdue));
        Assert.Equal(D(6, 1), item.LastReviewedAt);
    }

    [Fact]
    public void TestAReviewInsideItsCadenceIsNotOverdue()
    {
        Assert.Empty(Overdue(Candidate(History([Review(1, D(10, 1))]))));
    }

    [Fact]
    public void TestANeverReviewedRiskFallsDueFromItsSubmission()
    {
        var item = Assert.Single(Overdue(Candidate(History())));

        Assert.Null(item.LastReviewedAt);
        Assert.Equal((D(4, 1), ReviewDueBasis.Cadence), (item.DueAt, item.DueBasis));
    }

    /// <summary>The reviewer asked for an earlier look: due once that day is over. It used to be ignored.</summary>
    [Fact]
    public void TestAnEarlierNextReviewDateBringsTheDueDateForward()
    {
        var facts = History([Review(1, D(10, 1))]);

        var item = Assert.Single(Overdue(Candidate(facts, new() { [1] = new DateOnly(2026, 11, 1) })));

        Assert.Equal((D(11, 2), ReviewDueBasis.NextReview, 16), (item.DueAt, item.DueBasis, item.DaysOverdue));
    }

    /// <summary>A next-review date beyond the cadence does not postpone the review: the client cannot buy time.</summary>
    [Fact]
    public void TestALaterNextReviewDateNeverPostponesTheCadence()
    {
        var facts = History([Review(1, D(6, 1))]);

        var item = Assert.Single(Overdue(Candidate(facts, new() { [1] = new DateOnly(2027, 6, 1) })));

        Assert.Equal((D(8, 30), ReviewDueBasis.Cadence), (item.DueAt, item.DueBasis));
    }

    /// <summary>A next-review date before the review itself (the column's default, 0001-01-01) is a data error, not a deadline.</summary>
    [Fact]
    public void TestANextReviewDateBeforeTheReviewIsIgnored()
    {
        var facts = History([Review(1, D(10, 1))]);

        Assert.Empty(Overdue(Candidate(facts, new() { [1] = default })));
        Assert.Empty(Overdue(Candidate(facts, new() { [1] = new DateOnly(2026, 9, 1) })));
    }

    /// <summary>Only the qualified reviewer's date counts: an acceptance's own review row sets one too (its expiry).</summary>
    [Fact]
    public void TestOnlyTheReferenceReviewsNextReviewDateCounts()
    {
        var facts = History([Review(1, D(10, 1)), Review(2, D(10, 2), reviewer: RiskOwner)]);

        Assert.Empty(Overdue(Candidate(facts, new() { [2] = new DateOnly(2026, 10, 3) })));
    }

    [Fact]
    public void TestAnAcceptanceExpiringBeforeTheCadenceBringsTheDueDateForward()
    {
        var facts = History([Review(1, D(9, 1))], [Acceptance(D(9, 2), D(10, 1))]);

        var item = Assert.Single(Overdue(Candidate(facts)));

        Assert.Equal((D(10, 1), ReviewDueBasis.AcceptanceEnd, 48), (item.DueAt, item.DueBasis, item.DaysOverdue));
    }

    [Fact]
    public void TestARevokedAcceptanceIsDueFromItsRevocation()
    {
        var facts = History([Review(1, D(9, 1))],
            [Acceptance(D(9, 2), D(1, 1, 2027), revokedAt: D(10, 15))]);

        var item = Assert.Single(Overdue(Candidate(facts)));

        Assert.Equal((D(10, 15), ReviewDueBasis.AcceptanceEnd), (item.DueAt, item.DueBasis));
    }

    /// <summary>An acceptance that ended before the last qualified review is history: the review answered it.</summary>
    [Fact]
    public void TestAnAcceptanceEndingBeforeTheLastQualifiedReviewIsIgnored()
    {
        var facts = History([Review(1, D(10, 1))], [Acceptance(D(6, 1), D(9, 1))]);

        Assert.Empty(Overdue(Candidate(facts)));
    }

    /// <summary>
    /// Renewing is not reviewing (S39 §6.1, I10): the earliest end after the last qualified review still sets the date, and
    /// the renewal's own review row — same risk, its authorizer, within the window — does not restart the cadence.
    /// </summary>
    [Fact]
    public void TestRenewingAnAcceptanceDoesNotPushTheDueDateBack()
    {
        var renewedAt = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        var facts = History(
            [Review(1, D(9, 1)), Review(2, renewedAt.AddSeconds(1), reviewer: Authorizer)],
            [
                Acceptance(D(9, 2), D(10, 1)), // renewed: superseded by the next one, its end still counts
                Acceptance(renewedAt, D(12, 31))
            ]);

        var item = Assert.Single(Overdue(Candidate(facts)));

        Assert.Equal((D(10, 1), ReviewDueBasis.AcceptanceEnd, (DateTime?)D(9, 1)), (item.DueAt, item.DueBasis, item.LastReviewedAt));
    }

    /// <summary>Accepting a risk is not reviewing it: the acceptance's review row does not restart an overdue cadence.</summary>
    [Fact]
    public void TestAnAcceptancesReviewRowDoesNotRestartTheCadence()
    {
        var acceptedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var facts = History([Review(1, D(6, 1)), Review(2, acceptedAt, reviewer: Authorizer) with { SavedWithAcceptance = true }],
            [Acceptance(acceptedAt, D(4, 1, 2027))]);

        var item = Assert.Single(Overdue(Candidate(facts)));

        Assert.Equal(((DateTime?)D(6, 1), D(8, 30)), (item.LastReviewedAt, item.DueAt));
    }

    /// <summary>The risk's own submitter, owner or manager reviewing it (with a segregation override) restores nothing.</summary>
    [Theory]
    [InlineData(Submitter)]
    [InlineData(RiskOwner)]
    [InlineData(RiskManager)]
    public void TestASelfReviewDoesNotRestartTheCadence(int selfReviewer)
    {
        var item = Assert.Single(Overdue(Candidate(History([Review(1, D(6, 1)), Review(2, D(11, 1), selfReviewer)]))));

        Assert.Equal(D(6, 1), item.LastReviewedAt);
    }

    [Fact]
    public void TestACampaignDecisionsReviewRowDoesNotRestartTheCadence()
    {
        var decidedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var facts = History([Review(1, D(6, 1)), Review(2, decidedAt.AddSeconds(5))],
            decisions: [new CampaignDecisionEvidence(decidedAt, Reviewer)]);

        Assert.Equal(D(6, 1), Assert.Single(Overdue(Candidate(facts))).LastReviewedAt);
    }

    /// <summary>A review dated ahead counts from when the audit trail saw it created, not from the date it claims.</summary>
    [Fact]
    public void TestAFutureDatedReviewCountsFromItsAuditedCreation()
    {
        var facts = History([Review(1, D(6, 1, 2027)) with { AuditedAt = D(6, 1), CorrelationId = "c1" }]);

        var item = Assert.Single(Overdue(Candidate(facts)));

        Assert.Equal(((DateTime?)D(6, 1), D(8, 30)), (item.LastReviewedAt, item.DueAt));
    }

    /// <summary>The product list's derived-review window is the index's default one, so the two classify a review alike.</summary>
    [Fact]
    public void TestTheDerivedReviewWindowIsTheIndexDefault()
    {
        Assert.Equal(new Model.RiskIndex.RiskIndexParameters().DerivedReviewWindowSeconds,
            ReviewDueRule.DefaultDerivedWindow.TotalSeconds);
    }

    [Fact]
    public void TestBandsThatResolveNoCadenceListNothing()
    {
        Assert.Empty(MgmtReviewsService.ResolveOverdue([Candidate(History())], null, [], Cadences, AsOf));
    }

    [Fact]
    public void TestTheMostOverdueComeFirst()
    {
        var recent = Candidate(History([Review(1, D(8, 1))]), riskId: 2);
        var old = Candidate(History([Review(1, D(5, 1))]), riskId: 3);

        Assert.Equal(new[] { 3, 2 }, Overdue(recent, old).Select(o => o.RiskId));
    }
}
