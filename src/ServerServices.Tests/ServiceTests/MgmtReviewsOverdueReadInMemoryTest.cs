using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Governance;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// T278 (S39 D-08): what the overdue-review list reads, through a real context — only the open risks of the caller's
/// scope (the list is now served to users by <c>GET /MgmtReviews/Overdue</c>), each review's next-review date, and the
/// audit correlation that marks an acceptance's own review row. The dating rule is <c>MgmtReviewsOverdueTest</c>.
/// </summary>
[TestSubject(typeof(MgmtReviewsService))]
public class MgmtReviewsOverdueReadInMemoryTest : InMemoryServiceTestBase
{
    private const int UnitA = 100, UnitB = 200;
    private const int Reviewer = 7, Authorizer = 8;

    private static readonly DateTime Submitted = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Risk NewRisk(int id, int unit, string status = "New") => new()
    {
        Id = id, Status = status, Subject = $"Risk {id}", ReferenceId = $"R-{id}", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = Submitted, LastUpdate = Submitted,
        EntityId = unit, Owner = 2, Manager = 2, SubmittedBy = 2
    };

    private static MgmtReview NewReview(int id, int riskId, int reviewer, DateTime at, DateOnly nextReview) => new()
    {
        Id = id, RiskId = riskId, Reviewer = reviewer, SubmissionDate = at, Review = 1, NextStep = 1, Comments = "",
        NextReview = nextReview
    };

    public MgmtReviewsOverdueReadInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Risks.AddRange(NewRisk(1, UnitA), NewRisk(2, UnitB), NewRisk(3, UnitA, status: "Closed"));
            foreach (var id in new[] { 1, 2, 3 })
                ctx.RiskScorings.Add(new RiskScoring { Id = id, ScoringMethod = 1, CalculatedRisk = 8f });

            ctx.MgmtReviews.Add(NewReview(11, 1, Reviewer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateOnly(2026, 7, 1)));
        });
    }

    [Fact]
    public async Task TestAScopedCallerReadsOnlyItsOpenRisks()
    {
        ScopeTo(UnitA);

        await using var db = OpenContext();
        var candidates = await MgmtReviewsService.ReadOverdueCandidatesAsync(db);

        var candidate = Assert.Single(candidates);
        Assert.Equal(1, candidate.Risk.Id);
        Assert.Equal(new DateOnly(2026, 7, 1), candidate.NextReviews[11]);
    }

    [Fact]
    public async Task TestAnUnrestrictedCallerReadsEveryOpenRisk()
    {
        await using var db = OpenContext();

        var ids = (await MgmtReviewsService.ReadOverdueCandidatesAsync(db)).Select(c => c.Risk.Id).Order();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    /// <summary>
    /// An acceptance writes its review row in the same save; the interceptor gives both rows one correlation id, and that
    /// is what marks the row as the acceptance's — so it does not restart the cadence, whoever signed it.
    /// </summary>
    [Fact]
    public async Task TestAnAcceptancesReviewRowIsRecognisedThroughTheAuditTrail()
    {
        var acceptedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Seed(ctx =>
        {
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 30, RiskId = 2, EntityId = UnitB, Name = "Exception", BusinessJustification = "Compensating control.",
                AuthorizingManagerId = Authorizer, StartDate = acceptedAt, ExpiresAt = acceptedAt.AddDays(180),
                Status = RiskAcceptanceStatus.Active, CreatedAt = acceptedAt
            });
            // Signed at a different minute, so only the correlation id — not the 60 s fallback — can tell.
            ctx.MgmtReviews.Add(NewReview(21, 2, Reviewer, acceptedAt.AddMinutes(5), DateOnly.FromDateTime(acceptedAt.AddDays(180))));
        });

        await using var db = OpenContext();
        var candidate = (await MgmtReviewsService.ReadOverdueCandidatesAsync(db)).Single(c => c.Risk.Id == 2);

        var review = Assert.Single(candidate.History.Reviews);
        Assert.True(review.SavedWithAcceptance);

        var (due, basis, reference) = MgmtReviewsService.ResolveReviewDue(candidate.History, candidate.NextReviews, 90,
            new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(reference);
        Assert.Equal((Submitted.AddDays(90), ReviewDueBasis.Cadence), (due, basis));
    }
}
