using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Governance;
using Xunit;
using ApiMocks = API.Tests.Mock;

namespace API.Tests.APITests;

/// <summary>
/// The HTTP contract of the Track 8 governance controllers.
///
/// What a controller test is for here is which domain exception becomes which status code, because
/// that mapping is the whole of the controllers' logic — the rules themselves are in the services and
/// are tested there. The refusals matter more than the successes: a business reviewer who is told
/// "500" when the real answer is "this risk is above appetite" cannot act on it.
/// </summary>
[TestSubject(typeof(RiskGovernanceController))]
public class Track8ControllersTest : BaseControllerTest
{
    private readonly RiskGovernanceController _governance;
    private readonly RiskAppetitesController _appetites;
    private readonly MitigationTasksController _tasks;
    private readonly EntityRiskReviewersController _reviewers;
    private readonly RiskReviewCampaignsController _campaigns;
    private readonly AuditTrailController _auditTrail;

    public Track8ControllersTest()
    {
        _governance = _serviceProvider.GetRequiredService<RiskGovernanceController>();
        _appetites = _serviceProvider.GetRequiredService<RiskAppetitesController>();
        _tasks = _serviceProvider.GetRequiredService<MitigationTasksController>();
        _reviewers = _serviceProvider.GetRequiredService<EntityRiskReviewersController>();
        _campaigns = _serviceProvider.GetRequiredService<RiskReviewCampaignsController>();
        _auditTrail = _serviceProvider.GetRequiredService<AuditTrailController>();
    }

    private static RiskAcceptanceRequest ValidAcceptance() => new()
    {
        Name = "Q3 exception",
        BusinessJustification = "The vendor patch breaks the payment integration.",
        ExpiresAt = DateTime.UtcNow.AddDays(90)
    };

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // --- 8.1 acceptance -------------------------------------------------------------------------

    [Fact]
    public async Task TestCreatingAnAcceptanceReturnsCreated()
    {
        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.AcceptableRiskId, ValidAcceptance());

        var created = Assert.IsType<CreatedResult>(result.Result);
        var acceptance = Assert.IsType<RiskAcceptance>(created.Value);
        Assert.Equal(ApiMocks.MockedRiskAcceptancesService.AcceptableRiskId, acceptance.RiskId);
    }

    [Fact]
    public async Task TestAnAcceptanceWithoutAJustificationIsABadRequestNamingTheField()
    {
        var request = ValidAcceptance();
        request.BusinessJustification = null;

        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.AcceptableRiskId, request);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(nameof(request.BusinessJustification), Json(bad.Value));
    }

    [Fact]
    public async Task TestANullBodyIsABadRequest()
    {
        var result = await _governance.CreateAcceptance(1, null!);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>
    /// Segregation of duties is 422, not 403: the caller is allowed to accept risks in general, and
    /// it is their relationship to *this* risk that makes the request impossible. A 403 would send
    /// them to ask for a permission that would not help.
    /// </summary>
    [Fact]
    public async Task TestAcceptingYourOwnRiskIsUnprocessableAndNamesTheRule()
    {
        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.OwnRiskId, ValidAcceptance());

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Contains("segregation_of_duties", Json(unprocessable.Value));
    }

    [Fact]
    public async Task TestAcceptingAboveTheAppetiteCeilingIsUnprocessable()
    {
        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.AboveCeilingRiskId, ValidAcceptance());

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Contains("risk_appetite_ceiling", Json(unprocessable.Value));
    }

    [Fact]
    public async Task TestASecondLiveAcceptanceIsAConflict()
    {
        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.AlreadyAcceptedRiskId, ValidAcceptance());

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestInsufficientBandAuthorityIsForbidden()
    {
        var result = await _governance.CreateAcceptance(
            ApiMocks.MockedRiskAcceptancesService.OutOfBandRiskId, ValidAcceptance());

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task TestListingAcceptancesOfAnUnknownRiskIsNotFound()
    {
        var result = await _governance.GetAcceptances(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestTheActiveAcceptanceIsNoContentWhenTheRiskIsNotAccepted()
    {
        var result = await _governance.GetActiveAcceptance(
            ApiMocks.MockedRiskAcceptancesService.AcceptableRiskId);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task TestRevokingWithoutAReasonIsABadRequest()
    {
        var result = await _governance.RevokeAcceptance(1,
            ApiMocks.MockedRiskAcceptancesService.KnownAcceptanceId, new RiskAcceptanceRevocation());

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestRevokingReturnsTheRevokedRow()
    {
        var result = await _governance.RevokeAcceptance(1,
            ApiMocks.MockedRiskAcceptancesService.KnownAcceptanceId,
            new RiskAcceptanceRevocation { Reason = "The compensating control was removed." });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var acceptance = Assert.IsType<RiskAcceptance>(ok.Value);
        Assert.Equal(RiskAcceptanceStatus.Revoked, acceptance.Status);
    }

    [Fact]
    public async Task TestRenewingARevokedAcceptanceIsUnprocessable()
    {
        var result = await _governance.RenewAcceptance(1, 998, ValidAcceptance());

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
    }

    // --- 8.3 appetite and counter-signature ------------------------------------------------------

    [Fact]
    public async Task TestAppetiteEvaluationIsReturned()
    {
        var result = await _governance.GetAppetite(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var evaluation = Assert.IsType<AppetiteEvaluation>(ok.Value);
        Assert.True(evaluation.AppetiteConfigured);
    }

    [Fact]
    public async Task TestAppetiteEvaluationOfAnUnknownRiskIsNotFound()
    {
        var result = await _governance.GetAppetite(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestRisksAboveAppetiteAreListed()
    {
        var result = await _governance.GetAboveAppetite();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var counts = Assert.IsType<List<AppetiteBreachCount>>(ok.Value);
        Assert.Single(counts);
    }

    [Fact]
    public async Task TestLegacyWorkflowViolationsAreListed()
    {
        var result = await _governance.GetWorkflowViolations();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsType<List<ServerServices.Interfaces.WorkflowViolation>>(ok.Value));
    }

    // --- 8.4 audit trail -------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRiskAuditTrailIsReturned()
    {
        var result = await _governance.GetAuditTrail(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsType<List<AuditLog>>(ok.Value));
    }

    /// <summary>
    /// An unaudited type is a bad request naming the audited set, not an empty list. An empty list
    /// would read as "nothing has ever changed", which is the opposite of "this is not audited".
    /// </summary>
    [Fact]
    public async Task TestAnUnauditedEntityTypeIsARequestErrorNotAnEmptyList()
    {
        var result = await _auditTrail.GetForRecord("Vulnerability", 1);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("not_audited", Json(bad.Value));
    }

    [Fact]
    public async Task TestAnAuditedEntityTypeIsAccepted()
    {
        var result = await _auditTrail.GetForRecord(nameof(Risk), 1);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public void TestTheAuditedScopeIsDiscoverable()
    {
        var result = _auditTrail.GetScope();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Contains(nameof(Risk), (IEnumerable<string>)ok.Value!);
    }

    [Fact]
    public async Task TestAnInvertedEvidencePeriodIsABadRequest()
    {
        var result = await _auditTrail.GetEvidence(1,
            from: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestTheEvidenceReportComesBackAsACsvFile()
    {
        var result = await _auditTrail.GetEvidenceReport(1);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Contains("occurred_at_utc", System.Text.Encoding.UTF8.GetString(file.FileContents));
    }

    // --- 8.5 triage and tasks --------------------------------------------------------------------

    [Fact]
    public async Task TestPromotingAPendingRiskReturnsCreated()
    {
        var result = await _governance.PromotePending(1, new PendingRiskPromotion { Subject = "Promoted" });

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestDismissingAPendingRiskWithoutAReasonIsABadRequest()
    {
        var result = await _governance.DismissPending(1, new PendingRiskDismissal());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TestCreatingATaskWithoutATitleIsABadRequest()
    {
        var result = await _tasks.Create(new MitigationTaskRequest { MitigationId = 1 });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Title", Json(bad.Value));
    }

    [Fact]
    public async Task TestCreatingATaskReturnsCreated()
    {
        var result = await _tasks.Create(new MitigationTaskRequest
            { MitigationId = 1, Title = "Rotate the account" });

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestUpdatingATaskWithAMismatchedIdIsABadRequest()
    {
        var result = await _tasks.Update(1, new MitigationTaskRequest { Id = 2, MitigationId = 1 });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestUpdatingAnUnknownTaskIsNotFound()
    {
        var result = await _tasks.Update(999, new MitigationTaskRequest { Id = 999, MitigationId = 1 });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestDeletingAnUnknownTaskIsNotFound()
    {
        Assert.IsType<NotFoundResult>(await _tasks.Delete(999));
    }

    // --- 8.3.3 appetite administration -----------------------------------------------------------

    [Fact]
    public async Task TestATresholdAboveTheCeilingIsRejected()
    {
        var result = await _appetites.Save(new RiskAppetite
            { MaxAcceptableResidual = 4, DualApprovalThreshold = 8 });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("DualApprovalThreshold", Json(bad.Value));
    }

    [Fact]
    public async Task TestASecondGlobalAppetiteIsAConflict()
    {
        var result = await _appetites.Save(new RiskAppetite
        {
            EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
            Notes = "duplicate-global"
        });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestSavingAnAppetiteForAnUnknownEntityIsNotFound()
    {
        var result = await _appetites.Save(new RiskAppetite
            { EntityId = 999, MaxAcceptableResidual = 6, DualApprovalThreshold = 4 });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestTheGlobalAppetiteIsReturned()
    {
        var result = await _appetites.GetGlobal();

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestDeletingAnUnknownAppetiteIsNotFound()
    {
        Assert.IsType<NotFoundResult>(await _appetites.Delete(999));
    }

    // --- 8.6 portal -------------------------------------------------------------------------------

    [Fact]
    public async Task TestAppointingAReviewerReturnsCreated()
    {
        var result = await _reviewers.Appoint(new ReviewerAppointment
            { EntityId = 1, UserId = 2, IsPrimary = true });

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task TestAppointingADisabledAccountIsABadRequest()
    {
        var result = await _reviewers.Appoint(new ReviewerAppointment { EntityId = 1, UserId = 998 });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAppointingToAnUnknownEntityIsNotFound()
    {
        var result = await _reviewers.Appoint(new ReviewerAppointment { EntityId = 999, UserId = 2 });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestMyCampaignsAreListed()
    {
        var result = await _campaigns.MineAsync();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsType<List<RiskReviewCampaign>>(ok.Value));
    }

    /// <summary>
    /// Reading a campaign belonging to an entity the caller is not appointed to is refused, and the
    /// refusal names the reason so the portal can say something useful.
    ///
    /// The shared fixture user is an administrator, who may reach any campaign so that somebody can
    /// unblock one whose reviewer has left — the segregation-of-duties rule still governs the
    /// decision itself. So this test supplies a plain business reviewer instead.
    /// </summary>
    [Fact]
    public async Task TestACampaignOfAnUnappointedEntityIsForbidden()
    {
        var controller = ResolveController<RiskReviewCampaignsController>(services =>
            services.AddSingleton(ApiMocks.MockedNonAdminUsersService.Build()));

        var result = await controller.Get(ApiMocks.MockedRiskReviewCampaignsService.ForeignCampaignId);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
        Assert.Contains("not_appointed", Json(status.Value));
    }

    [Fact]
    public async Task TestACampaignOfAnAppointedEntityIsReturned()
    {
        var result = await _campaigns.Get(ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAnUnknownCampaignIsNotFound()
    {
        var result = await _campaigns.Get(999);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestAnEmptyRankingIsABadRequest()
    {
        var result = await _campaigns.SaveRanking(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId, new CampaignRankingRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TestARankingWithUnknownItemsIsABadRequest()
    {
        var result = await _campaigns.SaveRanking(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId,
            new CampaignRankingRequest { OrderedItemIds = [999] });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TestARankingIsAccepted()
    {
        var result = await _campaigns.SaveRanking(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId,
            new CampaignRankingRequest { OrderedItemIds = [10] });

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task TestADecisionIsRecorded()
    {
        var result = await _campaigns.Decide(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId,
            ApiMocks.MockedRiskReviewCampaignsService.KnownItemId,
            new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
            });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var item = Assert.IsType<RiskReviewCampaignItem>(ok.Value);
        Assert.Equal(RiskReviewDecision.Escalated, item.Decision);
    }

    [Fact]
    public async Task TestADecisionOnAnUnappointedEntitysCampaignIsForbidden()
    {
        var controller = ResolveController<RiskReviewCampaignsController>(services =>
            services.AddSingleton(ApiMocks.MockedNonAdminUsersService.Build()));

        var result = await controller.Decide(
            ApiMocks.MockedRiskReviewCampaignsService.ForeignCampaignId, 10,
            new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
            });

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task TestAnEscalationWithoutAnApproverIsABadRequest()
    {
        var result = await _campaigns.Decide(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId, 10,
            new CampaignDecisionRequest { Decision = RiskReviewDecision.Escalated });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestADecisionAboveTheCeilingIsUnprocessableAndExplainsWhy()
    {
        var result = await _campaigns.Decide(
            ApiMocks.MockedRiskReviewCampaignsService.KnownCampaignId, 10,
            new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Accepted,
                Notes = "over-ceiling",
                Acceptance = ValidAcceptance()
            });

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Contains("risk_appetite_ceiling", Json(unprocessable.Value));
    }

    [Fact]
    public async Task TestCampaignStatisticsAreReturned()
    {
        var result = await _campaigns.Statistics();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsType<List<CampaignStatistics>>(ok.Value));
    }

    // --- 8.7 quantitative -------------------------------------------------------------------------

    [Fact]
    public async Task TestQuantitativeResultIsNoContentWhenNeverScored()
    {
        var result = await _governance.GetQuantitative(
            ApiMocks.MockedQuantitativeRiskService.UnscoredRiskId);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task TestQuantitativeScoringReturnsTheResult()
    {
        var result = await _governance.ComputeQuantitative(1, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 0.5, LossEventFrequencyMostLikely = 1, LossEventFrequencyMax = 2,
            LossMagnitudeMin = 1000, LossMagnitudeMostLikely = 5000, LossMagnitudeMax = 20000
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var quantified = Assert.IsType<QuantitativeRiskResult>(ok.Value);
        Assert.True(quantified.MappedScore > 0);
    }

    [Fact]
    public async Task TestAnUnorderedRangeIsABadRequest()
    {
        var result = await _governance.ComputeQuantitative(1, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = 5, LossEventFrequencyMostLikely = 1, LossEventFrequencyMax = 2,
            LossMagnitudeMin = 1000, LossMagnitudeMostLikely = 5000, LossMagnitudeMax = 20000
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestNullQuantitativeInputIsABadRequest()
    {
        var result = await _governance.ComputeQuantitative(1, null!);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
