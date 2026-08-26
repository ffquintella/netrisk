using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="RiskGovernanceRestService"/> over <see cref="StubRestBackend"/>, so every URL it
/// builds and every status branch runs for real.
///
/// The property this client has to hold, and the reason several tests below assert on message text:
/// a 422 from the governance endpoints carries a sentence written to be read by a person — "Residual
/// 9.10 is above the acceptance ceiling of 6.00", "You cannot accept this risk because you own it".
/// A client that swallowed that body and threw a generic failure would turn a refusal the user can
/// act on into one they cannot.
/// </summary>
[TestSubject(typeof(RiskGovernanceRestService))]
public class RiskGovernanceRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRiskGovernanceService _service;

    public RiskGovernanceRestServiceTest()
    {
        _service = ResolveWith<IRiskGovernanceService>(_backend);
    }

    private static RiskAcceptance Acceptance(int id = 1, int riskId = 1) => new()
    {
        Id = id, RiskId = riskId, Name = "Q3 exception", AuthorizingManagerId = 2,
        BusinessJustification = "Compensating monitoring is in place.",
        StartDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = RiskAcceptanceStatus.Active
    };

    // --- 8.1 acceptance ---------------------------------------------------------------------

    [Fact]
    public async Task AcceptancesAreReadFromTheRiskSubResource()
    {
        _backend.OnGet("/Risks/7/Acceptances", new List<RiskAcceptance> { Acceptance(riskId: 7) });

        var acceptances = await _service.GetAcceptancesAsync(7);

        Assert.Equal(7, Assert.Single(acceptances).RiskId);
        Assert.True(_backend.Sent(Method.Get, "/Risks/7/Acceptances"));
    }

    /// <summary>
    /// "This risk is not accepted" is 204, and null is the honest translation. Throwing would make
    /// the risk editor treat a perfectly ordinary state as a failure.
    /// </summary>
    [Fact]
    public async Task NoActiveAcceptanceIsNullRatherThanAnError()
    {
        _backend.OnStatus(Method.Get, "/Risks/7/Acceptances/Active", HttpStatusCode.NoContent);

        Assert.Null(await _service.GetActiveAcceptanceAsync(7));
    }

    [Fact]
    public async Task AnActiveAcceptanceIsReturned()
    {
        _backend.OnGet("/Risks/7/Acceptances/Active", Acceptance(riskId: 7));

        var active = await _service.GetActiveAcceptanceAsync(7);

        Assert.NotNull(active);
        Assert.Equal(RiskAcceptanceStatus.Active, active!.Status);
    }

    [Fact]
    public async Task CreatingAnAcceptancePostsTheRequestAndReturnsTheRow()
    {
        _backend.On(Method.Post, "/Risks/7/Acceptances", Acceptance(riskId: 7), HttpStatusCode.Created);

        var created = await _service.CreateAcceptanceAsync(7, new RiskAcceptanceRequest
        {
            BusinessJustification = "Compensating monitoring is in place.",
            ExpiresAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.Equal(7, created.RiskId);
        Assert.Contains("Compensating monitoring", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task AnAppetiteRefusalKeepsTheServersExplanation()
    {
        _backend.On(Method.Post, "/Risks/7/Acceptances",
            new { error = "risk_appetite_ceiling", message = "Residual 9.10 is above the acceptance ceiling of 6.00." },
            HttpStatusCode.UnprocessableEntity);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.CreateAcceptanceAsync(7, new RiskAcceptanceRequest
            {
                BusinessJustification = "It is fine.", ExpiresAt = DateTime.UtcNow.AddDays(30)
            }));

        Assert.Contains("above the acceptance ceiling", ex.Message);
    }

    [Fact]
    public async Task ASegregationRefusalKeepsTheServersExplanation()
    {
        _backend.On(Method.Post, "/Risks/7/Acceptances",
            new { error = "segregation_of_duties", message = "You cannot accept this risk because you own it." },
            HttpStatusCode.UnprocessableEntity);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.CreateAcceptanceAsync(7, new RiskAcceptanceRequest
            {
                BusinessJustification = "It is fine.", ExpiresAt = DateTime.UtcNow.AddDays(30)
            }));

        Assert.Contains("because you own it", ex.Message);
    }

    [Fact]
    public async Task AConflictOnASecondAcceptanceIsReported()
    {
        _backend.On(Method.Post, "/Risks/7/Acceptances",
            new { error = "already_accepted" }, HttpStatusCode.Conflict);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.CreateAcceptanceAsync(7, new RiskAcceptanceRequest
            {
                BusinessJustification = "x", ExpiresAt = DateTime.UtcNow.AddDays(30)
            }));
    }

    [Fact]
    public async Task RevokingPutsTheReasonOnTheRevokeRoute()
    {
        var revoked = Acceptance(riskId: 7);
        revoked.Status = RiskAcceptanceStatus.Revoked;

        _backend.OnPut("/Risks/7/Acceptances/1/Revoke", revoked);

        var result = await _service.RevokeAcceptanceAsync(7, 1, "The control was removed.");

        Assert.Equal(RiskAcceptanceStatus.Revoked, result.Status);
        Assert.Contains("The control was removed.", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task RenewingPostsToTheRenewRoute()
    {
        _backend.OnPost("/Risks/7/Acceptances/1/Renew", Acceptance(2, 7));

        var renewal = await _service.RenewAcceptanceAsync(7, 1, new RiskAcceptanceRequest
        {
            BusinessJustification = "Still needed.", ExpiresAt = DateTime.UtcNow.AddDays(120)
        });

        Assert.Equal(2, renewal.Id);
        Assert.True(_backend.Sent(Method.Post, "/Risks/7/Acceptances/1/Renew"));
    }

    [Fact]
    public async Task ExpiringAcceptancesCarryTheWindowAsAQueryParameter()
    {
        _backend.OnGet("/RiskAcceptances/Expiring", new List<RiskAcceptance> { Acceptance() });

        await _service.GetExpiringAcceptancesAsync(45);

        Assert.Contains("days=45", _backend.LastRequest!.Query);
    }

    [Fact]
    public async Task AnUnknownRiskIsReportedAsNotFound()
    {
        _backend.OnStatus(Method.Get, "/Risks/999/Appetite", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetAppetiteEvaluationAsync(999));
    }

    // --- 8.2 both scores --------------------------------------------------------------------

    [Fact]
    public async Task ScorePairsAreReadWithTheRequestedIds()
    {
        _backend.OnGet("/Risks/Scores", new List<RiskScorePair>
        {
            new() { RiskId = 1, Inherent = 8f, Residual = 3f }
        });

        var pairs = await _service.GetScorePairsAsync([1, 2]);

        Assert.Equal(5f, Assert.Single(pairs).Delta!.Value, 3);
        Assert.Contains("ids=1", _backend.LastRequest!.Query);
        Assert.Contains("ids=2", _backend.LastRequest!.Query);
    }

    // --- 8.3 appetite -----------------------------------------------------------------------

    [Fact]
    public async Task NoGlobalAppetiteIsNullRatherThanAnError()
    {
        _backend.OnStatus(Method.Get, "/RiskAppetites/Global", HttpStatusCode.NoContent);

        // "Nothing is gated" is a real, seeded state, and the admin screen renders it as such.
        Assert.Null(await _service.GetGlobalAppetiteAsync());
    }

    [Fact]
    public async Task SavingAnAppetitePostsIt()
    {
        _backend.OnPost("/RiskAppetites", new RiskAppetite
            { Id = 1, MaxAcceptableResidual = 6, DualApprovalThreshold = 4 });

        var saved = await _service.SaveAppetiteAsync(new RiskAppetite
            { MaxAcceptableResidual = 6, DualApprovalThreshold = 4 });

        Assert.Equal(1, saved.Id);
    }

    [Fact]
    public async Task AnInvalidThresholdKeepsTheServersExplanation()
    {
        _backend.On(Method.Post, "/RiskAppetites",
            new { error = "invalid_parameter", message = "The dual-approval threshold has to be at or below the acceptance ceiling." },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveAppetiteAsync(new RiskAppetite
                { MaxAcceptableResidual = 4, DualApprovalThreshold = 8 }));

        Assert.Contains("at or below the acceptance ceiling", ex.Message);
    }

    [Fact]
    public async Task DeletingAnUnknownAppetiteIsNotFound()
    {
        _backend.OnStatus(Method.Delete, "/RiskAppetites/999", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeleteAppetiteAsync(999));
    }

    [Fact]
    public async Task RisksAboveAppetiteAreRead()
    {
        _backend.OnGet("/Risks/AboveAppetite", new List<AppetiteBreachCount>
        {
            new() { EntityId = 1, EntityName = "Head office", Count = 3 }
        });

        Assert.Equal(3, Assert.Single(await _service.GetRisksAboveAppetiteAsync()).Count);
    }

    [Fact]
    public async Task CounterSigningPostsToTheReviewRoute()
    {
        _backend.OnPost("/Risks/7/MgmtReviews/5/Countersign", new MgmtReview
            { Id = 5, RiskId = 7, Comments = "", SecondReviewerId = 9 });

        var review = await _service.CountersignAsync(7, 5);

        Assert.Equal(9, review.SecondReviewerId);
    }

    // --- 8.5 tasks and triage ---------------------------------------------------------------

    [Fact]
    public async Task PendingRisksCarryTheStatusFilter()
    {
        _backend.OnGet("/Risks/Pending", new List<PendingRiskListing>
        {
            new() { Id = 1, Subject = "Shared credentials", Status = PendingRiskStatus.Pending }
        });

        var pending = await _service.GetPendingRisksAsync();

        Assert.Equal("Shared credentials", Assert.Single(pending).Subject);
        Assert.Contains($"status={(int)PendingRiskStatus.Pending}", _backend.LastRequest!.Query);
    }

    [Fact]
    public async Task PromotingAPendingRiskPostsTheEdits()
    {
        _backend.On(Method.Post, "/Risks/Pending/1/Promote",
            new Risk { Id = 42, Subject = "Promoted", ReferenceId = "ASMT-3-4", Status = "New" },
            HttpStatusCode.Created);

        var risk = await _service.PromotePendingRiskAsync(1,
            new PendingRiskPromotion { Subject = "Promoted", OwnerId = 3 });

        Assert.Equal(42, risk.Id);
        Assert.Contains("Promoted", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task DismissingAPendingRiskSendsTheReason()
    {
        _backend.OnPost("/Risks/Pending/1/Dismiss", new { });

        await _service.DismissPendingRiskAsync(1, "Duplicate of R-17.");

        Assert.Contains("Duplicate of R-17.", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task DismissingWithoutAReasonSurfacesTheServersRefusal()
    {
        _backend.On(Method.Post, "/Risks/Pending/1/Dismiss",
            new { error = "invalid_parameter", message = "Dismissing a pending risk needs a reason." },
            HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.DismissPendingRiskAsync(1, ""));

        Assert.Contains("needs a reason", ex.Message);
    }

    [Fact]
    public async Task TasksOfARiskAreRead()
    {
        _backend.OnGet("/Risks/7/MitigationTasks", new List<MitigationTask>
        {
            new() { Id = 1, MitigationId = 2, Title = "Rotate the account", Status = MitigationTaskStatus.Open }
        });

        Assert.Equal("Rotate the account", Assert.Single(await _service.GetTasksByRiskAsync(7)).Title);
    }

    [Fact]
    public async Task CreatingATaskPostsIt()
    {
        _backend.On(Method.Post, "/MitigationTasks",
            new MitigationTask { Id = 5, MitigationId = 2, Title = "Rebuild" }, HttpStatusCode.Created);

        var created = await _service.CreateTaskAsync(new MitigationTaskRequest
            { MitigationId = 2, Title = "Rebuild" });

        Assert.Equal(5, created.Id);
    }

    [Fact]
    public async Task UpdatingATaskPutsToItsOwnRoute()
    {
        _backend.OnPut("/MitigationTasks/5", new MitigationTask
            { Id = 5, MitigationId = 2, Title = "Rebuild", Status = MitigationTaskStatus.Completed });

        var updated = await _service.UpdateTaskAsync(new MitigationTaskRequest
            { Id = 5, MitigationId = 2, Title = "Rebuild", Status = MitigationTaskStatus.Completed });

        Assert.Equal(MitigationTaskStatus.Completed, updated.Status);
        Assert.True(_backend.Sent(Method.Put, "/MitigationTasks/5"));
    }

    [Fact]
    public async Task RequestingAReviewPostsTheReason()
    {
        _backend.OnPost("/Risks/7/RequestReview", new { flagged = true });

        await _service.RequestReviewAsync(7, "A new Critical vulnerability was linked.");

        Assert.Contains("Critical vulnerability", _backend.LastRequest!.Body);
    }

    // --- 8.6 reviewers ----------------------------------------------------------------------

    [Fact]
    public async Task AppointingAReviewerPostsTheAppointment()
    {
        _backend.On(Method.Post, "/EntityRiskReviewers",
            new EntityRiskReviewer { Id = 1, EntityId = 4, UserId = 9, IsPrimary = true },
            HttpStatusCode.Created);

        var appointment = await _service.AppointReviewerAsync(4, 9, true);

        Assert.True(appointment.IsPrimary);
    }

    [Fact]
    public async Task CampaignStatisticsCarryTheEntityFilter()
    {
        _backend.OnGet("/RiskReviewCampaigns/Statistics", new List<CampaignStatistics>
        {
            new() { CampaignId = 1, EntityId = 4, EntityName = "Head office", TotalItems = 5 }
        });

        await _service.GetCampaignStatisticsAsync(4);

        Assert.Contains("entityId=4", _backend.LastRequest!.Query);
    }

    // --- 8.7 quantitative -------------------------------------------------------------------

    [Fact]
    public async Task ARiskNeverScoredQuantitativelyIsNull()
    {
        _backend.OnStatus(Method.Get, "/Risks/7/Quantitative", HttpStatusCode.NoContent);

        Assert.Null(await _service.GetQuantitativeAsync(7));
    }

    [Fact]
    public async Task ComputingQuantitativeScoringPostsTheRanges()
    {
        _backend.OnPost("/Risks/7/Quantitative", new QuantitativeRiskResult
        {
            RiskId = 7, InherentP50 = 45000, InherentP90 = 320000, MappedScore = 5.2f,
            MappedRiskLevel = "Medium"
        });

        var result = await _service.ComputeQuantitativeAsync(7, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 0.5, LossEventFrequencyMostLikely = 1, LossEventFrequencyMax = 2,
            LossMagnitudeMin = 10000, LossMagnitudeMostLikely = 50000, LossMagnitudeMax = 500000
        });

        Assert.Equal("Medium", result.MappedRiskLevel);
        Assert.Contains("lossEventFrequencyMostLikely", _backend.LastRequest!.Body,
            StringComparison.OrdinalIgnoreCase);
    }

    // --- transport --------------------------------------------------------------------------

    [Fact]
    public async Task ATransportFailureIsReportedAsACommunicationProblem()
    {
        _backend.OnTransportFailure(Method.Get, "/Risks/7/Acceptances");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAcceptancesAsync(7));
    }

    [Fact]
    public async Task AnEmptyListIsNotAnError()
    {
        _backend.OnStatus(Method.Get, "/Risks/7/Acceptances", HttpStatusCode.NoContent);

        Assert.Empty(await _service.GetAcceptancesAsync(7));
    }
}
