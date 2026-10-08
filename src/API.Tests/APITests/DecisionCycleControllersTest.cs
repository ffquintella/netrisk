using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DecisionCycle;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.9 (S50 §6) — <see cref="RiskArchiveController"/>, <see cref="BacktestingController"/> and
/// <see cref="RiskCommitteesController"/>: each action's success shape, that the acting user and every argument reach
/// the service, and the mapping of every domain exception through <c>DecisionCycleErrors</c> onto the status code the
/// other controllers use. An entity-scope violation is the one exception not answered here: it is re-thrown for
/// <c>EntityScopeViolationMiddleware</c> to make a 403.
/// </summary>
[TestSubject(typeof(RiskArchiveController))]
public class DecisionCycleControllersTest : BaseControllerTest
{
    private const int Known = DecisionCycleIds.Known;
    private const int Invalid = DecisionCycleIds.Invalid;
    private const int Forbidden = DecisionCycleIds.Forbidden;
    private const int Missing = DecisionCycleIds.Missing;
    private const int Conflict = DecisionCycleIds.Conflict;
    private const int Rule = DecisionCycleIds.Rule;
    private const int Transition = DecisionCycleIds.Transition;
    private const int Scope = DecisionCycleIds.Scope;
    private const int Broken = DecisionCycleIds.Broken;

    private readonly RiskArchiveController _archive;
    private readonly BacktestingController _backtesting;
    private readonly RiskCommitteesController _committees;

    public DecisionCycleControllersTest()
    {
        _archive = _serviceProvider.GetRequiredService<RiskArchiveController>();
        _backtesting = _serviceProvider.GetRequiredService<BacktestingController>();
        _committees = _serviceProvider.GetRequiredService<RiskCommitteesController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly RiskArchiveRequest ArchiveBody = new()
    {
        Justification = "Not worth treating.", CloseReason = 2,
        Conditions = [new RiskArchiveConditionRequest { TriggerType = ReassessmentTriggerType.NewRegulation, Description = "A law." }]
    };

    private static readonly RiskArchiveReopenRequest ReopenBody = new() { Reason = "It is back." };
    private static readonly RiskArchiveReviewRequest ReviewBody = new() { Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Still low." };
    private static readonly BacktestAssessmentRequest AssessBody = new() { RiskIds = [4, 5], Note = "Matched." };
    private static readonly RiskCommitteeRequest CommitteeBody = new() { Name = "Risk committee", Mandate = "Accepts.", EntityId = 3, RequiredApprovals = 2 };
    private static readonly RiskCommitteeDecisionRequest DecisionBody = new()
        { RiskId = 4, Kind = RiskCommitteeDecisionKind.Accept, Name = "Accept", BusinessJustification = "Cheap." };
    private static readonly RiskCommitteeVoteRequest VoteBody = new() { Choice = RiskCommitteeVoteChoice.Approve, Comment = "Agree." };
    private static readonly RiskCommitteeWithdrawRequest WithdrawBody = new() { Reason = "No longer needed." };

    /// <summary>Every action whose id can drive the service to an error, called with that id.</summary>
    private IEnumerable<(string Name, Func<int, Task<ActionResult?>> Call)> IdDrivenActions()
    {
        yield return ("GetRiskArchives", async id => (await _archive.GetRiskArchives(id)).Result);
        yield return ("Archive", async id => (await _archive.Archive(id, ArchiveBody)).Result);
        yield return ("Reopen", async id => (await _archive.Reopen(id, ReopenBody)).Result);
        yield return ("Review", async id => (await _archive.Review(id, ReviewBody)).Result);

        yield return ("GetReport", async id => (await _backtesting.GetReport(null, null, id)).Result);
        yield return ("GetIncident", async id => (await _backtesting.GetIncident(id)).Result);
        yield return ("Assess", async id => (await _backtesting.Assess(id, AssessBody)).Result);

        yield return ("GetCommittee", async id => (await _committees.GetCommittee(id)).Result);
        yield return ("CreateCommittee", async id =>
            (await _committees.CreateCommittee(new RiskCommitteeRequest { Name = "C", EntityId = id })).Result);
        yield return ("UpdateCommittee", async id => (await _committees.UpdateCommittee(id, CommitteeBody)).Result);
        yield return ("RetireCommittee", async id => (await _committees.RetireCommittee(id)).Result);
        yield return ("AddMember", async id => (await _committees.AddMember(id, 3)).Result);
        yield return ("AddMemberUser", async id => (await _committees.AddMember(Known, id)).Result);
        yield return ("RemoveMember", async id => await _committees.RemoveMember(id, 3));
        yield return ("RemoveMemberUser", async id => await _committees.RemoveMember(Known, id));
        yield return ("GetDecisions", async id => (await _committees.GetDecisions(id)).Result);
        yield return ("GetDecision", async id => (await _committees.GetDecision(id)).Result);
        yield return ("OpenDecision", async id => (await _committees.OpenDecision(id, DecisionBody)).Result);
        yield return ("Vote", async id => (await _committees.Vote(id, VoteBody)).Result);
        yield return ("Withdraw", async id => (await _committees.Withdraw(id, WithdrawBody)).Result);
    }

    // --- success ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheArchiveActionsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _archive.GetArchives(true, true)).Result);
        var first = Assert.Single(Assert.IsType<List<RiskArchiveDto>>(list.Value));
        Assert.Equal((4, RiskArchiveState.Live), (first.RiskId, first.State));

        var risk = Assert.IsType<OkObjectResult>((await _archive.GetRiskArchives(7)).Result);
        Assert.Equal(7, Assert.Single(Assert.IsType<List<RiskArchiveDto>>(risk.Value)).RiskId);

        var archived = Assert.IsType<OkObjectResult>((await _archive.Archive(7, ArchiveBody)).Result);
        var dto = Assert.IsType<RiskArchiveDto>(archived.Value);
        Assert.Equal((7, RiskArchiveStatus.Archived), (dto.RiskId, dto.Status));
        Assert.Single(dto.Conditions);

        var reopened = Assert.IsType<OkObjectResult>((await _archive.Reopen(7, ReopenBody)).Result);
        Assert.Equal(RiskArchiveStatus.Reopened, Assert.IsType<RiskArchiveDto>(reopened.Value).Status);

        var reviewed = Assert.IsType<OkObjectResult>((await _archive.Review(7, ReviewBody)).Result);
        Assert.Equal(7, Assert.IsType<RiskArchiveDto>(reviewed.Value).RiskId);
    }

    [Fact]
    public async Task TestTheBacktestingActionsAnswer200WithTheirDtos()
    {
        var report = Assert.IsType<OkObjectResult>((await _backtesting.GetReport(null, null, 3)).Result);
        var panel = Assert.IsType<BacktestReportDto>(report.Value);
        Assert.Equal((3, 3, 2), (panel.EntityId, panel.Incidents, panel.Assessed));
        Assert.Single(panel.Items);

        var incident = Assert.IsType<OkObjectResult>((await _backtesting.GetIncident(7)).Result);
        var dto = Assert.IsType<BacktestIncidentDto>(incident.Value);
        Assert.Equal((7, BacktestOutcome.ForeseenTreated), (dto.IncidentId, dto.Outcome));
        Assert.Single(dto.Risks);

        var assessed = Assert.IsType<OkObjectResult>((await _backtesting.Assess(7, AssessBody)).Result);
        Assert.Equal(7, Assert.IsType<BacktestIncidentDto>(assessed.Value).IncidentId);
    }

    [Fact]
    public async Task TestTheCommitteeActionsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _committees.GetCommittees(true)).Result);
        Assert.Equal(Known, Assert.Single(Assert.IsType<List<RiskCommitteeDto>>(list.Value)).Id);

        var one = Assert.IsType<OkObjectResult>((await _committees.GetCommittee(7)).Result);
        var committee = Assert.IsType<RiskCommitteeDto>(one.Value);
        Assert.Equal((7, 2), (committee.Id, committee.RequiredApprovals));
        Assert.Single(committee.Members);

        var created = Assert.IsType<OkObjectResult>((await _committees.CreateCommittee(CommitteeBody)).Result);
        Assert.Equal(Known, Assert.IsType<RiskCommitteeDto>(created.Value).Id);

        var updated = Assert.IsType<OkObjectResult>((await _committees.UpdateCommittee(7, CommitteeBody)).Result);
        Assert.Equal(7, Assert.IsType<RiskCommitteeDto>(updated.Value).Id);

        var retired = Assert.IsType<OkObjectResult>((await _committees.RetireCommittee(7)).Result);
        Assert.NotNull(Assert.IsType<RiskCommitteeDto>(retired.Value).RetiredAt);

        var member = Assert.IsType<OkObjectResult>((await _committees.AddMember(7, 3)).Result);
        Assert.Equal(7, Assert.IsType<RiskCommitteeDto>(member.Value).Id);

        Assert.IsType<NoContentResult>(await _committees.RemoveMember(7, 3));
    }

    [Fact]
    public async Task TestTheDecisionActionsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _committees.GetDecisions(Known, 4, true)).Result);
        Assert.Equal(Known, Assert.Single(Assert.IsType<List<RiskCommitteeDecisionDto>>(list.Value)).Id);

        var one = Assert.IsType<OkObjectResult>((await _committees.GetDecision(8)).Result);
        var decision = Assert.IsType<RiskCommitteeDecisionDto>(one.Value);
        Assert.Equal((8, RiskCommitteeDecisionStatus.Open), (decision.Id, decision.Status));
        Assert.Single(decision.Votes);

        var opened = Assert.IsType<OkObjectResult>((await _committees.OpenDecision(7, DecisionBody)).Result);
        Assert.Equal(Known, Assert.IsType<RiskCommitteeDecisionDto>(opened.Value).Id);

        var voted = Assert.IsType<OkObjectResult>((await _committees.Vote(8, VoteBody)).Result);
        Assert.Equal(8, Assert.IsType<RiskCommitteeDecisionDto>(voted.Value).Id);

        var withdrawn = Assert.IsType<OkObjectResult>((await _committees.Withdraw(8, WithdrawBody)).Result);
        Assert.Equal(RiskCommitteeDecisionStatus.Withdrawn, Assert.IsType<RiskCommitteeDecisionDto>(withdrawn.Value).Status);
    }

    // --- error mapping ------------------------------------------------------------------------

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<BadRequestObjectResult>(await call(Invalid));
            Assert.True(("invalid_parameter", "Reason") == (Property(result.Value, "error"), Property(result.Value, "ParameterName")), name);
            Assert.False(string.IsNullOrWhiteSpace(Property(result.Value, "Message")), name);
        }
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryAction()
    {
        foreach (var (name, call) in IdDrivenActions())
            Assert.True(await call(Missing) is NotFoundResult, name);
    }

    [Fact]
    public async Task TestAnExistingRecordIsA409()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ConflictObjectResult>(await call(Conflict));
            Assert.True(("already_exists", Conflict.ToString()) == (Property(result.Value, "error"), Property(result.Value, "Identification")), name);
            Assert.False(string.IsNullOrWhiteSpace(Property(result.Value, "Message")), name);
        }
    }

    [Fact]
    public async Task TestAPermissionRefusalIsA403NamingThePermission()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ObjectResult>(await call(Forbidden));
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.True(("not_permitted", "committee_member") == (Property(result.Value, "error"), Property(result.Value, "Permission")), name);
            Assert.False(string.IsNullOrWhiteSpace(Property(result.Value, "message")), name);
        }
    }

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<UnprocessableEntityObjectResult>(await call(Rule));
            Assert.True("risk_archive_not_live" == Property(result.Value, "error"), name);
            Assert.False(string.IsNullOrWhiteSpace(Property(result.Value, "Message")), name);
        }
    }

    [Fact]
    public async Task TestAnInvalidStateTransitionIsA422NamingBothStates()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<UnprocessableEntityObjectResult>(await call(Transition));
            Assert.True(("invalid_transition", "Closed", "Open") ==
                        (Property(result.Value, "error"), Property(result.Value, "FromState"), Property(result.Value, "ToState")), name);
        }
    }

    /// <summary>A write outside the caller's entities is not swallowed into a 500: the middleware answers 403.</summary>
    [Fact]
    public async Task TestAScopeViolationPropagatesToTheMiddleware()
    {
        foreach (var (_, call) in IdDrivenActions())
            await Assert.ThrowsAsync<EntityScopeViolationException>(() => call(Scope));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<StatusCodeResult>(await call(Broken));
            Assert.True(StatusCodes.Status500InternalServerError == result.StatusCode, name);
        }
    }

    /// <summary>The list actions no id can drive: a service that throws is mapped like any other, for each exception kind.</summary>
    [Fact]
    public async Task TestTheListActionsMapAFailureToo()
    {
        var archives = Substitute.For<IRiskArchiveService>();
        archives.GetArchivesAsync(Arg.Any<bool>(), Arg.Any<bool>())
            .Returns<List<RiskArchiveDto>>(_ => throw new InvalidOperationException("boom"));
        var committeesService = Substitute.For<IRiskCommitteesService>();
        committeesService.GetCommitteesAsync(Arg.Any<bool>())
            .Returns<List<RiskCommitteeDto>>(_ => throw new DAL.Exceptions.EntityScopeViolationException(nameof(DAL.Entities.Risk), 1, "2"));
        var committeesBroken = Substitute.For<IRiskCommitteesService>();
        committeesBroken.GetCommitteesAsync(Arg.Any<bool>())
            .Returns<List<RiskCommitteeDto>>(_ => throw new Model.Exceptions.RuleBrokenException("Retired.", "committee_retired"));

        var archive = ResolveController<RiskArchiveController>(s => s.AddSingleton(archives));
        var scoped = ResolveController<RiskCommitteesController>(s => s.AddSingleton(committeesService));
        var broken = ResolveController<RiskCommitteesController>(s => s.AddSingleton(committeesBroken));

        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await archive.GetArchives()).Result).StatusCode);
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => scoped.GetCommittees());
        Assert.Equal("committee_retired", Property(Assert.IsType<UnprocessableEntityObjectResult>(
            (await broken.GetCommittees()).Result).Value, "error"));
    }

    // --- arguments ----------------------------------------------------------------------------

    /// <summary>The acting user's id and every argument reach the archive service; a controller passing a fixed value would be invisible above.</summary>
    [Fact]
    public async Task TestTheArchiveUserIdAndArgumentsReachTheService()
    {
        var recording = MockedRiskArchiveService.Create();
        var controller = ResolveController<RiskArchiveController>(s => s.AddSingleton(recording));

        await controller.GetArchives();
        await controller.GetArchives(true, true);
        await controller.GetRiskArchives(7);
        await controller.Archive(7, ArchiveBody);
        await controller.Reopen(7, ReopenBody);
        await controller.Review(7, ReviewBody);

        await recording.Received(1).GetArchivesAsync(false, false);
        await recording.Received(1).GetArchivesAsync(true, true);
        await recording.Received(1).GetRiskArchivesAsync(7);
        await recording.Received(1).ArchiveAsync(7, Arg.Is<RiskArchiveRequest>(r =>
            r.Justification == "Not worth treating." && r.CloseReason == 2
            && r.Conditions!.Single().TriggerType == ReassessmentTriggerType.NewRegulation), 1);
        await recording.Received(1).ReopenAsync(7, Arg.Is<RiskArchiveReopenRequest>(r => r.Reason == "It is back."), 1);
        await recording.Received(1).ReviewAsync(7, Arg.Is<RiskArchiveReviewRequest>(r =>
            r.Outcome == RiskArchiveReviewOutcome.KeepArchived && r.Note == "Still low."), 1);
    }

    [Fact]
    public async Task TestTheBacktestingUserIdAndArgumentsReachTheService()
    {
        var recording = MockedBacktestingService.Create();
        var controller = ResolveController<BacktestingController>(s => s.AddSingleton(recording));
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        await controller.GetReport();
        await controller.GetReport(from, to, 3);
        await controller.GetIncident(7);
        await controller.Assess(7, AssessBody);

        await recording.Received(1).GetReportAsync(null, null, null);
        await recording.Received(1).GetReportAsync(from, to, 3);
        await recording.Received(1).GetIncidentAsync(7);
        await recording.Received(1).AssessAsync(7, Arg.Is<BacktestAssessmentRequest>(r =>
            r.RiskIds!.SequenceEqual(new[] { 4, 5 }) && r.Note == "Matched."), 1);
    }

    [Fact]
    public async Task TestTheCommitteeUserIdAndArgumentsReachTheService()
    {
        var recording = MockedRiskCommitteesService.Create();
        var controller = ResolveController<RiskCommitteesController>(s => s.AddSingleton(recording));

        await controller.GetCommittees();
        await controller.GetCommittees(true);
        await controller.GetCommittee(7);
        await controller.CreateCommittee(CommitteeBody);
        await controller.UpdateCommittee(7, CommitteeBody);
        await controller.RetireCommittee(7);
        await controller.AddMember(7, 3);
        await controller.RemoveMember(7, 3);
        await controller.GetDecisions();
        await controller.GetDecisions(7, 4, true);
        await controller.GetDecision(8);
        await controller.OpenDecision(7, DecisionBody);
        await controller.Vote(8, VoteBody);
        await controller.Withdraw(8, WithdrawBody);

        await recording.Received(1).GetCommitteesAsync(false);
        await recording.Received(1).GetCommitteesAsync(true);
        await recording.Received(1).GetCommitteeAsync(7);
        await recording.Received(1).CreateCommitteeAsync(Arg.Is<RiskCommitteeRequest>(r =>
            r.Name == "Risk committee" && r.EntityId == 3 && r.RequiredApprovals == 2), 1);
        await recording.Received(1).UpdateCommitteeAsync(7, Arg.Is<RiskCommitteeRequest>(r => r.Mandate == "Accepts."), 1);
        await recording.Received(1).RetireCommitteeAsync(7, 1);
        await recording.Received(1).AddMemberAsync(7, 3, 1);
        await recording.Received(1).RemoveMemberAsync(7, 3, 1);
        await recording.Received(1).GetDecisionsAsync(null, null, false);
        await recording.Received(1).GetDecisionsAsync(7, 4, true);
        await recording.Received(1).GetDecisionAsync(8);
        await recording.Received(1).OpenDecisionAsync(7, Arg.Is<RiskCommitteeDecisionRequest>(r =>
            r.RiskId == 4 && r.Kind == RiskCommitteeDecisionKind.Accept && r.BusinessJustification == "Cheap."), 1);
        await recording.Received(1).VoteAsync(8, Arg.Is<RiskCommitteeVoteRequest>(r =>
            r.Choice == RiskCommitteeVoteChoice.Approve && r.Comment == "Agree."), 1);
        await recording.Received(1).WithdrawAsync(8, Arg.Is<RiskCommitteeWithdrawRequest>(r => r.Reason == "No longer needed."), 1);
    }

    /// <summary>A missing body is replaced by an empty request, so the service names the missing field instead of throwing on null.</summary>
    [Fact]
    public async Task TestAMissingBodyReachesTheServiceAsAnEmptyRequest()
    {
        var archives = MockedRiskArchiveService.Create();
        var backtesting = MockedBacktestingService.Create();
        var committees = MockedRiskCommitteesService.Create();
        var archive = ResolveController<RiskArchiveController>(s => s.AddSingleton(archives));
        var backtest = ResolveController<BacktestingController>(s => s.AddSingleton(backtesting));
        var committee = ResolveController<RiskCommitteesController>(s => s.AddSingleton(committees));

        await archive.Archive(7, null);
        await archive.Reopen(7, null);
        await archive.Review(7, null);
        await backtest.Assess(7, null);
        await committee.CreateCommittee(null);
        await committee.UpdateCommittee(7, null);
        await committee.OpenDecision(7, null);
        await committee.Vote(8, null);
        await committee.Withdraw(8, null);

        await archives.Received(1).ArchiveAsync(7, Arg.Is<RiskArchiveRequest>(r => r != null && r.Justification == null), 1);
        await archives.Received(1).ReopenAsync(7, Arg.Is<RiskArchiveReopenRequest>(r => r != null && r.Reason == null), 1);
        await archives.Received(1).ReviewAsync(7, Arg.Is<RiskArchiveReviewRequest>(r => r != null && r.Outcome == null), 1);
        await backtesting.Received(1).AssessAsync(7, Arg.Is<BacktestAssessmentRequest>(r => r != null && r.RiskIds == null), 1);
        await committees.Received(1).CreateCommitteeAsync(Arg.Is<RiskCommitteeRequest>(r => r != null && r.Name == null), 1);
        await committees.Received(1).UpdateCommitteeAsync(7, Arg.Is<RiskCommitteeRequest>(r => r != null && r.Name == null), 1);
        await committees.Received(1).OpenDecisionAsync(7, Arg.Is<RiskCommitteeDecisionRequest>(r => r != null && r.RiskId == null), 1);
        await committees.Received(1).VoteAsync(8, Arg.Is<RiskCommitteeVoteRequest>(r => r != null && r.Choice == null), 1);
        await committees.Received(1).WithdrawAsync(8, Arg.Is<RiskCommitteeWithdrawRequest>(r => r != null && r.Reason == null), 1);
    }
}
