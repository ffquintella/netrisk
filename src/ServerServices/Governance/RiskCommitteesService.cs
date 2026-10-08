using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.RiskFlags;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.DecisionCycle;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.9 (S50 §4.5–§4.6) — the risk committee: the methodology's Phase 0 "comitê de risco constituído", a collegiate
/// approver beside the individual authorizing manager of Track 8.
///
/// The individual path is untouched: <c>POST /Risks/{id}/Acceptances</c> still takes one manager holding the severity
/// band. The committee path submits the same acceptance to a committee, whose members vote; the vote that reaches the
/// committee's required approvals (<see cref="CommitteeTally"/>, pure) creates the acceptance in the same write, through
/// <see cref="IRiskAcceptancesService.StageCommitteeAcceptanceAsync"/> — so Gate A, the live-acceptance rule, the ceiling,
/// the tail and the indicators are the ones an individual acceptance passes, checked again at the moment it is decided.
///
/// The people: a member who submitted, owns or manages the risk is recused (S50 D9) — always, whatever the
/// installation's segregation switch says, because a committee voting on a member's own risk is a conflict of interest —
/// and the third line never sits as a voter (S50 §4.7).
/// </summary>
public class RiskCommitteesService(
    ILogger logger,
    IDalService dalService,
    IRiskWorkflowService workflow,
    IRiskAcceptancesService acceptances,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IRiskCommitteesService
{
    public const string RetiredRule = "committee_retired";
    public const string EntityMismatchRule = "committee_entity_mismatch";
    public const string QuorumUnreachableRule = "committee_quorum_unreachable";
    public const string DecisionClosedRule = "committee_decision_closed";
    public const string MembershipPermission = "risk_committee_member";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Runs after a vote is staged and before it is saved. A seam for the concurrency test (S50 §8 V8), which casts a
    /// competing vote here; null in production.
    /// </summary>
    public Func<Task>? BeforeVoteSaved { get; set; }

    // --- committees --------------------------------------------------------------------------------------------

    public async Task<List<RiskCommitteeDto>> GetCommitteesAsync(bool includeRetired)
    {
        await using var db = DalService.GetContext();

        var committees = await db.RiskCommittees.AsNoTracking()
            .Where(c => includeRetired || c.RetiredAt == null)
            .Include(c => c.Members).ThenInclude(m => m.User)
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .ToListAsync();

        return committees.Select(ToDto).ToList();
    }

    public async Task<RiskCommitteeDto> GetCommitteeAsync(int committeeId)
    {
        await using var db = DalService.GetContext();
        return ToDto(await RequireCommitteeAsync(db, committeeId, tracked: false));
    }

    public async Task<RiskCommitteeDto> CreateCommitteeAsync(RiskCommitteeRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = ValidateCommittee(request);
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireEntityAsync(db, valid.EntityId);

        var committee = new RiskCommittee
        {
            Name = valid.Name, Mandate = valid.Mandate, EntityId = valid.EntityId,
            RequiredApprovals = valid.RequiredApprovals, CreatedAt = now, UpdatedById = actingUserId
        };
        db.RiskCommittees.Add(committee);

        // A scoped caller creating the organization's committee, or another entity's, is refused by the write guard
        // (403), because RiskCommittee is IEntityScoped.
        await db.SaveChangesAsync();

        Logger.Information("Risk committee {Committee} '{Name}' created by user {User}: {Required} approvals required",
            committee.Id, committee.Name, actingUserId, committee.RequiredApprovals);

        return await ReadCommitteeAsync(committee.Id);
    }

    public async Task<RiskCommitteeDto> UpdateCommitteeAsync(int committeeId, RiskCommitteeRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = ValidateCommittee(request);

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var committee = await RequireCommitteeAsync(db, committeeId, tracked: true);
            RefuseRetired(committee);
            await RequireEntityAsync(db, valid.EntityId);

            if (valid.RequiredApprovals < committee.RequiredApprovals)
                Logger.Warning("Risk committee {Committee}: required approvals LOWERED by user {User} from {Old} to {New}",
                    committeeId, actingUserId, committee.RequiredApprovals, valid.RequiredApprovals);

            committee.Name = valid.Name;
            committee.Mandate = valid.Mandate;
            committee.EntityId = valid.EntityId;
            committee.RequiredApprovals = valid.RequiredApprovals;
            committee.UpdatedAt = Clock();
            committee.UpdatedById = actingUserId;

            await db.SaveChangesAsync();
        }

        return await ReadCommitteeAsync(committeeId);
    }

    public async Task<RiskCommitteeDto> RetireCommitteeAsync(int committeeId, int actingUserId)
    {
        var now = Clock();

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var committee = await RequireCommitteeAsync(db, committeeId, tracked: true);
            if (committee.RetiredAt is null)
            {
                committee.RetiredAt = now;
                committee.UpdatedAt = now;
                committee.UpdatedById = actingUserId;

                // A retired committee decides nothing, so what it was deciding is withdrawn, with the reason recorded.
                var open = await db.RiskCommitteeDecisions
                    .Where(d => d.CommitteeId == committeeId && d.Status == RiskCommitteeDecisionStatus.Open)
                    .ToListAsync();
                foreach (var decision in open)
                {
                    decision.Status = RiskCommitteeDecisionStatus.Withdrawn;
                    decision.ClosedAt = now;
                    decision.WithdrawalReason = "The committee was retired.";
                    decision.UpdatedAt = now;
                    decision.Version++;
                }

                await db.SaveChangesAsync();

                Logger.Warning("Risk committee {Committee} RETIRED by user {User}; {Count} open decision(s) withdrawn",
                    committeeId, actingUserId, open.Count);
            }
        }

        return await ReadCommitteeAsync(committeeId);
    }

    public async Task<RiskCommitteeDto> AddMemberAsync(int committeeId, int userId, int actingUserId)
    {
        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var committee = await RequireCommitteeAsync(db, committeeId, tracked: false);
            RefuseRetired(committee);

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Value == userId)
                       ?? throw new DataNotFoundException("user", userId.ToString(Invariant));

            if (user.Enabled != true)
                throw new InvalidParameterException(nameof(userId),
                    "A disabled account cannot sit on a committee — its vote would never come.");

            await ThirdLineGuard.EnsureNotThirdLineAsync(db, userId, "sit on a risk committee as a voting member");

            if (!await db.RiskCommitteeMembers.AnyAsync(m => m.CommitteeId == committeeId && m.UserId == userId))
            {
                db.RiskCommitteeMembers.Add(new RiskCommitteeMember
                    { CommitteeId = committeeId, UserId = userId, CreatedAt = Clock(), CreatedById = actingUserId });
                await db.SaveChangesAsync();

                Logger.Information("User {Member} added to risk committee {Committee} by user {User}", userId,
                    committeeId, actingUserId);
            }
        }

        return await ReadCommitteeAsync(committeeId);
    }

    public async Task RemoveMemberAsync(int committeeId, int userId, int actingUserId)
    {
        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        await RequireCommitteeAsync(db, committeeId, tracked: false);

        var member = await db.RiskCommitteeMembers.FirstOrDefaultAsync(m => m.CommitteeId == committeeId && m.UserId == userId)
                     ?? throw new DataNotFoundException("risk_committee_members", $"{committeeId}/{userId}");

        db.RiskCommitteeMembers.Remove(member);
        await db.SaveChangesAsync();

        // The votes the member already cast stay cast (S50 §4.6).
        Logger.Warning("User {Member} REMOVED from risk committee {Committee} by user {User}", userId, committeeId,
            actingUserId);
    }

    // --- decisions ---------------------------------------------------------------------------------------------

    public async Task<List<RiskCommitteeDecisionDto>> GetDecisionsAsync(int? committeeId, int? riskId, bool openOnly)
    {
        await using var db = DalService.GetContext();

        var decisions = await DecisionQuery(db)
            .Where(d => committeeId == null || d.CommitteeId == committeeId)
            .Where(d => riskId == null || d.RiskId == riskId)
            .Where(d => !openOnly || d.Status == RiskCommitteeDecisionStatus.Open)
            .OrderByDescending(d => d.OpenedAt).ThenByDescending(d => d.Id)
            .Take(DecisionCycleLimits.MaxListLimit)
            .ToListAsync();

        return await ToDtosAsync(db, decisions);
    }

    public async Task<RiskCommitteeDecisionDto> GetDecisionAsync(int decisionId)
    {
        await using var db = DalService.GetContext();

        var decision = await DecisionQuery(db).FirstOrDefaultAsync(d => d.Id == decisionId)
                       ?? throw new DataNotFoundException("risk_committee_decisions", decisionId.ToString(Invariant));

        return (await ToDtosAsync(db, [decision])).Single();
    }

    public async Task<RiskCommitteeDecisionDto> OpenDecisionAsync(int committeeId, RiskCommitteeDecisionRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = ValidateDecision(request);
        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var committee = await RequireCommitteeAsync(db, committeeId, tracked: false);
        RefuseRetired(committee);

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == valid.RiskId)
                   ?? throw new DataNotFoundException("risks", valid.RiskId.ToString(Invariant));

        if (committee.EntityId is { } entity && entity != risk.EntityId)
            throw new RuleBrokenException(
                "This committee decides the risks of another business entity. Submit it to the organization's committee " +
                "or to the risk's own entity's.", EntityMismatchRule);

        await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "submit a risk to a committee");

        if (await db.RiskCommitteeDecisions.AnyAsync(d =>
                d.RiskId == risk.Id && d.Status == RiskCommitteeDecisionStatus.Open))
            throw new DataAlreadyExistsException("local", "risk_committee_decisions", risk.Id.ToString(Invariant),
                "This risk already has a decision open before a committee. Withdraw it, or wait for it, rather than " +
                "putting the same question twice.");

        if (valid.Kind == RiskCommitteeDecisionKind.Renew)
        {
            var previous = await db.RiskAcceptances.AsNoTracking()
                               .FirstOrDefaultAsync(a => a.Id == valid.RenewsAcceptanceId && a.RiskId == risk.Id)
                           ?? throw new DataNotFoundException("risk_acceptances",
                               valid.RenewsAcceptanceId!.Value.ToString(Invariant));

            if (previous.Status is RiskAcceptanceStatus.Revoked or RiskAcceptanceStatus.Renewed)
                throw new InvalidStateTransitionException(previous.Status.ToString(),
                    RiskAcceptanceStatus.Renewed.ToString(),
                    previous.Status == RiskAcceptanceStatus.Revoked
                        ? "A revoked acceptance is not renewed, it is replaced: submit a new acceptance."
                        : "This acceptance was already renewed; renew the acceptance that replaced it.");

            if (await db.RiskAcceptances.AnyAsync(a => a.RiskId == risk.Id && a.Id != previous.Id &&
                                                     a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now))
                throw new DataAlreadyExistsException("local", "risk_acceptances", risk.Id.ToString(Invariant),
                    "Another acceptance of this risk is live: renewing this one would leave two in force.");

            await workflow.EnsureGateAAllowsAsync(risk.Id, GateAAction.RenewAcceptance);
        }
        else
        {
            if (await db.RiskAcceptances.AnyAsync(a =>
                    a.RiskId == risk.Id && a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt > now))
                throw new DataAlreadyExistsException("local", "risk_acceptances", risk.Id.ToString(Invariant),
                    "This risk already has a live acceptance. Submit a renewal instead.");

            await workflow.EnsureGateAAllowsAsync(risk.Id, GateAAction.Accept);
        }

        var eligible = await EligibleMembersAsync(db, committee.Members.Select(m => m.UserId), risk);
        if (!CommitteeTally.Reachable(committee.RequiredApprovals, eligible.Count))
            throw new RuleBrokenException(
                $"The committee requires {committee.RequiredApprovals} approvals and only {eligible.Count} of its members " +
                "may vote on this risk (its submitter, owner and manager are recused, the third line never votes). Add " +
                "members first.", QuorumUnreachableRule);

        var decision = new RiskCommitteeDecision
        {
            CommitteeId = committeeId, RiskId = risk.Id, Kind = valid.Kind, RenewsAcceptanceId = valid.RenewsAcceptanceId,
            Status = RiskCommitteeDecisionStatus.Open, RequiredApprovals = committee.RequiredApprovals, Name = valid.Name,
            BusinessJustification = valid.Justification, CompensatingControls = valid.CompensatingControls,
            ExpiresAt = valid.ExpiresAt, MinutesReference = valid.MinutesReference, OpenedById = actingUserId,
            OpenedAt = now, CreatedAt = now
        };
        db.RiskCommitteeDecisions.Add(decision);
        await db.SaveChangesAsync();

        Logger.Information("Risk {Risk} submitted to committee {Committee} by user {User} as decision {Decision} ({Kind})",
            risk.Id, committeeId, actingUserId, decision.Id, valid.Kind);

        var score = await db.RiskScorings.AsNoTracking().Where(s => s.Id == risk.Id)
            .Select(s => (double?)(s.ResidualRisk ?? s.CalculatedRisk)).FirstOrDefaultAsync();
        await notifications.RiskCommitteeDecisionOpenedAsync(risk, score, committee, decision);

        return await GetDecisionAsync(decision.Id);
    }

    public async Task<RiskCommitteeDecisionDto> VoteAsync(int decisionId, RiskCommitteeVoteRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Choice is not { } choice || !Enum.IsDefined(choice))
            throw new InvalidParameterException(nameof(RiskCommitteeVoteRequest.Choice),
                "The vote is approve (1), reject (2) or abstain (3).");

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > DecisionCycleLimits.MaxVoteCommentLength })
            throw new InvalidParameterException(nameof(RiskCommitteeVoteRequest.Comment),
                $"At most {DecisionCycleLimits.MaxVoteCommentLength} characters.");

        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var decision = await db.RiskCommitteeDecisions
                           .Include(d => d.Votes)
                           .Include(d => d.Committee).ThenInclude(c => c.Members)
                           .FirstOrDefaultAsync(d => d.Id == decisionId)
                       ?? throw new DataNotFoundException("risk_committee_decisions", decisionId.ToString(Invariant));

        if (decision.Status != RiskCommitteeDecisionStatus.Open)
            throw new RuleBrokenException($"Decision {decisionId} is {decision.Status.ToString().ToLowerInvariant()}; " +
                                          "it takes no more votes.", DecisionClosedRule);

        RefuseRetired(decision.Committee);

        if (decision.Committee.Members.All(m => m.UserId != actingUserId))
            throw new PermissionInvalidException(MembershipPermission, actingUserId, "vote on a committee decision");

        await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "vote on a committee decision");

        var risk = await db.Risks.FirstAsync(r => r.Id == decision.RiskId);

        var conflicts = RiskWorkflowService.SegregationConflicts(risk, actingUserId);
        if (conflicts.Count > 0)
            throw new RuleBrokenException(
                $"You cannot vote on this risk because you {RiskWorkflowService.DescribeRelation(conflicts)}: a committee " +
                "member is recused from deciding their own risk.", "segregation_of_duties");

        if (decision.Votes.Any(v => v.VoterId == actingUserId))
            throw new DataAlreadyExistsException("local", "risk_committee_votes", $"{decisionId}/{actingUserId}",
                "You have already voted on this decision; a vote is final.");

        decision.Votes.Add(new RiskCommitteeVote
            { VoterId = actingUserId, Choice = choice, Comment = comment, CastAt = now, CreatedAt = now });

        var eligible = await EligibleMembersAsync(db, decision.Committee.Members.Select(m => m.UserId), risk);
        var voters = decision.Votes.Where(v => v.VoterId != null).Select(v => v.VoterId!.Value).ToHashSet();
        voters.UnionWith(eligible);

        var approvals = decision.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Approve);
        var outcome = CommitteeTally.Evaluate(decision.RequiredApprovals, voters.Count, approvals,
            decision.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Reject),
            decision.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Abstain));

        if (outcome == CommitteeTallyOutcome.Approved)
        {
            // The deciding vote creates the acceptance, in this write, after the gates (re-checked now: a Gate A
            // condition, a KRI breach or a new score since the decision opened refuses the vote, and nothing is recorded).
            var second = decision.Votes
                .Where(v => v.Choice == RiskCommitteeVoteChoice.Approve && v.VoterId != null && v.VoterId != actingUserId)
                .OrderBy(v => v.CastAt).Select(v => v.VoterId).FirstOrDefault();

            var acceptance = await acceptances.StageCommitteeAcceptanceAsync(db, new CommitteeAcceptance(
                decision.Id, decision.Committee.Name, decision.RiskId, decision.RenewsAcceptanceId, decision.Name,
                decision.BusinessJustification, decision.CompensatingControls, decision.ExpiresAt, actingUserId, second,
                decision.OpenedById, approvals, decision.RequiredApprovals));

            decision.Status = RiskCommitteeDecisionStatus.Approved;
            decision.Acceptance = acceptance;
        }
        else if (outcome == CommitteeTallyOutcome.Rejected)
        {
            decision.Status = RiskCommitteeDecisionStatus.Rejected;
        }

        if (decision.Status != RiskCommitteeDecisionStatus.Open) decision.ClosedAt = now;

        // Every vote changes the decision row, so two votes racing for it collide on its concurrency token: the second
        // write is refused whole — its vote and any acceptance it staged — and the voter reads the decision again (R3).
        decision.UpdatedAt = now;
        decision.Version++;

        if (BeforeVoteSaved is { } seam) await seam();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            Logger.Warning(ex, "Vote of user {User} on committee decision {Decision} lost a concurrent write", actingUserId,
                decisionId);
            throw new DataAlreadyExistsException("local", "risk_committee_decisions", decisionId.ToString(Invariant),
                "The decision changed while your vote was being recorded — another member voted, or it was withdrawn. " +
                "Read it again: your vote was not recorded.");
        }

        Logger.Information("User {User} voted {Choice} on committee decision {Decision}: {Outcome}", actingUserId, choice,
            decisionId, decision.Status);

        return await GetDecisionAsync(decisionId);
    }

    public async Task<RiskCommitteeDecisionDto> WithdrawAsync(int decisionId, RiskCommitteeWithdrawRequest request,
        int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidParameterException(nameof(RiskCommitteeWithdrawRequest.Reason), "Say why it is withdrawn.");
        var reason = request.Reason.Trim();
        if (reason.Length > DecisionCycleLimits.MaxWithdrawalReasonLength)
            throw new InvalidParameterException(nameof(RiskCommitteeWithdrawRequest.Reason),
                $"At most {DecisionCycleLimits.MaxWithdrawalReasonLength} characters.");

        await using (var db = DalService.GetContext())
        {
            db.UserId = actingUserId;

            var decision = await db.RiskCommitteeDecisions.FirstOrDefaultAsync(d => d.Id == decisionId)
                           ?? throw new DataNotFoundException("risk_committee_decisions", decisionId.ToString(Invariant));

            if (decision.Status != RiskCommitteeDecisionStatus.Open)
                throw new RuleBrokenException($"Decision {decisionId} is already closed.", DecisionClosedRule);

            var admin = await db.Users.AsNoTracking().AnyAsync(u => u.Value == actingUserId && u.Admin);
            if (decision.OpenedById != actingUserId && !admin)
                throw new PermissionInvalidException("risk_committee_submitter", actingUserId,
                    "withdraw a committee decision");

            await ThirdLineGuard.EnsureNotThirdLineAsync(db, actingUserId, "withdraw a committee decision");

            var now = Clock();
            decision.Status = RiskCommitteeDecisionStatus.Withdrawn;
            decision.WithdrawalReason = reason;
            decision.ClosedAt = now;
            decision.UpdatedAt = now;
            decision.Version++;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new DataAlreadyExistsException("local", "risk_committee_decisions", decisionId.ToString(Invariant),
                    "The decision changed while it was being withdrawn. Read it again.");
            }

            Logger.Information("Committee decision {Decision} withdrawn by user {User}", decisionId, actingUserId);
        }

        return await GetDecisionAsync(decisionId);
    }

    // --- internals ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The members who may vote on <paramref name="risk"/>: not its submitter, owner or manager, and not the third line —
    /// a member whose role became third-line after joining is excluded here as well as refused when voting.
    /// </summary>
    private static async Task<HashSet<int>> EligibleMembersAsync(AuditableContext db, IEnumerable<int> members, Risk risk)
    {
        var distinct = members.Distinct().ToList();
        var thirdLine = await ThirdLineGuard.ThirdLineAmongAsync(db, distinct);

        return distinct.Where(m => RiskWorkflowService.SegregationConflicts(risk, m).Count == 0 && !thirdLine.Contains(m))
            .ToHashSet();
    }

    /// <summary>The same rule as <see cref="EligibleMembersAsync"/>, with the third line resolved once for a whole list.</summary>
    private static HashSet<int> EligibleMembers(IEnumerable<int> members, Risk risk, IReadOnlySet<int> thirdLine) =>
        members.Distinct()
            .Where(m => RiskWorkflowService.SegregationConflicts(risk, m).Count == 0 && !thirdLine.Contains(m))
            .ToHashSet();

    private static IQueryable<RiskCommitteeDecision> DecisionQuery(AuditableContext db) =>
        db.RiskCommitteeDecisions.AsNoTracking()
            .Include(d => d.Votes)
            .Include(d => d.Committee).ThenInclude(c => c.Members);

    private static async Task<List<RiskCommitteeDecisionDto>> ToDtosAsync(AuditableContext db,
        List<RiskCommitteeDecision> decisions)
    {
        var riskIds = decisions.Select(d => d.RiskId).Distinct().ToList();
        var risks = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);

        var voterIds = decisions.SelectMany(d => d.Votes).Where(v => v.VoterId != null).Select(v => v.VoterId!.Value)
            .Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => voterIds.Contains(u.Value))
            .Select(u => new { u.Value, u.Name }).ToDictionaryAsync(u => u.Value, u => u.Name);

        var memberIds = decisions.SelectMany(d => d.Committee.Members).Select(m => m.UserId).Distinct().ToList();
        var thirdLine = await ThirdLineGuard.ThirdLineAmongAsync(db, memberIds);

        var result = new List<RiskCommitteeDecisionDto>();
        foreach (var d in decisions)
        {
            var risk = risks.GetValueOrDefault(d.RiskId);
            var eligible = risk is null
                ? 0
                : EligibleMembers(d.Committee.Members.Select(m => m.UserId), risk, thirdLine)
                    .Union(d.Votes.Where(v => v.VoterId != null).Select(v => v.VoterId!.Value)).Count();

            result.Add(new RiskCommitteeDecisionDto
            {
                Id = d.Id,
                CommitteeId = d.CommitteeId,
                CommitteeName = d.Committee.Name,
                RiskId = d.RiskId,
                RiskSubject = risk?.Subject ?? string.Empty,
                Kind = d.Kind,
                RenewsAcceptanceId = d.RenewsAcceptanceId,
                Status = d.Status,
                RequiredApprovals = d.RequiredApprovals,
                Name = d.Name,
                BusinessJustification = d.BusinessJustification,
                CompensatingControls = d.CompensatingControls,
                ExpiresAt = d.ExpiresAt,
                MinutesReference = d.MinutesReference,
                OpenedById = d.OpenedById,
                OpenedAt = d.OpenedAt,
                ClosedAt = d.ClosedAt,
                WithdrawalReason = d.WithdrawalReason,
                AcceptanceId = d.AcceptanceId,
                Approvals = d.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Approve),
                Rejections = d.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Reject),
                Abstentions = d.Votes.Count(v => v.Choice == RiskCommitteeVoteChoice.Abstain),
                EligibleVoters = eligible,
                Votes = d.Votes.OrderBy(v => v.CastAt).ThenBy(v => v.Id).Select(v => new RiskCommitteeVoteDto
                {
                    VoterId = v.VoterId,
                    VoterName = v.VoterId is { } id ? names.GetValueOrDefault(id) : null,
                    Choice = v.Choice, Comment = v.Comment, CastAt = v.CastAt
                }).ToList()
            });
        }

        return result;
    }

    private async Task<RiskCommitteeDto> ReadCommitteeAsync(int committeeId)
    {
        await using var read = DalService.GetContext();
        return ToDto(await RequireCommitteeAsync(read, committeeId, tracked: false));
    }

    private static RiskCommitteeDto ToDto(RiskCommittee c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Mandate = c.Mandate,
        EntityId = c.EntityId,
        RequiredApprovals = c.RequiredApprovals,
        RetiredAt = c.RetiredAt,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
        UpdatedById = c.UpdatedById,
        Members = c.Members.OrderBy(m => m.UserId).Select(m => new RiskCommitteeMemberDto
        {
            UserId = m.UserId, Name = m.User?.Name ?? string.Empty, AddedAt = m.CreatedAt, AddedById = m.CreatedById
        }).ToList()
    };

    private static async Task<RiskCommittee> RequireCommitteeAsync(AuditableContext db, int committeeId, bool tracked)
    {
        var query = tracked ? db.RiskCommittees : db.RiskCommittees.AsNoTracking();
        return await query.Include(c => c.Members).ThenInclude(m => m.User).FirstOrDefaultAsync(c => c.Id == committeeId)
               ?? throw new DataNotFoundException("risk_committees", committeeId.ToString(Invariant));
    }

    private static async Task RequireEntityAsync(AuditableContext db, int? entityId)
    {
        if (entityId is { } entity && !await db.Entities.AnyAsync(e => e.Id == entity))
            throw new DataNotFoundException("entities", entity.ToString(Invariant));
    }

    private static void RefuseRetired(RiskCommittee committee)
    {
        if (committee.RetiredAt is not null)
            throw new RuleBrokenException(
                $"The committee was retired on {committee.RetiredAt:yyyy-MM-dd}: it takes no member, no decision and no vote.",
                RetiredRule);
    }

    private sealed record ValidCommittee(string Name, string? Mandate, int? EntityId, int RequiredApprovals);

    private static ValidCommittee ValidateCommittee(RiskCommitteeRequest request)
    {
        var name = Required(request.Name, DecisionCycleLimits.MaxCommitteeNameLength, nameof(RiskCommitteeRequest.Name),
            "The committee needs a name.");
        var mandate = Optional(request.Mandate, DecisionCycleLimits.MaxMandateLength, nameof(RiskCommitteeRequest.Mandate));

        if (request.RequiredApprovals is not { } required ||
            required is < DecisionCycleLimits.MinRequiredApprovals or > DecisionCycleLimits.MaxRequiredApprovals)
            throw new InvalidParameterException(nameof(RiskCommitteeRequest.RequiredApprovals),
                $"A committee decides with {DecisionCycleLimits.MinRequiredApprovals} to " +
                $"{DecisionCycleLimits.MaxRequiredApprovals} approvals — one would be an individual approval.");

        if (request.EntityId is <= 0)
            throw new InvalidParameterException(nameof(RiskCommitteeRequest.EntityId), "The entity is an entity id.");

        return new ValidCommittee(name, mandate, request.EntityId, required);
    }

    private sealed record ValidDecision(int RiskId, RiskCommitteeDecisionKind Kind, int? RenewsAcceptanceId,
        string? Name, string Justification, string? CompensatingControls, DateTime ExpiresAt, string? MinutesReference);

    private ValidDecision ValidateDecision(RiskCommitteeDecisionRequest request)
    {
        if (request.RiskId is not { } riskId || riskId <= 0)
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.RiskId), "The risk is required.");

        if (request.Kind is not { } kind || !Enum.IsDefined(kind))
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.Kind),
                "The decision is an acceptance (1) or a renewal (2).");

        if (kind == RiskCommitteeDecisionKind.Renew && request.RenewsAcceptanceId is not > 0)
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.RenewsAcceptanceId),
                "A renewal names the acceptance it renews.");

        if (kind == RiskCommitteeDecisionKind.Accept && request.RenewsAcceptanceId is not null)
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.RenewsAcceptanceId),
                "Only a renewal names an acceptance.");

        var justification = Required(request.BusinessJustification, DecisionCycleLimits.MaxDecisionJustificationLength,
            nameof(RiskCommitteeDecisionRequest.BusinessJustification),
            "An acceptance needs a written business justification — the field the auditor reads.");

        if (request.ExpiresAt is not { } expires)
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.ExpiresAt),
                "An acceptance needs an expiry date.");

        var expiresUtc = expires.Kind == DateTimeKind.Local ? expires.ToUniversalTime() : expires;
        if (expiresUtc <= Clock())
            throw new InvalidParameterException(nameof(RiskCommitteeDecisionRequest.ExpiresAt),
                "The expiry date has to be in the future.");

        return new ValidDecision(riskId, kind, request.RenewsAcceptanceId,
            Optional(request.Name, DecisionCycleLimits.MaxDecisionNameLength, nameof(RiskCommitteeDecisionRequest.Name)),
            justification,
            Optional(request.CompensatingControls, DecisionCycleLimits.MaxDecisionJustificationLength,
                nameof(RiskCommitteeDecisionRequest.CompensatingControls)),
            expiresUtc,
            Optional(request.MinutesReference, DecisionCycleLimits.MaxMinutesReferenceLength,
                nameof(RiskCommitteeDecisionRequest.MinutesReference)));
    }

    private static string Required(string? text, int max, string parameter, string missing)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidParameterException(parameter, missing);

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }

    private static string? Optional(string? text, int max, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim();
        if (trimmed.Length > max) throw new InvalidParameterException(parameter, $"At most {max} characters.");

        return trimmed;
    }
}
