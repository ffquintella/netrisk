using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.Track9;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestone 8.3.2 on the path a management review is actually recorded through. <c>POST /MgmtReviews</c>
/// reaches <see cref="MgmtReviewsService.CreateReviewAsync"/>, which refuses a review by anyone who submitted, owns or
/// manages the risk — administrators included — and checks the <em>caller</em>, never the reviewer the payload names.
///
/// Before this, the route reached a legacy <c>Create(MgmtReview)</c> that carried no acting user and checked nothing
/// (S53 §11, defect 2), and no test drove segregation of duties through <c>CreateReviewAsync</c>: deleting the check
/// from it left the whole suite green.
/// </summary>
[TestSubject(typeof(MgmtReviewsService))]
public class MgmtReviewSegregationInMemoryTest : DecisionCycleTestBase
{
    private const int RiskId = 1;

    // Three different people, so each relation is refused on its own rather than all three at once.
    private const int Submitter = MemberA;
    private const int RiskOwner = MemberB;
    private const int ManagerOfRisk = MemberC;

    /// <summary>The analyst seeded by the chain base: no relation to the risk, not an administrator.</summary>
    private const int Uninvolved = Author;

    private IMgmtReviewsService Reviews => GetService<IMgmtReviewsService>();

    public MgmtReviewSegregationInMemoryTest()
    {
        AddRisk(RiskId, UnitA);
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == RiskId);
            risk.SubmittedBy = Submitter;
            risk.Owner = RiskOwner;
            risk.Manager = ManagerOfRisk;
            risk.ReviewRequested = true;

            // The read-back includes the review's type and next step, required navigations an unseeded row would hide.
            ctx.Reviews.Add(new Review { Value = 1, Name = "Accept the risk" });
            ctx.NextSteps.Add(new NextStep { Value = 2, Name = "Accept until next review" });
        });
    }

    private static MgmtReview Payload(int namedReviewer = 0) => new()
    {
        RiskId = RiskId, Review = 1, NextStep = 2, Reviewer = namedReviewer, Comments = "Re-assessed with the unit.",
        NextReview = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
    };

    private int ReviewCount() => Read(ctx => ctx.MgmtReviews.Count(r => r.RiskId == RiskId));

    /// <summary>MR1 — whoever submitted, owns or manages the risk is refused, by name of the rule; nothing is written and
    /// the open review request stays open.</summary>
    [Theory]
    [InlineData(Submitter, "submitted it")]
    [InlineData(RiskOwner, "own it")]
    [InlineData(ManagerOfRisk, "manage it")]
    public async Task TestMR1_TheRisksOwnPeopleCannotReviewIt(int caller, string relation)
    {
        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Reviews.CreateReviewAsync(Payload(), caller));

        Assert.Equal("segregation_of_duties", ex.RuleName);
        Assert.Contains(relation, ex.Message);
        Assert.Equal(0, ReviewCount());
        Assert.True(RiskRow(RiskId).ReviewRequested);
    }

    /// <summary>MR2 — the administrator flag lifts nothing: an administrator who owns the risk is refused too.</summary>
    [Fact]
    public async Task TestMR2_AnAdministratorDoesNotBypassSegregation()
    {
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == RiskId).Owner = Cro);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Reviews.CreateReviewAsync(Payload(), Cro));

        Assert.Equal("segregation_of_duties", ex.RuleName);
        Assert.Equal(0, ReviewCount());
    }

    /// <summary>
    /// MR3 — the check binds the caller, not the payload. The owner naming an uninvolved colleague as the reviewer is
    /// still the owner and is refused; an uninvolved caller naming the owner is recorded as the reviewer themself, so no
    /// row ever carries a reviewer the check did not see.
    /// </summary>
    [Fact]
    public async Task TestMR3_TheCheckBindsTheCallerNotTheNamedReviewer()
    {
        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Reviews.CreateReviewAsync(Payload(namedReviewer: Uninvolved), RiskOwner));
        Assert.Equal("segregation_of_duties", ex.RuleName);
        Assert.Equal(0, ReviewCount());

        var review = await Reviews.CreateReviewAsync(Payload(namedReviewer: RiskOwner), Uninvolved);

        Assert.Equal(Uninvolved, Read(ctx => ctx.MgmtReviews.Single(r => r.Id == review.Id).Reviewer));
    }

    /// <summary>MR4 — the control: someone with no relation to the risk reviews it, the row is written in their name with
    /// no override recorded, and the review answers the open request.</summary>
    [Fact]
    public async Task TestMR4_AnUninvolvedReviewerIsRecorded()
    {
        var review = await Reviews.CreateReviewAsync(Payload(), Uninvolved);

        Assert.True(review.Id > 0);
        var stored = Read(ctx => ctx.MgmtReviews.Single(r => r.RiskId == RiskId));
        Assert.Equal((review.Id, Uninvolved), (stored.Id, stored.Reviewer));
        Assert.Null(stored.SegregationOverrideReason);
        Assert.False(RiskRow(RiskId).ReviewRequested);
    }

    /// <summary>
    /// MR5 — by construction: no method of <see cref="IMgmtReviewsService"/> takes a review to persist without the user
    /// acting. A method that did could not apply segregation of duties to anyone, and the legacy <c>Create(MgmtReview)</c>
    /// was exactly that — the one <c>POST /MgmtReviews</c> called.
    /// </summary>
    [Fact]
    public void TestMR5_EveryReviewWritingMethodNamesTheActingUser()
    {
        var unbound = typeof(IMgmtReviewsService).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(MgmtReview)))
            .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(int) && p.Name == "actingUserId"))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(unbound);
    }

    [Fact]
    public async Task TestMR6_UpdateCannotBypassSegregationOrRewriteReviewIdentity()
    {
        var created = await Reviews.CreateReviewAsync(Payload(), Uninvolved);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Reviews.UpdateAsync(new MgmtReviewDto
        {
            Id = created.Id,
            RiskId = 999,
            Reviewer = RiskOwner,
            SubmissionDate = new DateTime(2030, 1, 1),
            Review = 1,
            NextStep = 2,
            Comments = "Owner tried to rewrite the review.",
            NextReview = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(120))
        }, RiskOwner));

        Assert.Equal("segregation_of_duties", ex.RuleName);
        var stored = Read(ctx => ctx.MgmtReviews.Single(r => r.Id == created.Id));
        Assert.Equal((RiskId, Uninvolved, "Re-assessed with the unit."),
            (stored.RiskId, stored.Reviewer, stored.Comments));
    }

    /// <summary>
    /// MR7 — T278 (S39 D-07) regression: the review is dated by the server, in UTC, whatever the payload says. The date
    /// was kept as sent unless it was empty, so a client could post a review dated a year ahead and keep the risk off the
    /// overdue list (and keep its treatment credit, S39 §6.1) for a year; the desktop sent its local time.
    /// </summary>
    [Theory]
    [InlineData(365)]
    [InlineData(-400)]
    public async Task TestMR7_TheSubmissionDateIsStampedInUtcByTheServer(int clientOffsetDays)
    {
        var payload = Payload();
        payload.SubmissionDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(clientOffsetDays), DateTimeKind.Local);

        var before = DateTime.UtcNow;
        var review = await Reviews.CreateReviewAsync(payload, Uninvolved);
        var after = DateTime.UtcNow;

        var stored = Read(ctx => ctx.MgmtReviews.Single(r => r.Id == review.Id));
        Assert.InRange(stored.SubmissionDate, before, after);
        Assert.Equal(DateTimeKind.Utc, review.SubmissionDate.Kind);
    }

    /// <summary>
    /// MR8 — T278 (S39 D-07) regression: the desktop saves the whole risk right after its review, from the copy it held
    /// before the review — which still says a review is requested. The update copied every field of that copy, so the
    /// flag <see cref="MgmtReviewsService.CreateReviewAsync"/> had just cleared came straight back and REV-REQ stayed lit.
    /// The flag is server-owned: a risk save neither sets nor clears it.
    /// </summary>
    [Fact]
    public async Task TestMR8_SavingAStaleRiskDoesNotReopenTheReviewRequestTheReviewAnswered()
    {
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == RiskId);
            risk.ReviewRequestedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            risk.ReviewRequestedReason = "A major incident touched this process.";
        });
        var staleCopy = RiskRow(RiskId);
        Assert.True(staleCopy.ReviewRequested);

        await Reviews.CreateReviewAsync(Payload(), Uninvolved);
        await GetService<IRisksService>().SaveRiskAsync(staleCopy);

        var stored = RiskRow(RiskId);
        Assert.False(stored.ReviewRequested);
        Assert.Null(stored.ReviewRequestedReason);
    }

    /// <summary>MR9 — the same through the legacy synchronous save, which the reopen path still uses.</summary>
    [Fact]
    public async Task TestMR9_TheSynchronousSaveLeavesTheReviewRequestAlone()
    {
        var staleCopy = RiskRow(RiskId);

        await Reviews.CreateReviewAsync(Payload(), Uninvolved);
        GetService<IRisksService>().SaveRisk(staleCopy);

        Assert.False(RiskRow(RiskId).ReviewRequested);

        // And the other way round: a payload cannot raise the flag either — that is RequestReviewAsync's job, which
        // records why.
        var raising = RiskRow(RiskId);
        raising.ReviewRequested = true;
        raising.ReviewRequestedReason = "Set from the form.";
        GetService<IRisksService>().SaveRisk(raising);

        Assert.False(RiskRow(RiskId).ReviewRequested);
        Assert.Null(RiskRow(RiskId).ReviewRequestedReason);
    }
}
