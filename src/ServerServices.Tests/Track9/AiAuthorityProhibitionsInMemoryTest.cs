using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.AiGovernance;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Governance;
using Model.RiskFlags;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.12 (S53 §8, PR1–PR6; T215) — the Phase 6 prohibitions of MIGR-TI/IA still hold after the AI model inventory
/// exists, asserted against the real guards of the decision services: an approver a request <em>names</em> — the
/// authorizing manager of an acceptance, a committee member, a business reviewer, a model's owner — must be an existing
/// user; the acting reviewer of a management review is always the caller; and none of it changes when the risk is an AI
/// component's, linked to an inventoried model with flag 11 derived.
///
/// The half where the <em>caller</em> is not a user is the API's: every policy requires a valid user
/// (<c>API.Tests/Security/NonUserApprovalInventoryTest</c>). Together they are the "by construction" guarantee S27 asks to
/// keep under test, "so that it is not lost in a future refactoring".
/// </summary>
[TestSubject(typeof(RiskAcceptancesService))]
public class AiAuthorityProhibitionsInMemoryTest : DecisionCycleTestBase
{
    private const int NoSuchUser = 999;

    private IRiskAcceptancesService Acceptances => GetService<IRiskAcceptancesService>();

    private IAiGovernanceService AiModels => GetService<IAiGovernanceService>();

    private static RiskAcceptanceRequest Acceptance(int? authorizer = null) => new()
    {
        Name = "Exception", BusinessJustification = "Insured and monitored.", ExpiresAt = DateTime.UtcNow.AddDays(90),
        AuthorizingManagerId = authorizer
    };

    /// <summary>Risk 1, reviewed and owned by <see cref="DecisionCycleTestBase.Owner"/>, linked to an inventoried model in
    /// production: an AI component's risk, flag 11 derived.</summary>
    private async Task<int> AiComponentRiskAsync()
    {
        ReviewedRisk(1);

        var model = (await AiModels.CreateAsync(new AiModelRequest
        {
            Name = "Admissions triage", Purpose = "Ranks applications for an admissions officer.",
            Kind = AiModelKind.Classification, Source = AiModelSource.InHouse, Version = "2.1",
            Status = AiModelStatus.Production, RiskTier = AiModelRiskTier.High, HumanOversight = AiHumanOversight.EveryOutput,
            OwnerId = Owner, MaxEvaluationAgeDays = 90
        }, Cro)).Id;
        await AiModels.LinkRiskAsync(model, 1, new AiModelRiskLinkRequest { Note = "Bias against transfer students." }, Cro);

        var flags = await GetService<IRiskFlagsService>().RefreshAsync(1);
        Assert.True(flags.Flags.Single(f => f.Code == RiskFlagCode.ArtificialIntelligence).Derived);
        return model;
    }

    /// <summary>
    /// The refusal is the user lookup — the band-authority guard of the acceptance service throws "user not found" for the
    /// id it was asked to resolve —, not a missing risk or acceptance, which are the same exception type.
    /// </summary>
    private static void AssertUnknownUser(DataNotFoundException ex, int userId)
    {
        Assert.Equal("user", ex.Identification);
        Assert.Equal($"User with id {userId} not found", ex.InnerException?.Message);
    }

    private void AssertNothingDecided()
    {
        Assert.Empty(Read(ctx => ctx.RiskAcceptances.ToList()));
        Assert.Equal(1, Read(ctx => ctx.MgmtReviews.Count())); // only the seeded review that settled the risk
        Assert.Empty(Read(ctx => ctx.RiskCommitteeVotes.ToList()));
    }

    /// <summary>
    /// PR1 — residual risk is accepted only by a person: an acceptance naming as its authorizing manager the background actor
    /// (id 0), an id that is no user, or a negative id passes Gate A, the third-line guard and segregation of duties — none
    /// of which knows the id — and is refused by the band-authority guard's user lookup, naming the id; nothing is written —
    /// on an AI component's risk, with flag 11 derived.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(NoSuchUser)]
    [InlineData(-1)]
    public async Task TestPR1_AnAcceptanceNamingANonUserIsRefused(int authorizer)
    {
        await AiComponentRiskAsync();

        AssertUnknownUser(await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Acceptances.CreateAsync(1, Acceptance(authorizer), Cro)), authorizer);

        AssertNothingDecided();
    }

    /// <summary>
    /// PR2 — nor by a caller that is not a person: the background actor accepting in its own name is refused, and so is a
    /// renewal; the control — a person with authority — is accepted, so the refusals above are the guard.
    /// </summary>
    [Fact]
    public async Task TestPR2_ANonUserCallerCannotAcceptOrRenew()
    {
        await AiComponentRiskAsync();

        AssertUnknownUser(await Assert.ThrowsAsync<DataNotFoundException>(() => Acceptances.CreateAsync(1, Acceptance(), 0)), 0);
        AssertNothingDecided();

        var accepted = await Acceptances.CreateAsync(1, Acceptance(), Cro);
        Assert.Equal((RiskAcceptanceStatus.Active, Cro), (accepted.Status, accepted.AuthorizingManagerId));

        AssertUnknownUser(await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Acceptances.RenewAsync(accepted.Id, Acceptance(), 0)), 0);
        AssertUnknownUser(await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Acceptances.RenewAsync(accepted.Id, Acceptance(NoSuchUser), Cro)), NoSuchUser);
        Assert.Single(Read(ctx => ctx.RiskAcceptances.ToList()));
    }

    /// <summary>
    /// PR3 — a collegiate decision is taken by people: no one who is not a user sits on a committee, and a vote from one —
    /// the background actor included — is refused as not a member; nothing is recorded.
    /// </summary>
    [Fact]
    public async Task TestPR3_ACommitteeIsMadeOfPeople()
    {
        await AiComponentRiskAsync();
        var committees = GetService<IRiskCommitteesService>();
        var committee = (await committees.CreateCommitteeAsync(new RiskCommitteeRequest
            { Name = "IT Risk Committee", Mandate = "Board resolution 2026/03.", RequiredApprovals = 2 }, Cro)).Id;

        foreach (var nonUser in new[] { 0, NoSuchUser })
        {
            var ex = await Assert.ThrowsAsync<DataNotFoundException>(() => committees.AddMemberAsync(committee, nonUser, Cro));
            Assert.Equal(("user", nonUser.ToString()), (ex.DatabaseName, ex.Identification));
        }

        foreach (var member in new[] { MemberA, MemberB, MemberC }) await committees.AddMemberAsync(committee, member, Cro);
        var decision = (await committees.OpenDecisionAsync(committee, new RiskCommitteeDecisionRequest
        {
            RiskId = 1, Kind = RiskCommitteeDecisionKind.Accept, Name = "Exception",
            BusinessJustification = "Insured and monitored.", ExpiresAt = DateTime.UtcNow.AddDays(90),
            MinutesReference = "Minutes 2026-10, item 2"
        }, Cro)).Id;

        foreach (var nonUser in new[] { 0, NoSuchUser })
            await Assert.ThrowsAsync<PermissionInvalidException>(() => committees.VoteAsync(decision,
                new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Approve, Comment = "Automated." }, nonUser));

        AssertNothingDecided();
        Assert.Equal([MemberA, MemberB, MemberC],
            Read(ctx => ctx.RiskCommitteeMembers.Select(m => m.UserId).OrderBy(id => id).ToArray()));
    }

    /// <summary>PR4 — a business reviewer, who decides risks in the portal, is a person: a non-user is not appointed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(NoSuchUser)]
    public async Task TestPR4_ABusinessReviewerIsAPerson(int nonUser)
    {
        await AiComponentRiskAsync();

        var ex = await Assert.ThrowsAsync<DataNotFoundException>(() =>
            GetService<IEntityRiskReviewersService>().AppointAsync(UnitA, nonUser, true, Cro));
        AssertUnknownUser(ex, nonUser);

        Assert.Empty(Read(ctx => ctx.EntityRiskReviewers.ToList()));
    }

    /// <summary>
    /// PR5 — the reviewer of a management review is always the caller, whatever the payload says: a review naming the
    /// background actor (0) as its reviewer is recorded with the person who acted. This pins
    /// <c>MgmtReviewsService.CreateReviewAsync</c>, the enforced path; the route the desktop client uses today,
    /// <c>POST /MgmtReviews</c>, sets the reviewer to the caller in the controller (<c>MgmtReviewsControllerTest</c>) but
    /// reaches the legacy <c>Create</c>, without segregation of duties (S53 §11, defect 2).
    /// </summary>
    [Fact]
    public async Task TestPR5_TheReviewerIsTheCallerNeverThePayload()
    {
        await AiComponentRiskAsync();
        SeedUnscoped(ctx =>
        {
            // The read-back includes the review's type and next step, required navigations an unseeded row would hide.
            ctx.Reviews.Add(new Review { Value = 1, Name = "Accept the risk" });
            ctx.NextSteps.Add(new NextStep { Value = 2, Name = "Accept until next review" });
        });

        var review = await GetService<IMgmtReviewsService>().CreateReviewAsync(new MgmtReview
        {
            RiskId = 1, Review = 1, NextStep = 2, Reviewer = 0, Comments = "Re-assessed with the admissions office.",
            NextReview = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
        }, MemberA);

        Assert.Equal(MemberA, review.Reviewer);
        Assert.Equal(MemberA, Read(ctx => ctx.MgmtReviews.Single(r => r.Id == review.Id).Reviewer));
    }

    /// <summary>
    /// PR6 — the model is governed, never an actor: its owner is a person, never a non-user nor the third line; it holds no
    /// user, so it can be named nowhere a person is required; and deriving flag 11 on its risk recorded no decision.
    /// </summary>
    [Fact]
    public async Task TestPR6_TheModelIsGovernedNeverAnActor()
    {
        var users = Read(ctx => ctx.Users.Count());
        var model = await AiComponentRiskAsync();

        foreach (var (owner, expected) in new (int Owner, Type Exception)[]
                 {
                     (0, typeof(InvalidParameterException)), (NoSuchUser, typeof(DataNotFoundException)),
                     (Auditor, typeof(RuleBrokenException))
                 })
        {
            var request = new AiModelRequest
            {
                Name = "Second model", Purpose = "Summarises incidents.", Kind = AiModelKind.Generative,
                Source = AiModelSource.InHouse, Version = "1", OwnerId = owner, MaxEvaluationAgeDays = 30
            };
            Assert.IsType(expected, await Record.ExceptionAsync(() => AiModels.CreateAsync(request, Cro)));
        }

        Assert.Equal(users, Read(ctx => ctx.Users.Count()));
        Assert.Empty(Read(ctx => ctx.RiskDecisions.ToList()));
        Assert.Equal(Owner, (await AiModels.GetModelAsync(model)).OwnerId);
    }
}
