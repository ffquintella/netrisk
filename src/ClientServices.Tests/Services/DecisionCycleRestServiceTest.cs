using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.9 (S50 §7) — <see cref="DecisionCycleRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every body and query string it sends and every status branch runs for real. A refusal (a risk already closed, a vote
/// already cast, a member recused) keeps the server's sentence.
/// </summary>
[TestSubject(typeof(DecisionCycleRestService))]
public class DecisionCycleRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IDecisionCycleService _service;

    public DecisionCycleRestServiceTest()
    {
        _service = ResolveWith<IDecisionCycleService>(_backend);
    }

    private static RiskArchiveDto Archive(int riskId = 4) => new()
    {
        Id = 5, RiskId = riskId, RiskSubject = "Ransomware", Status = RiskArchiveStatus.Archived, State = RiskArchiveState.Live,
        Justification = "Not worth treating.", ReviewOverdue = true,
        Conditions = [new RiskArchiveConditionDto { TriggerType = ReassessmentTriggerType.NewRegulation, Description = "A law." }],
        Reviews = [new RiskArchiveReviewDto { Id = 1, Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Still low." }]
    };

    private static BacktestIncidentDto Incident(int id = 7) => new()
    {
        IncidentId = id, IncidentName = "Outage", Kind = IncidentKind.NearMiss, Outcome = BacktestOutcome.ForeseenDismissed,
        HiddenRiskCount = 2,
        Risks = [new BacktestRiskDto { RiskId = 3, Subject = "Ransomware", DismissedAtOccurrence = true, DismissalReasons = ["archived"] }]
    };

    private static RiskCommitteeDto Committee(int id = 6) => new()
    {
        Id = id, Name = "Risk committee", RequiredApprovals = 2,
        Members = [new RiskCommitteeMemberDto { UserId = 3, Name = "Member" }]
    };

    private static RiskCommitteeDecisionDto Decision(int id = 8) => new()
    {
        Id = id, CommitteeId = 6, RiskId = 4, Kind = RiskCommitteeDecisionKind.Renew, Status = RiskCommitteeDecisionStatus.Open,
        RequiredApprovals = 2, Approvals = 1, EligibleVoters = 3,
        Votes = [new RiskCommitteeVoteDto { VoterId = 3, Choice = RiskCommitteeVoteChoice.Reject, Comment = "No." }]
    };

    private bool BodyHas(string fragment) =>
        _backend.LastRequest.Body.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private bool QueryHas(string fragment) =>
        _backend.LastRequest.Query.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // --- archive ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheArchivesAreListedWithTheirFiltersOnlyWhenAsked()
    {
        _backend.OnGet("/RiskArchive/Risks", new[] { Archive() });

        var all = await _service.GetArchivesAsync();
        var first = Assert.Single(all);
        Assert.Equal((4, RiskArchiveState.Live, true), (first.RiskId, first.State, first.ReviewOverdue));
        Assert.Single(first.Conditions);
        Assert.Single(first.Reviews);
        Assert.True(_backend.Sent(Method.Get, "/RiskArchive/Risks"));
        Assert.False(QueryHas("dueOnly"));
        Assert.False(QueryHas("includeEnded"));

        await _service.GetArchivesAsync(dueOnly: true);
        Assert.True(QueryHas("dueOnly=true"));
        Assert.False(QueryHas("includeEnded"));

        await _service.GetArchivesAsync(includeEnded: true);
        Assert.True(QueryHas("includeEnded=true"));
        Assert.False(QueryHas("dueOnly"));

        await _service.GetArchivesAsync(true, true);
        Assert.True(QueryHas("dueOnly=true"));
        Assert.True(QueryHas("includeEnded=true"));
    }

    [Fact]
    public async Task TestTheArchivesOfARiskAreRead()
    {
        _backend.OnGet("/RiskArchive/Risks/4", new[] { Archive() });

        var list = await _service.GetRiskArchivesAsync(4);

        Assert.Equal(4, Assert.Single(list).RiskId);
        Assert.True(_backend.Sent(Method.Get, "/RiskArchive/Risks/4"));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));
    }

    [Fact]
    public async Task TestARiskIsArchivedWithItsBody()
    {
        _backend.OnPost("/RiskArchive/Risks/4", Archive());

        var archived = await _service.ArchiveAsync(4, new RiskArchiveRequest
        {
            Justification = "Not worth treating.", CloseReason = 2,
            Conditions = [new RiskArchiveConditionRequest { TriggerType = ReassessmentTriggerType.NewRegulation, Description = "A law." }]
        });

        Assert.Equal((4, RiskArchiveStatus.Archived), (archived.RiskId, archived.Status));
        Assert.True(_backend.Sent(Method.Post, "/RiskArchive/Risks/4"));
        Assert.True(BodyHas("\"justification\":\"Not worth treating.\""));
        Assert.True(BodyHas("\"closeReason\":2"));
        Assert.True(BodyHas("\"conditions\":[{"));
        Assert.True(BodyHas("\"description\":\"A law.\""));
    }

    [Fact]
    public async Task TestAnArchiveIsReopenedAndReviewedWithItsBody()
    {
        _backend.OnPost("/RiskArchive/Risks/4/Reopen", Archive());
        _backend.OnPost("/RiskArchive/Risks/4/Reviews", Archive());

        var reopened = await _service.ReopenArchiveAsync(4, new RiskArchiveReopenRequest { Reason = "It is back." });
        Assert.Equal(4, reopened.RiskId);
        Assert.True(_backend.Sent(Method.Post, "/RiskArchive/Risks/4/Reopen"));
        Assert.True(BodyHas("\"reason\":\"It is back.\""));

        var reviewed = await _service.ReviewArchiveAsync(4,
            new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.Reopen, Note = "Worse now." });
        Assert.Equal(4, reviewed.RiskId);
        Assert.True(_backend.Sent(Method.Post, "/RiskArchive/Risks/4/Reviews"));
        Assert.True(BodyHas("\"outcome\":2"));
        Assert.True(BodyHas("\"note\":\"Worse now.\""));
    }

    // --- backtesting --------------------------------------------------------------------------

    [Fact]
    public async Task TestTheReportIsReadWithItsFiltersOnlyWhenGiven()
    {
        _backend.OnGet("/Backtesting", new BacktestReportDto
        {
            Incidents = 3, NearMisses = 1, Assessed = 2, NotAssessed = 1, UnforeseenRate = 0.5, FalseNegativeRate = null,
            Truncated = true, Items = [Incident()]
        });

        var report = await _service.GetBacktestingReportAsync();
        Assert.Equal((3, 2, 0.5, true), (report.Incidents, report.Assessed, report.UnforeseenRate, report.Truncated));
        Assert.Null(report.FalseNegativeRate);
        Assert.Equal(BacktestOutcome.ForeseenDismissed, Assert.Single(report.Items).Outcome);
        Assert.True(_backend.Sent(Method.Get, "/Backtesting"));
        Assert.False(QueryHas("from"));
        Assert.False(QueryHas("to="));
        Assert.False(QueryHas("entityId"));

        await _service.GetBacktestingReportAsync(entityId: 3);
        Assert.True(QueryHas("entityId=3"));
        Assert.False(QueryHas("from"));

        await _service.GetBacktestingReportAsync(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));
        Assert.True(QueryHas("from=2026-01-01T00"));
        Assert.True(QueryHas("to=2026-06-30T12"));
        Assert.False(QueryHas("entityId"));
    }

    [Fact]
    public async Task TestAnIncidentBacktestIsReadAndAssessedWithItsBody()
    {
        _backend.OnGet("/Backtesting/Incidents/7", Incident());
        _backend.OnPut("/Backtesting/Incidents/7", Incident());

        var read = await _service.GetIncidentBacktestAsync(7);
        Assert.Equal((7, IncidentKind.NearMiss, 2), (read.IncidentId, read.Kind, read.HiddenRiskCount));
        Assert.Equal("archived", Assert.Single(Assert.Single(read.Risks).DismissalReasons));
        Assert.True(_backend.Sent(Method.Get, "/Backtesting/Incidents/7"));

        var assessed = await _service.AssessIncidentAsync(7,
            new BacktestAssessmentRequest { RiskIds = [3, 4], NoCorrespondingScenario = false, Note = "Matched." });
        Assert.Equal(7, assessed.IncidentId);
        Assert.True(_backend.Sent(Method.Put, "/Backtesting/Incidents/7"));
        Assert.True(BodyHas("\"riskIds\":[3,4]"));
        Assert.True(BodyHas("\"noCorrespondingScenario\":false"));
        Assert.True(BodyHas("\"note\":\"Matched.\""));
    }

    // --- committees ---------------------------------------------------------------------------

    [Fact]
    public async Task TestTheCommitteesAreListedWithTheRetiredOnlyWhenAsked()
    {
        _backend.OnGet("/RiskCommittees", new[] { Committee() });

        var active = await _service.GetCommitteesAsync();
        var committee = Assert.Single(active);
        Assert.Equal((6, 2), (committee.Id, committee.RequiredApprovals));
        Assert.Equal(3, Assert.Single(committee.Members).UserId);
        Assert.True(_backend.Sent(Method.Get, "/RiskCommittees"));
        Assert.False(QueryHas("includeRetired"));

        await _service.GetCommitteesAsync(false);
        Assert.False(QueryHas("includeRetired"));

        await _service.GetCommitteesAsync(true);
        Assert.True(QueryHas("includeRetired=true"));
    }

    [Fact]
    public async Task TestACommitteeIsRead()
    {
        _backend.OnGet("/RiskCommittees/6", Committee());

        var committee = await _service.GetCommitteeAsync(6);

        Assert.Equal(6, committee.Id);
        Assert.True(_backend.Sent(Method.Get, "/RiskCommittees/6"));
    }

    [Fact]
    public async Task TestACommitteeIsConstitutedAndChangedWithItsBody()
    {
        _backend.OnPost("/RiskCommittees", Committee());
        _backend.OnPut("/RiskCommittees/6", Committee());
        var request = new RiskCommitteeRequest { Name = "Risk committee", Mandate = "Accepts.", EntityId = 3, RequiredApprovals = 2 };

        var created = await _service.CreateCommitteeAsync(request);
        Assert.Equal(6, created.Id);
        Assert.True(_backend.Sent(Method.Post, "/RiskCommittees"));
        Assert.True(BodyHas("\"name\":\"Risk committee\""));
        Assert.True(BodyHas("\"mandate\":\"Accepts.\""));
        Assert.True(BodyHas("\"entityId\":3"));
        Assert.True(BodyHas("\"requiredApprovals\":2"));

        var updated = await _service.UpdateCommitteeAsync(6, request);
        Assert.Equal(6, updated.Id);
        Assert.True(_backend.Sent(Method.Put, "/RiskCommittees/6"));
        Assert.True(BodyHas("\"requiredApprovals\":2"));
    }

    [Fact]
    public async Task TestACommitteeIsRetiredWithoutABody()
    {
        _backend.OnPost("/RiskCommittees/6/Retire", Committee());

        var retired = await _service.RetireCommitteeAsync(6);

        Assert.Equal(6, retired.Id);
        Assert.True(_backend.Sent(Method.Post, "/RiskCommittees/6/Retire"));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Body));
    }

    [Fact]
    public async Task TestAMemberIsAddedAndRemovedWithoutABody()
    {
        _backend.OnPut("/RiskCommittees/6/Members/3", Committee());
        _backend.OnStatus(Method.Delete, "/RiskCommittees/6/Members/3", HttpStatusCode.NoContent);

        var added = await _service.AddCommitteeMemberAsync(6, 3);
        Assert.Equal(3, Assert.Single(added.Members).UserId);
        Assert.True(_backend.Sent(Method.Put, "/RiskCommittees/6/Members/3"));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Body));

        await _service.RemoveCommitteeMemberAsync(6, 3);
        Assert.True(_backend.Sent(Method.Delete, "/RiskCommittees/6/Members/3"));
    }

    [Fact]
    public async Task TestTheDecisionsAreListedWithTheirFiltersOnlyWhenGiven()
    {
        _backend.OnGet("/RiskCommittees/Decisions", new[] { Decision() });

        var all = await _service.GetCommitteeDecisionsAsync();
        var first = Assert.Single(all);
        Assert.Equal((8, RiskCommitteeDecisionKind.Renew, 1), (first.Id, first.Kind, first.Approvals));
        Assert.Equal(RiskCommitteeVoteChoice.Reject, Assert.Single(first.Votes).Choice);
        Assert.True(_backend.Sent(Method.Get, "/RiskCommittees/Decisions"));
        Assert.False(QueryHas("committeeId"));
        Assert.False(QueryHas("riskId"));
        Assert.False(QueryHas("openOnly"));

        await _service.GetCommitteeDecisionsAsync(committeeId: 6);
        Assert.True(QueryHas("committeeId=6"));
        Assert.False(QueryHas("riskId"));

        await _service.GetCommitteeDecisionsAsync(riskId: 4);
        Assert.True(QueryHas("riskId=4"));
        Assert.False(QueryHas("committeeId"));

        await _service.GetCommitteeDecisionsAsync(openOnly: true);
        Assert.True(QueryHas("openOnly=true"));

        await _service.GetCommitteeDecisionsAsync(6, 4, true);
        Assert.True(QueryHas("committeeId=6"));
        Assert.True(QueryHas("riskId=4"));
        Assert.True(QueryHas("openOnly=true"));
    }

    [Fact]
    public async Task TestADecisionIsRead()
    {
        _backend.OnGet("/RiskCommittees/Decisions/8", Decision());

        var decision = await _service.GetCommitteeDecisionAsync(8);

        Assert.Equal((8, RiskCommitteeDecisionStatus.Open), (decision.Id, decision.Status));
        Assert.True(_backend.Sent(Method.Get, "/RiskCommittees/Decisions/8"));
    }

    [Fact]
    public async Task TestADecisionIsSubmittedVotedAndWithdrawnWithItsBody()
    {
        _backend.OnPost("/RiskCommittees/6/Decisions", Decision());
        _backend.OnPost("/RiskCommittees/Decisions/8/Votes", Decision());
        _backend.OnPost("/RiskCommittees/Decisions/8/Withdraw", Decision());

        var opened = await _service.OpenCommitteeDecisionAsync(6, new RiskCommitteeDecisionRequest
        {
            RiskId = 4, Kind = RiskCommitteeDecisionKind.Renew, RenewsAcceptanceId = 2, Name = "Renew",
            BusinessJustification = "Cheap.", CompensatingControls = "MFA.", MinutesReference = "ATA-1"
        });
        Assert.Equal(8, opened.Id);
        Assert.True(_backend.Sent(Method.Post, "/RiskCommittees/6/Decisions"));
        Assert.True(BodyHas("\"riskId\":4"));
        Assert.True(BodyHas("\"kind\":2"));
        Assert.True(BodyHas("\"renewsAcceptanceId\":2"));
        Assert.True(BodyHas("\"businessJustification\":\"Cheap.\""));
        Assert.True(BodyHas("\"minutesReference\":\"ATA-1\""));

        var voted = await _service.VoteAsync(8,
            new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Abstain, Comment = "Conflict." });
        Assert.Equal(8, voted.Id);
        Assert.True(_backend.Sent(Method.Post, "/RiskCommittees/Decisions/8/Votes"));
        Assert.True(BodyHas("\"choice\":3"));
        Assert.True(BodyHas("\"comment\":\"Conflict.\""));

        var withdrawn = await _service.WithdrawCommitteeDecisionAsync(8,
            new RiskCommitteeWithdrawRequest { Reason = "No longer needed." });
        Assert.Equal(8, withdrawn.Id);
        Assert.True(_backend.Sent(Method.Post, "/RiskCommittees/Decisions/8/Withdraw"));
        Assert.True(BodyHas("\"reason\":\"No longer needed.\""));
    }

    // --- refusals -----------------------------------------------------------------------------

    [Fact]
    public async Task TestANullRequestIsRefusedBeforeAnyCall()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ArchiveAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ReopenArchiveAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ReviewArchiveAsync(4, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.AssessIncidentAsync(7, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateCommitteeAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateCommitteeAsync(6, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.OpenCommitteeDecisionAsync(6, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.VoteAsync(8, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.WithdrawCommitteeDecisionAsync(8, null!));
        Assert.Empty(_backend.Requests);
    }

    /// <summary>One call per client method, each against its own route, so a refusal is checked on all of them.</summary>
    private static (string Name, Method Method, string Path, Func<IDecisionCycleService, Task> Call)[] Calls() =>
    [
        ("GetArchives", Method.Get, "/RiskArchive/Risks", s => s.GetArchivesAsync()),
        ("GetRiskArchives", Method.Get, "/RiskArchive/Risks/4", s => s.GetRiskArchivesAsync(4)),
        ("Archive", Method.Post, "/RiskArchive/Risks/4", s => s.ArchiveAsync(4, new RiskArchiveRequest())),
        ("ReopenArchive", Method.Post, "/RiskArchive/Risks/4/Reopen", s => s.ReopenArchiveAsync(4, new RiskArchiveReopenRequest())),
        ("ReviewArchive", Method.Post, "/RiskArchive/Risks/4/Reviews", s => s.ReviewArchiveAsync(4, new RiskArchiveReviewRequest())),
        ("GetReport", Method.Get, "/Backtesting", s => s.GetBacktestingReportAsync()),
        ("GetIncidentBacktest", Method.Get, "/Backtesting/Incidents/7", s => s.GetIncidentBacktestAsync(7)),
        ("AssessIncident", Method.Put, "/Backtesting/Incidents/7", s => s.AssessIncidentAsync(7, new BacktestAssessmentRequest())),
        ("GetCommittees", Method.Get, "/RiskCommittees", s => s.GetCommitteesAsync()),
        ("GetCommittee", Method.Get, "/RiskCommittees/6", s => s.GetCommitteeAsync(6)),
        ("CreateCommittee", Method.Post, "/RiskCommittees", s => s.CreateCommitteeAsync(new RiskCommitteeRequest())),
        ("UpdateCommittee", Method.Put, "/RiskCommittees/6", s => s.UpdateCommitteeAsync(6, new RiskCommitteeRequest())),
        ("RetireCommittee", Method.Post, "/RiskCommittees/6/Retire", s => s.RetireCommitteeAsync(6)),
        ("AddMember", Method.Put, "/RiskCommittees/6/Members/3", s => s.AddCommitteeMemberAsync(6, 3)),
        ("RemoveMember", Method.Delete, "/RiskCommittees/6/Members/3", s => s.RemoveCommitteeMemberAsync(6, 3)),
        ("GetDecisions", Method.Get, "/RiskCommittees/Decisions", s => s.GetCommitteeDecisionsAsync()),
        ("GetDecision", Method.Get, "/RiskCommittees/Decisions/8", s => s.GetCommitteeDecisionAsync(8)),
        ("OpenDecision", Method.Post, "/RiskCommittees/6/Decisions", s => s.OpenCommitteeDecisionAsync(6, new RiskCommitteeDecisionRequest())),
        ("Vote", Method.Post, "/RiskCommittees/Decisions/8/Votes", s => s.VoteAsync(8, new RiskCommitteeVoteRequest())),
        ("Withdraw", Method.Post, "/RiskCommittees/Decisions/8/Withdraw", s => s.WithdrawCommitteeDecisionAsync(8, new RiskCommitteeWithdrawRequest()))
    ];

    [Fact]
    public void TestEveryClientMethodIsCoveredByTheRefusalChecks()
    {
        // The interface has 20 methods (5 archive, 3 backtesting, 12 committee); Calls() has one entry for each.
        var methods = typeof(IDecisionCycleService).GetMethods().Select(m => m.Name).Distinct().Count();
        Assert.Equal(20, methods);
        Assert.Equal(methods, Calls().Length);
    }

    [Fact]
    public async Task TestA404IsNotFoundOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.NotFound);

        foreach (var (name, _, _, call) in Calls())
            await Assert.ThrowsAsync<DataNotFoundException>(() => call(_service));
    }

    /// <summary>A closed archive, a vote already cast, a recused member and a validation error reach the person with the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "The risk is already closed.")]
    [InlineData(HttpStatusCode.Conflict, "The vote was already cast.")]
    [InlineData(HttpStatusCode.BadRequest, "A justification is required.")]
    [InlineData(HttpStatusCode.Forbidden, "You are not a member of this committee.")]
    public async Task TestARefusalKeepsTheServersSentenceOnEveryMethod(HttpStatusCode status, string sentence)
    {
        foreach (var (_, method, path, _) in Calls())
            _backend.On(method, path, new { error = "refused", message = sentence }, status);

        foreach (var (name, _, _, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains(sentence), name);
        }
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailureOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.InternalServerError);

        foreach (var (name, _, path, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains($"Error calling {path}"), name);
        }
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsAFailureNotANull()
    {
        _backend.OnStatus(Method.Get, "/RiskCommittees/6", HttpStatusCode.OK);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetCommitteeAsync(6));
        Assert.Contains("empty body", thrown.Message);
    }
}
