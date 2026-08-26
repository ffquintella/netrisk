using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestone 8.6 — reviewer appointments and the periodic review campaigns the portal drives.
///
/// The invariant worth guarding is that a portal decision materializes as the same first-class
/// records a desktop decision does: an 8.1 acceptance, 8.5.3 tasks, a <c>MgmtReview</c>. If the portal
/// wrote its own parallel history, the two surfaces would disagree about what was decided.
/// </summary>
[TestSubject(typeof(RiskReviewCampaignsService))]
public class RiskReviewPortalInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskReviewCampaignsService _campaigns;
    private readonly IEntityRiskReviewersService _reviewers;

    public RiskReviewPortalInMemoryTest()
    {
        _campaigns = GetService<IRiskReviewCampaignsService>();
        _reviewers = GetService<IEntityRiskReviewersService>();
    }

    private static User NewUser(int id, string name, bool enabled = true, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = enabled, Admin = admin,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "organization", DefinitionVersion = "1", Status = "active",
        Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Updated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Risk NewRisk(int id, int entityId, int? owner = null, int? submittedBy = null) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EntityId = entityId, Owner = owner, SubmittedBy = submittedBy
    };

    /// <summary>Entity 1 with two open risks and one appointed reviewer who is unrelated to them.</summary>
    private void SeedEntityWithRisks()
    {
        Seed(ctx =>
        {
            // The reviewer holds the band permission the acceptance path checks. A business reviewer
            // in a real installation is granted exactly this and nothing else, which is the point of
            // the separate `business_risk_review` permission.
            var reviewPermission = new Permission
            {
                Id = 22, Key = "review_insignificant", Name = "Able to Review Insignificant Risks",
                Description = "seeded for the test", Order = 7
            };
            ctx.Permissions.Add(reviewPermission);

            var reviewer = NewUser(1, "reviewer");
            reviewer.Permissions.Add(reviewPermission);

            ctx.Users.Add(reviewer);
            ctx.Users.Add(NewUser(2, "owner"));
            ctx.Users.Add(NewUser(3, "senior"));
            ctx.Entities.Add(NewEntity(1));

            ctx.Risks.Add(NewRisk(1, 1, owner: 2, submittedBy: 2));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 8f, ResidualRisk = 3f,
                  ClassicImpact = 3, ClassicLikelihood = 3 });

            ctx.Risks.Add(NewRisk(2, 1, owner: 2, submittedBy: 2));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 2, ScoringMethod = 1, CalculatedRisk = 4f, ResidualRisk = 1f,
                  ClassicImpact = 2, ClassicLikelihood = 2 });

            ctx.Settings.Add(new Setting
                { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
            ctx.Settings.Add(new Setting
                { Name = RiskWorkflowService.BreakGlassSetting, Value = "false" });

            ctx.MitigationEfforts.Add(new MitigationEffort { Value = 1, Name = "Low" });
            ctx.MitigationCosts.Add(new MitigationCost { Value = 1, Name = "Low" });
            ctx.PlanningStrategies.Add(new PlanningStrategy { Value = 1, Name = "Mitigate" });
        });
    }

    // --- 8.6.2 reviewer appointments -----------------------------------------------------------------

    [Fact]
    public async Task TestAppointingIsIdempotentPerEntityAndUser()
    {
        SeedEntityWithRisks();

        await _reviewers.AppointAsync(1, 1, isPrimary: false, actingUserId: 3);
        await _reviewers.AppointAsync(1, 1, isPrimary: true, actingUserId: 3);

        var appointed = await _reviewers.GetByEntityAsync(1);
        var single = Assert.Single(appointed);
        Assert.True(single.IsPrimary);
    }

    /// <summary>
    /// At most one primary per entity. Two would make "who gets chased when the campaign is overdue"
    /// a judgement call, which is the thing the flag exists to remove.
    /// </summary>
    [Fact]
    public async Task TestAppointingANewPrimaryDemotesTheIncumbent()
    {
        SeedEntityWithRisks();

        await _reviewers.AppointAsync(1, 1, isPrimary: true, actingUserId: 3);
        await _reviewers.AppointAsync(1, 3, isPrimary: true, actingUserId: 3);

        var appointed = await _reviewers.GetByEntityAsync(1);

        Assert.Equal(2, appointed.Count);
        Assert.Single(appointed, r => r.IsPrimary);
        Assert.True(appointed.Single(r => r.UserId == 3).IsPrimary);
    }

    [Fact]
    public async Task TestADisabledAccountCannotBeAppointed()
    {
        SeedEntityWithRisks();
        Seed(ctx => ctx.Users.Add(NewUser(9, "left", enabled: false)));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _reviewers.AppointAsync(1, 9, false, 3));
    }

    [Fact]
    public async Task TestAppointingToAnUnknownEntityIsNotFound()
    {
        SeedEntityWithRisks();

        await Assert.ThrowsAsync<DataNotFoundException>(() => _reviewers.AppointAsync(404, 1, false, 3));
    }

    [Fact]
    public async Task TestAReviewerSeesOnlyTheEntitiesTheyAreAppointedTo()
    {
        SeedEntityWithRisks();
        Seed(ctx => ctx.Entities.Add(NewEntity(2)));

        await _reviewers.AppointAsync(1, 1, false, 3);

        var entities = await _reviewers.GetEntitiesForReviewerAsync(1);

        Assert.Equal([1], entities);
    }

    // --- 8.6.3 campaign generation -------------------------------------------------------------------

    [Fact]
    public async Task TestNoCampaignIsGeneratedForAnEntityWithNoAppointedReviewer()
    {
        SeedEntityWithRisks();

        // Generating a campaign nobody can act on produces an overdue record that reflects a
        // configuration gap rather than a review failure, and the two must not look the same.
        Assert.Empty(await _campaigns.GenerateDueCampaignsAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task TestGenerationIsIdempotentWithinAPeriod()
    {
        SeedEntityWithRisks();
        await _reviewers.AppointAsync(1, 1, true, 3);

        var asOf = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var first = await _campaigns.GenerateDueCampaignsAsync(asOf);
        var second = await _campaigns.GenerateDueCampaignsAsync(asOf.AddDays(1));

        Assert.Single(first);
        // The daily job must converge on the campaign it already made, not create one each morning.
        Assert.Empty(second);
    }

    [Fact]
    public async Task TestCampaignItemsAreOrderedWithTheMostUrgentFirst()
    {
        SeedEntityWithRisks();
        await _reviewers.AppointAsync(1, 1, true, 3);

        // Risk 2 has the lower residual score but is flagged for review, so it should lead: a
        // deadline beats a merely higher score.
        await GetService<IRisksService>().RequestReviewAsync(2, "A Critical vulnerability was linked.");

        var campaign = (await _campaigns.GenerateDueCampaignsAsync(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))).Single();

        var loaded = await _campaigns.GetAsync(campaign.Id);
        var ordered = loaded.Items.OrderBy(i => i.Id).ToList();

        Assert.Equal(2, ordered.Count);
        Assert.Equal(2, ordered[0].RiskId);
        Assert.Equal(1, ordered[1].RiskId);
    }

    [Fact]
    public async Task TestOverdueCampaignsAreMarked()
    {
        SeedEntityWithRisks();
        await _reviewers.AppointAsync(1, 1, true, 3);

        var campaign = (await _campaigns.GenerateDueCampaignsAsync(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))).Single();

        var overdue = await _campaigns.MarkOverdueAsync(campaign.DueDate.AddDays(1));

        Assert.Single(overdue);
        Assert.Equal(RiskReviewCampaignStatus.Overdue, (await _campaigns.GetAsync(campaign.Id)).Status);
    }

    // --- 8.6.4 ranking and decisions -----------------------------------------------------------------

    private async Task<RiskReviewCampaign> OpenCampaignAsync()
    {
        SeedEntityWithRisks();
        await _reviewers.AppointAsync(1, 1, true, 3);

        return (await _campaigns.GenerateDueCampaignsAsync(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc))).Single();
    }

    [Fact]
    public async Task TestRankingIsMirroredOntoTheRiskSoTheDesktopListCanSortOnIt()
    {
        var campaign = await OpenCampaignAsync();
        var loaded = await _campaigns.GetAsync(campaign.Id);

        var ordered = loaded.Items.OrderByDescending(i => i.RiskId).Select(i => i.Id).ToList();

        await _campaigns.SaveRankingAsync(campaign.Id, ordered, 1);

        await using var db = OpenContext();
        var items = db.RiskReviewCampaignItems.Where(i => i.CampaignId == campaign.Id)
            .ToDictionary(i => i.RiskId, i => i.Rank);

        Assert.Equal(1, items[2]);
        Assert.Equal(2, items[1]);

        // 8.6.5: business rank surfaced on the risk itself.
        Assert.Equal(1, db.Risks.Single(r => r.Id == 2).BusinessRank);
        Assert.Equal(2, db.Risks.Single(r => r.Id == 1).BusinessRank);
    }

    [Fact]
    public async Task TestRankingRejectsIdsThatAreNotInTheCampaign()
    {
        var campaign = await OpenCampaignAsync();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _campaigns.SaveRankingAsync(campaign.Id, [9999], 1));
    }

    [Fact]
    public async Task TestPendingIsNotADecision()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _campaigns.DecideAsync(campaign.Id, item.Id,
                new CampaignDecisionRequest { Decision = RiskReviewDecision.Pending }, 1));
    }

    [Fact]
    public async Task TestAcceptingCreatesAnAcceptanceLinkedToTheItem()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First(i => i.RiskId == 1);

        var decided = await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
        {
            Decision = RiskReviewDecision.Accepted,
            Notes = "Signed off at the August risk committee.",
            Acceptance = new RiskAcceptanceRequest
            {
                BusinessJustification = "Compensating monitoring is in place until the rebuild.",
                ExpiresAt = DateTime.UtcNow.AddDays(120)
            }
        }, actingUserId: 1);

        Assert.Equal(RiskReviewDecision.Accepted, decided.Decision);
        Assert.NotNull(decided.RiskAcceptanceId);

        await using var db = OpenContext();

        var acceptance = db.RiskAcceptances.Single(a => a.Id == decided.RiskAcceptanceId);
        Assert.Equal(1, acceptance.RiskId);

        // One timeline: the acceptance wrote the review, and the campaign did not write a second.
        Assert.Single(db.MgmtReviews.Where(r => r.RiskId == 1).ToList());
    }

    [Fact]
    public async Task TestAcceptingNeedsAJustificationAndExpiry()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _campaigns.DecideAsync(campaign.Id, item.Id,
                new CampaignDecisionRequest { Decision = RiskReviewDecision.Accepted }, 1));
    }

    /// <summary>
    /// Segregation of duties reaches the portal. User 2 owns and submitted both risks, so their
    /// "accept" is refused with the same rule the desktop path uses.
    /// </summary>
    [Fact]
    public async Task TestAReviewerCannotAcceptARiskTheyOwn()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First(i => i.RiskId == 1);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Accepted,
                Acceptance = new RiskAcceptanceRequest
                {
                    BusinessJustification = "It is fine.", ExpiresAt = DateTime.UtcNow.AddDays(30)
                }
            }, actingUserId: 2));

        Assert.Equal("segregation_of_duties", ex.RuleName);
    }

    [Fact]
    public async Task TestRequestingMitigationCreatesTasksAndAMitigationToHangThemOff()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First(i => i.RiskId == 1);

        await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
        {
            Decision = RiskReviewDecision.MitigationRequested,
            Notes = "Please rebuild the appliance this quarter.",
            Tasks =
            [
                new MitigationTaskRequest
                {
                    Title = "Rebuild the appliance", OwnerId = 2,
                    DueDate = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        }, actingUserId: 1);

        await using var db = OpenContext();

        var mitigation = Assert.Single(db.Mitigations.Where(m => m.RiskId == 1).ToList());
        var task = Assert.Single(db.MitigationTasks.Where(t => t.MitigationId == mitigation.Id).ToList());

        Assert.Equal("Rebuild the appliance", task.Title);
        Assert.Single(db.MgmtReviews.Where(r => r.RiskId == 1).ToList());
    }

    [Fact]
    public async Task TestRequestingMitigationNeedsAtLeastOneTask()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _campaigns.DecideAsync(campaign.Id, item.Id,
                new CampaignDecisionRequest { Decision = RiskReviewDecision.MitigationRequested }, 1));
    }

    [Fact]
    public async Task TestEscalationNeedsANamedApprover()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _campaigns.DecideAsync(campaign.Id, item.Id,
                new CampaignDecisionRequest { Decision = RiskReviewDecision.Escalated }, 1));
    }

    [Fact]
    public async Task TestEscalationRecordsTheApproverAndWritesAReview()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First(i => i.RiskId == 1);

        var decided = await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
        {
            Decision = RiskReviewDecision.Escalated,
            EscalateToUserId = 3,
            Notes = "Above my delegated authority."
        }, 1);

        Assert.Equal(3, decided.EscalatedToId);

        await using var db = OpenContext();
        Assert.Single(db.MgmtReviews.Where(r => r.RiskId == 1).ToList());
    }

    [Fact]
    public async Task TestDecidingEveryItemCompletesTheCampaign()
    {
        var campaign = await OpenCampaignAsync();
        var items = (await _campaigns.GetAsync(campaign.Id)).Items.ToList();

        foreach (var item in items)
            await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
            }, 1);

        var completed = await _campaigns.GetAsync(campaign.Id);

        Assert.Equal(RiskReviewCampaignStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task TestAClosedCampaignRefusesFurtherDecisions()
    {
        var campaign = await OpenCampaignAsync();
        var items = (await _campaigns.GetAsync(campaign.Id)).Items.ToList();

        foreach (var item in items)
            await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
            }, 1);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _campaigns.DecideAsync(campaign.Id, items[0].Id, new CampaignDecisionRequest
            {
                Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
            }, 1));
    }

    // --- 8.6.5 statistics ----------------------------------------------------------------------------

    [Fact]
    public async Task TestStatisticsReportTheDecisionMix()
    {
        var campaign = await OpenCampaignAsync();
        var item = (await _campaigns.GetAsync(campaign.Id)).Items.First(i => i.RiskId == 1);

        await _campaigns.DecideAsync(campaign.Id, item.Id, new CampaignDecisionRequest
        {
            Decision = RiskReviewDecision.Escalated, EscalateToUserId = 3
        }, 1);

        var stats = Assert.Single(await _campaigns.GetStatisticsAsync(1));

        Assert.Equal(2, stats.TotalItems);
        Assert.Equal(1, stats.DecidedItems);
        Assert.Equal(1, stats.Escalated);
        Assert.NotNull(stats.AverageDaysToDecide);
    }
}
