using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Governance;
using Model.RiskFlags;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 §8) — the risk committee (T197) against the real model, scope filters, audit interceptor and the Track 8
/// acceptance gates on the EF in-memory provider: constituting C1–C4, submitting D1–D4, voting V1–V7 and withdrawing W1–W2.
///
/// The committee is a collegiate approver <em>beside</em> the individual authorizing manager: V1 shows the k-th approval
/// creating the acceptance with no member holding a severity band, V2/V3 that the appetite, Gate A and the live-acceptance
/// rule still hold when it is decided, and the individual path is untouched (its Track 8 tests run unchanged).
/// </summary>
[TestSubject(typeof(RiskCommitteesService))]
public class RiskCommitteesServiceInMemoryTest : DecisionCycleTestBase
{
    private IRiskCommitteesService Committees => GetService<IRiskCommitteesService>();
    private IRiskFlagsService Flags => GetService<IRiskFlagsService>();

    private static RiskCommitteeRequest Committee(int required = 2, int? entityId = null) => new()
    {
        Name = "IT Risk Committee", Mandate = "Board resolution 2026/03.", RequiredApprovals = required, EntityId = entityId
    };

    private static RiskCommitteeDecisionRequest Acceptance(int riskId = 1) => new()
    {
        RiskId = riskId, Kind = RiskCommitteeDecisionKind.Accept, Name = "Kiosk exception",
        BusinessJustification = "Isolated network, insured, decommissioning in Q1.", ExpiresAt = DateTime.UtcNow.AddDays(180),
        MinutesReference = "Minutes 2026-09, item 4"
    };

    /// <summary>A committee of A, B and C requiring <paramref name="required"/> approvals.</summary>
    private async Task<int> ThreeMemberCommittee(int required = 2, int? entityId = null)
    {
        var id = (await Committees.CreateCommitteeAsync(Committee(required, entityId), Cro)).Id;
        foreach (var member in new[] { MemberA, MemberB, MemberC }) await Committees.AddMemberAsync(id, member, Cro);
        return id;
    }

    private async Task<int> OpenAcceptance(int committeeId, int riskId = 1) =>
        (await Committees.OpenDecisionAsync(committeeId, Acceptance(riskId), Cro)).Id;

    private Task<RiskCommitteeDecisionDto> Vote(int decisionId, int member,
        RiskCommitteeVoteChoice choice = RiskCommitteeVoteChoice.Approve) =>
        Committees.VoteAsync(decisionId, new RiskCommitteeVoteRequest { Choice = choice, Comment = "Considered." }, member);

    private void Appetite(double ceiling, double dual) =>
        SeedUnscoped(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = 1, EntityId = null, MaxAcceptableResidual = ceiling, DualApprovalThreshold = dual, CreatedAt = DateTime.UtcNow
        }));

    private System.Collections.Generic.List<RiskAcceptance> AcceptancesOf(int riskId) =>
        Read(ctx => ctx.RiskAcceptances.Where(a => a.RiskId == riskId).OrderBy(a => a.Id).ToList());

    // --- C1–C4: constituting ------------------------------------------------------------------------------------

    /// <summary>C1 — a committee is created with its required approvals, audited with who constituted it.</summary>
    [Fact]
    public async Task TestC1_ACommitteeIsConstituted()
    {
        var dto = await Committees.CreateCommitteeAsync(Committee(3), Cro);

        Assert.Equal(("IT Risk Committee", 3, (int?)null), (dto.Name, dto.RequiredApprovals, dto.EntityId));
        Assert.Contains(Audit(nameof(RiskCommittee)), a => a.UserId == Cro && a.EntityId == dto.Id);
    }

    /// <summary>C2 — every invalid committee is refused naming the field; one approval is not collegiate.</summary>
    [Theory]
    [InlineData("no-name", "Name")]
    [InlineData("long-name", "Name")]
    [InlineData("long-mandate", "Mandate")]
    [InlineData("one-approval", "RequiredApprovals")]
    [InlineData("too-many-approvals", "RequiredApprovals")]
    [InlineData("no-approvals", "RequiredApprovals")]
    [InlineData("zero-entity", "EntityId")]
    public async Task TestC2_AnInvalidCommitteeIsRefused(string scenario, string field)
    {
        var request = Committee();
        switch (scenario)
        {
            case "no-name": request.Name = ""; break;
            case "long-name": request.Name = new string('x', 201); break;
            case "long-mandate": request.Mandate = new string('x', 2001); break;
            case "one-approval": request.RequiredApprovals = 1; break;
            case "too-many-approvals": request.RequiredApprovals = 51; break;
            case "no-approvals": request.RequiredApprovals = null; break;
            default: request.EntityId = 0; break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Committees.CreateCommitteeAsync(request, Cro));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.RiskCommittees.ToList()));
    }

    /// <summary>
    /// C3 — members: added once however many times; a disabled or missing account refused; the third line never sits as
    /// a voter, administrator or not; removing a non-member is not found; a retired committee takes no member.
    /// </summary>
    [Fact]
    public async Task TestC3_Members()
    {
        var id = (await Committees.CreateCommitteeAsync(Committee(), Cro)).Id;
        SeedUnscoped(ctx => ctx.Users.Add(NewUser(30, "former") .WithEnabled(false)));

        await Committees.AddMemberAsync(id, MemberA, Cro);
        var twice = await Committees.AddMemberAsync(id, MemberA, Cro);
        Assert.Equal(new[] { MemberA }, twice.Members.Select(m => m.UserId));

        await Assert.ThrowsAsync<InvalidParameterException>(() => Committees.AddMemberAsync(id, 30, Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Committees.AddMemberAsync(id, 999, Cro));
        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Committees.AddMemberAsync(id, Auditor, Cro))).RuleName);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Committees.RemoveMemberAsync(id, MemberB, Cro));

        await Committees.RetireCommitteeAsync(id, Cro);
        Assert.Equal(RiskCommitteesService.RetiredRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Committees.AddMemberAsync(id, MemberB, Cro))).RuleName);
    }

    /// <summary>C4 — scope: a unit's caller sees the organization's committee and their own, and cannot constitute the organization's.</summary>
    [Fact]
    public async Task TestC4_CommitteesFollowTheScope()
    {
        await Committees.CreateCommitteeAsync(Committee(), Cro);
        await Committees.CreateCommitteeAsync(Committee(entityId: UnitA), Cro);
        await Committees.CreateCommitteeAsync(Committee(entityId: UnitB), Cro);

        ScopeTo(UnitA);
        var visible = await Committees.GetCommitteesAsync(includeRetired: false);

        Assert.Equal(new int?[] { null, UnitA }, visible.Select(c => c.EntityId).OrderBy(e => e ?? 0));

        // A caller of several units has no single unit to file an organization-wide record in: the write guard refuses it.
        ScopeTo(UnitA, Process);
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Committees.CreateCommitteeAsync(Committee(), Cro));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            Committees.CreateCommitteeAsync(Committee(entityId: UnitB), Cro));
    }

    // --- D1–D4: submitting --------------------------------------------------------------------------------------

    /// <summary>D1 — a decision opens with the committee's required approvals and is announced.</summary>
    [Fact]
    public async Task TestD1_ADecisionIsSubmitted()
    {
        ReviewedRisk(1);
        var committee = await ThreeMemberCommittee();
        var publisher = Substitute.For<INotificationEventPublisher>();
        var svc = new RiskCommitteesService(GetService<Serilog.ILogger>(), GetService<IDalService>(),
            GetService<IRiskWorkflowService>(), GetService<IRiskAcceptancesService>(), publisher);

        var dto = await svc.OpenDecisionAsync(committee, Acceptance(), Author);

        Assert.Equal((RiskCommitteeDecisionStatus.Open, 2, 3, (int?)Author),
            (dto.Status, dto.RequiredApprovals, dto.EligibleVoters, dto.OpenedById));
        await publisher.Received(1).RiskCommitteeDecisionOpenedAsync(Arg.Any<Risk>(), Arg.Any<double?>(),
            Arg.Any<RiskCommittee>(), Arg.Any<RiskCommitteeDecision>());
    }

    /// <summary>D2 — every invalid submission is refused naming the field.</summary>
    [Theory]
    [InlineData("no-risk", "RiskId")]
    [InlineData("no-kind", "Kind")]
    [InlineData("renew-without-acceptance", "RenewsAcceptanceId")]
    [InlineData("accept-with-acceptance", "RenewsAcceptanceId")]
    [InlineData("no-justification", "BusinessJustification")]
    [InlineData("no-expiry", "ExpiresAt")]
    [InlineData("past-expiry", "ExpiresAt")]
    [InlineData("long-minutes", "MinutesReference")]
    public async Task TestD2_AnInvalidSubmissionIsRefused(string scenario, string field)
    {
        ReviewedRisk(1);
        var committee = await ThreeMemberCommittee();
        var request = Acceptance();
        switch (scenario)
        {
            case "no-risk": request.RiskId = null; break;
            case "no-kind": request.Kind = null; break;
            case "renew-without-acceptance": request.Kind = RiskCommitteeDecisionKind.Renew; break;
            case "accept-with-acceptance": request.RenewsAcceptanceId = 5; break;
            case "no-justification": request.BusinessJustification = " "; break;
            case "no-expiry": request.ExpiresAt = null; break;
            case "past-expiry": request.ExpiresAt = DateTime.UtcNow.AddDays(-1); break;
            default: request.MinutesReference = new string('x', 501); break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Committees.OpenDecisionAsync(committee, request, Cro));

        Assert.Equal(field, ex.ParameterName);
    }

    /// <summary>
    /// D3 — refused before any vote: a retired committee, another entity's committee, a second open decision on the risk,
    /// a live acceptance, Gate A, the third line submitting, and a committee whose eligible members cannot reach the
    /// required approvals once the risk's own people are recused.
    /// </summary>
    [Fact]
    public async Task TestD3_WhatCannotBeSubmitted()
    {
        ReviewedRisk(1);
        ReviewedRisk(2);
        ReviewedRisk(3);
        ReviewedRisk(4, unit: UnitB);
        var committee = await ThreeMemberCommittee();
        var unitCommittee = await ThreeMemberCommittee(entityId: UnitA);

        Assert.Equal(RiskCommitteesService.EntityMismatchRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.OpenDecisionAsync(unitCommittee, Acceptance(4), Cro))).RuleName);

        await OpenAcceptance(committee);
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Committees.OpenDecisionAsync(committee, Acceptance(), Cro));

        SeedUnscoped(ctx => ctx.RiskAcceptances.Add(new RiskAcceptance
        {
            Name = "Live", RiskId = 2, AuthorizingManagerId = Cro, BusinessJustification = "x", StartDate = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30), Status = RiskAcceptanceStatus.Active, CreatedAt = DateTime.UtcNow
        }));
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Committees.OpenDecisionAsync(committee, Acceptance(2), Cro));

        await Flags.DeclareAsync(3, RiskFlagCode.HumanSafety,
            new RiskFlagDeclarationRequest { Reason = "Drives the door lock." }, Author);
        Assert.Equal("gate_a_non_discretionary", (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.OpenDecisionAsync(committee, Acceptance(3), Cro))).RuleName);

        ReviewedRisk(5);
        Assert.Equal(ThirdLineAssurance.CannotApproveRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.OpenDecisionAsync(committee, Acceptance(5), Auditor))).RuleName);

        var small = (await Committees.CreateCommitteeAsync(Committee(2), Cro)).Id;
        await Committees.AddMemberAsync(small, MemberA, Cro);
        await Committees.AddMemberAsync(small, Owner, Cro); // recused on risk 5: they own it
        Assert.Equal(RiskCommitteesService.QuorumUnreachableRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.OpenDecisionAsync(small, Acceptance(5), Cro))).RuleName);

        await Committees.RetireCommitteeAsync(committee, Cro);
        Assert.Equal(RiskCommitteesService.RetiredRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.OpenDecisionAsync(committee, Acceptance(5), Cro))).RuleName);
    }

    /// <summary>D4 — a decision on a risk outside the caller's scope is not found, read or written.</summary>
    [Fact]
    public async Task TestD4_ADecisionOutsideTheScopeIsNotFound()
    {
        ReviewedRisk(1, unit: UnitB);
        var committee = await ThreeMemberCommittee();
        var decision = await OpenAcceptance(committee);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Committees.GetDecisionAsync(decision));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Committees.OpenDecisionAsync(committee, Acceptance(), Cro));
        Assert.Empty(await Committees.GetDecisionsAsync(null, null, openOnly: false));
    }

    // --- V1–V7: voting ------------------------------------------------------------------------------------------

    /// <summary>
    /// V1 — two of three: the first approval leaves it open, the second approves it and creates, in the same write, the
    /// acceptance — authorized by the member whose vote carried it, requested by whoever submitted it, on the single
    /// review timeline naming the committee. No member holds a severity band: the collegiate decision is the authority.
    /// </summary>
    [Fact]
    public async Task TestV1_TheKthApprovalCreatesTheAcceptance()
    {
        ReviewedRisk(1, score: 9f);
        var committee = await ThreeMemberCommittee();
        var decision = (await Committees.OpenDecisionAsync(committee, Acceptance(), Author)).Id;

        var first = await Vote(decision, MemberA);
        Assert.Equal((RiskCommitteeDecisionStatus.Open, 1), (first.Status, first.Approvals));
        Assert.Empty(AcceptancesOf(1));

        var second = await Vote(decision, MemberB);

        Assert.Equal(RiskCommitteeDecisionStatus.Approved, second.Status);
        Assert.NotNull(second.ClosedAt);
        var acceptance = Assert.Single(AcceptancesOf(1));
        Assert.Equal((second.AcceptanceId, MemberB, (int?)Author, RiskAcceptanceStatus.Active, "Kiosk exception"),
            ((int?)acceptance.Id, acceptance.AuthorizingManagerId, acceptance.RequestedById, acceptance.Status,
                acceptance.Name));
        Assert.Equal(Acceptance().ExpiresAt!.Value.Date, acceptance.ExpiresAt.Date);

        var review = Read(ctx => ctx.MgmtReviews.Where(r => r.RiskId == 1).OrderByDescending(r => r.Id).First());
        Assert.Equal(MemberB, review.Reviewer);
        Assert.Contains("risk committee 'IT Risk Committee'", review.Comments);
        Assert.Contains("2 of 2 required approvals", review.Comments);

        Assert.Equal(RiskCommitteesService.DecisionClosedRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Vote(decision, MemberC))).RuleName);
        Assert.Equal(2, Audit(nameof(RiskCommitteeVote)).Count(a => a.Action == AuditLogAction.Create));
    }

    /// <summary>V2 — a reject and an abstention leave one voter for two approvals: rejected at once, no acceptance.</summary>
    [Fact]
    public async Task TestV2_UnreachableIsRejected()
    {
        ReviewedRisk(1);
        var decision = await OpenAcceptance(await ThreeMemberCommittee());

        await Vote(decision, MemberA, RiskCommitteeVoteChoice.Reject);
        var dto = await Vote(decision, MemberB, RiskCommitteeVoteChoice.Abstain);

        Assert.Equal((RiskCommitteeDecisionStatus.Rejected, 1, 1), (dto.Status, dto.Rejections, dto.Abstentions));
        Assert.Empty(AcceptancesOf(1));
    }

    /// <summary>
    /// V3 — the gates are checked when the decision is reached, not only when it was opened: above the appetite's ceiling
    /// the deciding vote is refused and nothing is recorded — neither the vote nor an acceptance.
    /// </summary>
    [Fact]
    public async Task TestV3_TheAppetiteCeilingRefusesTheDecidingVote()
    {
        ReviewedRisk(1, score: 9f);
        var decision = await OpenAcceptance(await ThreeMemberCommittee());
        await Vote(decision, MemberA);
        Appetite(ceiling: 5, dual: 5);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Vote(decision, MemberB));

        Assert.Equal("risk_appetite_ceiling", ex.RuleName);
        Assert.Empty(AcceptancesOf(1));
        var dto = await Committees.GetDecisionAsync(decision);
        Assert.Equal((RiskCommitteeDecisionStatus.Open, 1), (dto.Status, dto.Votes.Count));
    }

    /// <summary>V4 — who may vote: a member only (403 otherwise), once (409), not on their own risk (recused), not the third line.</summary>
    [Fact]
    public async Task TestV4_WhoMayVote()
    {
        ReviewedRisk(1);
        var committee = await ThreeMemberCommittee();
        await Committees.AddMemberAsync(committee, Owner, Cro);
        var decision = await OpenAcceptance(committee);

        await Assert.ThrowsAsync<PermissionInvalidException>(() => Vote(decision, Cro));

        await Vote(decision, MemberA, RiskCommitteeVoteChoice.Abstain);
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Vote(decision, MemberA));

        Assert.Equal("segregation_of_duties",
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Vote(decision, Owner))).RuleName);

        // A member who became the third line after joining is excluded and refused.
        SeedUnscoped(ctx => ctx.Users.Single(u => u.Value == MemberC).RoleId = ThirdLineRole);
        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Vote(decision, MemberC))).RuleName);

        var dto = await Committees.GetDecisionAsync(decision);
        Assert.Equal(2, dto.EligibleVoters); // A (voted) and B; the owner and the third line are not counted
        Assert.Single(dto.Votes);
        Assert.Equal("Choice", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Committees.VoteAsync(decision, new RiskCommitteeVoteRequest(), MemberB))).ParameterName);
    }

    /// <summary>
    /// V5 — above the dual-approval threshold the collegiate decision carries its own second signature: the review is
    /// counter-signed by another approving member, so the risk may close on it.
    /// </summary>
    [Fact]
    public async Task TestV5_DualApprovalIsSatisfiedCollegially()
    {
        ReviewedRisk(1, score: 9f);
        Appetite(ceiling: 10, dual: 5);
        var decision = await OpenAcceptance(await ThreeMemberCommittee());

        await Vote(decision, MemberA);
        await Vote(decision, MemberB);

        var review = Read(ctx => ctx.MgmtReviews.Where(r => r.RiskId == 1).OrderByDescending(r => r.Id).First());
        Assert.Equal((true, (int?)MemberA, MemberB), (review.RequiresCountersignature, review.SecondReviewerId, review.Reviewer));
        await GetService<IRiskWorkflowService>().EnsureTransitionAllowedAsync(1, "Mgmt Reviewed", "Closed");
    }

    /// <summary>V6 — a renewal: the previous acceptance becomes Renewed and the new one is chained to it.</summary>
    [Fact]
    public async Task TestV6_ARenewal()
    {
        ReviewedRisk(1);
        var previous = await GetService<IRiskAcceptancesService>().CreateAsync(1, new RiskAcceptanceRequest
        {
            BusinessJustification = "First year.", ExpiresAt = DateTime.UtcNow.AddDays(10)
        }, Cro);
        var committee = await ThreeMemberCommittee();
        var request = Acceptance();
        request.Kind = RiskCommitteeDecisionKind.Renew;
        request.RenewsAcceptanceId = previous.Id;
        var decision = (await Committees.OpenDecisionAsync(committee, request, Cro)).Id;

        await Vote(decision, MemberA);
        await Vote(decision, MemberC);

        var acceptances = AcceptancesOf(1);
        Assert.Equal(new[] { RiskAcceptanceStatus.Renewed, RiskAcceptanceStatus.Active }, acceptances.Select(a => a.Status));
        Assert.Equal(previous.Id, acceptances[1].RenewedFromId);
    }

    /// <summary>V7 — a member who leaves after voting: the vote stays cast and counts.</summary>
    [Fact]
    public async Task TestV7_AVoteCastStaysCast()
    {
        ReviewedRisk(1);
        var committee = await ThreeMemberCommittee();
        var decision = await OpenAcceptance(committee);

        await Vote(decision, MemberA);
        await Committees.RemoveMemberAsync(committee, MemberA, Cro);
        var dto = await Vote(decision, MemberB);

        Assert.Equal(RiskCommitteeDecisionStatus.Approved, dto.Status);
        Assert.Equal(new int?[] { MemberA, MemberB }, dto.Votes.Select(v => v.VoterId));
    }

    /// <summary>
    /// V8 (S50 R3, regression) — two members cast the deciding approval at the same moment: the second write finds the
    /// decision changed (its concurrency token) and is refused with 409; the decision carries the first one's acceptance.
    /// On MariaDB the refused <c>SaveChanges</c> is one transaction and rolls back whole — its vote and its staged
    /// acceptance with it. The EF in-memory provider has no transactions and keeps the rows it wrote before the conflicting
    /// update, so this test asserts the refusal and the decision, not the number of acceptance rows (S50 §8).
    /// </summary>
    [Fact]
    public async Task TestV8_TwoDecidingVotesAtOnce_TheSecondIsRefused()
    {
        ReviewedRisk(1);
        var decision = await OpenAcceptance(await ThreeMemberCommittee());
        await Vote(decision, MemberA);

        RiskCommitteesService New() => new(GetService<Serilog.ILogger>(), GetService<IDalService>(),
            GetService<IRiskWorkflowService>(), GetService<IRiskAcceptancesService>(),
            Substitute.For<INotificationEventPublisher>());

        var rival = New();
        var late = New();
        late.BeforeVoteSaved = () => rival.VoteAsync(decision,
            new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Approve }, MemberC);

        var ex = await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            late.VoteAsync(decision, new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Approve }, MemberB));

        Assert.Contains("not recorded", ex.Message);
        var row = Read(ctx => ctx.RiskCommitteeDecisions.Single(d => d.Id == decision));
        Assert.Equal(RiskCommitteeDecisionStatus.Approved, row.Status);
        var carried = Read(ctx => ctx.RiskAcceptances.Single(a => a.Id == row.AcceptanceId));
        Assert.Equal(MemberC, carried.AuthorizingManagerId);
        Assert.Equal(2, row.Version);
    }

    /// <summary>
    /// V9 (S50 §4.6, regression) — a renewal of an acceptance already renewed, or while another acceptance of the risk is
    /// live, would leave two in force: refused when submitted, and again when decided.
    /// </summary>
    [Fact]
    public async Task TestV9_ARenewalNeverLeavesTwoAcceptancesInForce()
    {
        ReviewedRisk(1);
        var acceptances = GetService<IRiskAcceptancesService>();
        var first = await acceptances.CreateAsync(1, new RiskAcceptanceRequest
            { BusinessJustification = "First year.", ExpiresAt = DateTime.UtcNow.AddDays(10) }, Cro);
        var second = await acceptances.RenewAsync(first.Id, new RiskAcceptanceRequest
            { BusinessJustification = "Second year.", ExpiresAt = DateTime.UtcNow.AddDays(400) }, Cro);
        var committee = await ThreeMemberCommittee();

        var renewTheRenewed = Acceptance();
        renewTheRenewed.Kind = RiskCommitteeDecisionKind.Renew;
        renewTheRenewed.RenewsAcceptanceId = first.Id;
        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            Committees.OpenDecisionAsync(committee, renewTheRenewed, Cro));

        // A live acceptance plus an older, expired one: renewing the expired one would put two in force.
        SeedUnscoped(ctx =>
        {
            var old = ctx.RiskAcceptances.Single(a => a.Id == first.Id);
            old.Status = RiskAcceptanceStatus.Expired;
        });
        var renewTheExpired = Acceptance();
        renewTheExpired.Kind = RiskCommitteeDecisionKind.Renew;
        renewTheExpired.RenewsAcceptanceId = first.Id;
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            Committees.OpenDecisionAsync(committee, renewTheExpired, Cro));

        Assert.Equal(new[] { RiskAcceptanceStatus.Expired, RiskAcceptanceStatus.Active },
            AcceptancesOf(1).Select(a => a.Status));
        Assert.Equal(second.Id, AcceptancesOf(1).Single(a => a.Status == RiskAcceptanceStatus.Active).Id);
    }

    // --- W1–W2: withdrawing -------------------------------------------------------------------------------------

    /// <summary>W1 — withdrawn by whoever submitted it or an administrator, with a reason; nobody else; never twice.</summary>
    [Fact]
    public async Task TestW1_Withdrawing()
    {
        ReviewedRisk(1);
        var decision = (await Committees.OpenDecisionAsync(await ThreeMemberCommittee(), Acceptance(), Author)).Id;

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Committees.WithdrawAsync(decision, new RiskCommitteeWithdrawRequest(), Author));
        await Assert.ThrowsAsync<PermissionInvalidException>(() =>
            Committees.WithdrawAsync(decision, new RiskCommitteeWithdrawRequest { Reason = "Mine now." }, MemberA));

        var dto = await Committees.WithdrawAsync(decision, new RiskCommitteeWithdrawRequest { Reason = "Treating instead." }, Author);
        Assert.Equal((RiskCommitteeDecisionStatus.Withdrawn, "Treating instead."), (dto.Status, dto.WithdrawalReason));

        Assert.Equal(RiskCommitteesService.DecisionClosedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Committees.WithdrawAsync(decision, new RiskCommitteeWithdrawRequest { Reason = "Again." }, Cro))).RuleName);
    }

    /// <summary>W2 — retiring a committee withdraws what it was deciding, with the reason recorded.</summary>
    [Fact]
    public async Task TestW2_RetiringWithdrawsOpenDecisions()
    {
        ReviewedRisk(1);
        var committee = await ThreeMemberCommittee();
        var decision = await OpenAcceptance(committee);

        await Committees.RetireCommitteeAsync(committee, Cro);

        var dto = await Committees.GetDecisionAsync(decision);
        Assert.Equal((RiskCommitteeDecisionStatus.Withdrawn, "The committee was retired."), (dto.Status, dto.WithdrawalReason));
    }
}

internal static class UserTestExtensions
{
    public static User WithEnabled(this User user, bool enabled)
    {
        user.Enabled = enabled;
        return user;
    }
}
