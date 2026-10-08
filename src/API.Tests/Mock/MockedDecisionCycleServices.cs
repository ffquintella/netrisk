using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.DecisionCycle;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// The ids that drive the error branches of the three Stage 9.9 doubles (S50 §6): 400 an invalid parameter, 401 a
/// permission refusal (answered 403), 404 missing, 409 already exists, 422 a broken rule, 423 an invalid state
/// transition (answered 422), 451 an entity-scope violation (re-thrown to the middleware), 500 anything else.
/// </summary>
public static class DecisionCycleIds
{
    public const int Known = 10;
    public const int Invalid = 400;
    public const int Forbidden = 401;
    public const int Missing = 404;
    public const int Conflict = 409;
    public const int Rule = 422;
    public const int Transition = 423;
    public const int Scope = 451;
    public const int Broken = 500;

    public static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("Reason", "A reason is required.");
            case Forbidden: throw new PermissionInvalidException("committee_member", 1, "voting");
            case Missing: throw new DataNotFoundException("decision_cycle", id.ToString());
            case Conflict:
                throw new DataAlreadyExistsException("netrisk", "risk_committee_votes", id.ToString(),
                    "The vote was already cast.");
            case Rule: throw new RuleBrokenException("The archive is not live.", "risk_archive_not_live");
            case Transition: throw new InvalidStateTransitionException("Closed", "Open", "The risk is closed.");
            case Scope: throw new DAL.Exceptions.EntityScopeViolationException(nameof(DAL.Entities.Risk), 99, "100");
            case Broken: throw new InvalidOperationException("boom");
        }
    }
}

/// <summary>A deterministic <see cref="IRiskArchiveService"/> for <c>DecisionCycleControllersTest</c>; the risk id drives the branch.</summary>
public static class MockedRiskArchiveService
{
    public static RiskArchiveDto Archive(int riskId) => new()
    {
        Id = 5, RiskId = riskId, RiskSubject = "Ransomware", Status = RiskArchiveStatus.Archived,
        State = RiskArchiveState.Live, Justification = "Not worth treating.", PreviousStatus = "Mitigation Planned",
        ArchivedAt = DecisionCycleIds.When, ArchivedById = 1, NextReviewDueAt = DecisionCycleIds.When.AddMonths(3),
        Conditions = [new RiskArchiveConditionDto { TriggerType = ReassessmentTriggerType.NewRegulation, Description = "A law." }]
    };

    public static IRiskArchiveService Create()
    {
        var service = Substitute.For<IRiskArchiveService>();

        service.GetArchivesAsync(Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(_ => new List<RiskArchiveDto> { Archive(4) });
        service.GetRiskArchivesAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return new List<RiskArchiveDto> { Archive(call.Arg<int>()) };
        });
        service.ArchiveAsync(Arg.Any<int>(), Arg.Any<RiskArchiveRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Archive(call.ArgAt<int>(0));
        });
        service.ReopenAsync(Arg.Any<int>(), Arg.Any<RiskArchiveReopenRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var archive = Archive(call.ArgAt<int>(0));
            archive.Status = RiskArchiveStatus.Reopened;
            return archive;
        });
        service.ReviewAsync(Arg.Any<int>(), Arg.Any<RiskArchiveReviewRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Archive(call.ArgAt<int>(0));
        });

        return service;
    }
}

/// <summary>A deterministic <see cref="IBacktestingService"/>; the incident id (the entity id for the report) drives the branch.</summary>
public static class MockedBacktestingService
{
    public static BacktestIncidentDto Incident(int incidentId) => new()
    {
        IncidentId = incidentId, IncidentName = "Outage", Kind = IncidentKind.Incident, OccurredAt = DecisionCycleIds.When,
        Outcome = BacktestOutcome.ForeseenTreated, AssessedAt = DecisionCycleIds.When, AssessedById = 1,
        Risks = [new BacktestRiskDto { RiskId = 4, Subject = "Ransomware", RegisteredBeforeOccurrence = true }]
    };

    public static IBacktestingService Create()
    {
        var service = Substitute.For<IBacktestingService>();

        service.GetReportAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<int?>()).Returns(call =>
        {
            if (call.ArgAt<int?>(2) is { } entityId) DecisionCycleIds.Gate(entityId);
            return new BacktestReportDto
            {
                From = call.ArgAt<DateTime?>(0) ?? DecisionCycleIds.When.AddYears(-1),
                To = call.ArgAt<DateTime?>(1) ?? DecisionCycleIds.When, EntityId = call.ArgAt<int?>(2),
                Incidents = 3, Assessed = 2, ForeseenTreated = 1, NotForeseen = 1, Items = [Incident(7)]
            };
        });
        service.GetIncidentAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Incident(call.Arg<int>());
        });
        service.AssessAsync(Arg.Any<int>(), Arg.Any<BacktestAssessmentRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Incident(call.ArgAt<int>(0));
        });

        return service;
    }
}

/// <summary>
/// A deterministic <see cref="IRiskCommitteesService"/>; the committee id (the decision id for a decision, the committee
/// filter for the decision list) drives the branch.
/// </summary>
public static class MockedRiskCommitteesService
{
    public static RiskCommitteeDto Committee(int id) => new()
    {
        Id = id, Name = "Risk committee", Mandate = "Accepts residual risk.", RequiredApprovals = 2,
        CreatedAt = DecisionCycleIds.When,
        Members = [new RiskCommitteeMemberDto { UserId = 3, Name = "Member", AddedAt = DecisionCycleIds.When }]
    };

    public static RiskCommitteeDecisionDto Decision(int id) => new()
    {
        Id = id, CommitteeId = DecisionCycleIds.Known, CommitteeName = "Risk committee", RiskId = 4,
        RiskSubject = "Ransomware", Kind = RiskCommitteeDecisionKind.Accept, Status = RiskCommitteeDecisionStatus.Open,
        RequiredApprovals = 2, BusinessJustification = "Cheaper than treating it.", ExpiresAt = DecisionCycleIds.When.AddYears(1),
        OpenedAt = DecisionCycleIds.When, Approvals = 1, EligibleVoters = 3,
        Votes = [new RiskCommitteeVoteDto { VoterId = 3, Choice = RiskCommitteeVoteChoice.Approve, CastAt = DecisionCycleIds.When }]
    };

    public static IRiskCommitteesService Create()
    {
        var service = Substitute.For<IRiskCommitteesService>();

        service.GetCommitteesAsync(Arg.Any<bool>()).Returns(_ => new List<RiskCommitteeDto> { Committee(DecisionCycleIds.Known) });
        service.GetCommitteeAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Committee(call.Arg<int>());
        });
        service.CreateCommitteeAsync(Arg.Any<RiskCommitteeRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.Arg<RiskCommitteeRequest>();
            if (request.EntityId is { } entityId) DecisionCycleIds.Gate(entityId);
            return Committee(DecisionCycleIds.Known);
        });
        service.UpdateCommitteeAsync(Arg.Any<int>(), Arg.Any<RiskCommitteeRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Committee(call.ArgAt<int>(0));
        });
        service.RetireCommitteeAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var committee = Committee(call.ArgAt<int>(0));
            committee.RetiredAt = DecisionCycleIds.When;
            return committee;
        });
        service.AddMemberAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Committee(call.ArgAt<int>(0));
        });
        service.RemoveMemberAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            DecisionCycleIds.Gate(call.ArgAt<int>(1));
            return Task.CompletedTask;
        });

        service.GetDecisionsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>()).Returns(call =>
        {
            if (call.ArgAt<int?>(0) is { } committeeId) DecisionCycleIds.Gate(committeeId);
            return new List<RiskCommitteeDecisionDto> { Decision(DecisionCycleIds.Known) };
        });
        service.GetDecisionAsync(Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.Arg<int>());
            return Decision(call.Arg<int>());
        });
        service.OpenDecisionAsync(Arg.Any<int>(), Arg.Any<RiskCommitteeDecisionRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Decision(DecisionCycleIds.Known);
        });
        service.VoteAsync(Arg.Any<int>(), Arg.Any<RiskCommitteeVoteRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            return Decision(call.ArgAt<int>(0));
        });
        service.WithdrawAsync(Arg.Any<int>(), Arg.Any<RiskCommitteeWithdrawRequest>(), Arg.Any<int>()).Returns(call =>
        {
            DecisionCycleIds.Gate(call.ArgAt<int>(0));
            var decision = Decision(call.ArgAt<int>(0));
            decision.Status = RiskCommitteeDecisionStatus.Withdrawn;
            return decision;
        });

        return service;
    }
}
