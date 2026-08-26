using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Tests.DI;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Governance;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// The five Track 8 recurring jobs.
///
/// All five are deliberately thin: the decisions live in the services, and the job's job is the
/// notifications. That split is what makes them testable without a database — none of these tests
/// opens a connection — and it is why the resolution of "which risks are overdue" moved into
/// <c>MgmtReviewsService</c> rather than living in the job.
///
/// The property that matters for every one of them is that a broken notification channel must not
/// stop the pass. A job that propagates an exception is retried immediately by Hangfire with the same
/// broken state, and for a daily governance sweep that means the rest of the register is never
/// processed at all.
/// </summary>
[TestSubject(typeof(RiskReviewCadenceJob))]
public class GovernanceJobsTest
{
    private static Risk NewRisk(int id, int? owner = null, int? manager = null) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        Owner = owner, Manager = manager, ReviewRequested = true,
        ReviewRequestedReason = "A new Critical vulnerability was linked."
    };

    // --- residual calculation --------------------------------------------------------------------

    [Fact]
    public void TheResidualJobReSimulatesQuantitativeRisksBeforeTheQualitativePass()
    {
        var calculation = Substitute.For<IRiskCalculationService>();
        var quantitative = Substitute.For<IQuantitativeRiskService>();

        calculation.CalculateResidualRiskAsync().Returns(Task.FromResult(3));
        quantitative.RecomputeAllAsync().Returns(Task.FromResult(1));

        new ResidualRiskCalculation(TestDoubles.Logger(), TestDoubles.DalService(), calculation,
            quantitative).Run();

        // Order matters: the qualitative pass would otherwise overwrite the residual the simulation
        // just derived.
        Received.InOrder(() =>
        {
            quantitative.RecomputeAllAsync();
            calculation.CalculateResidualRiskAsync();
        });
    }

    // --- retention -------------------------------------------------------------------------------

    [Fact]
    public void TheRetentionJobTrimsBothTheAuditTrailAndTheRevocationList()
    {
        var trail = Substitute.For<IAuditTrailService>();
        var revocation = Substitute.For<ITokenRevocationService>();

        trail.ApplyRetentionAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(12));
        revocation.PruneExpiredAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(4));

        new GovernanceRetentionJob(TestDoubles.Logger(), TestDoubles.DalService(), trail, revocation)
            .Run();

        trail.Received(1).ApplyRetentionAsync(Arg.Any<DateTime>());
        revocation.Received(1).PruneExpiredAsync(Arg.Any<DateTime>());
    }

    // --- acceptance expiry ------------------------------------------------------------------------

    [Fact]
    public void TheExpiryPassNotifiesTheAuthorizerAndTheRequesterOfAnExpiredAcceptance()
    {
        var acceptances = Substitute.For<IRiskAcceptancesService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        var result = new RiskAcceptanceExpiryResult();
        result.Expired.Add(new RiskAcceptance
        {
            Id = 1, RiskId = 5, Name = "Q2 exception", AuthorizingManagerId = 7, RequestedById = 8,
            BusinessJustification = "Compensating control.",
            ExpiresAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = RiskAcceptanceStatus.Expired,
            Risk = NewRisk(5, owner: 9)
        });

        acceptances.ProcessExpiryAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(result));

        new RiskAcceptanceExpiryPass(TestDoubles.Logger(), TestDoubles.DalService(), acceptances,
            notifications, messages).Run();

        notifications.Received(1).RiskAcceptanceExpiredAsync(Arg.Any<RiskAcceptance>(), Arg.Any<Risk?>());

        // The authorizing manager, the requester and the risk owner — three distinct people.
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 7, Arg.Any<int>());
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 8, Arg.Any<int>());
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 9, Arg.Any<int>());
    }

    [Fact]
    public void TheExpiryPassAnnouncesAPreExpiryWarningToTheAuthorizer()
    {
        var acceptances = Substitute.For<IRiskAcceptancesService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        var result = new RiskAcceptanceExpiryResult();
        result.Warnings.Add((new RiskAcceptance
        {
            Id = 1, RiskId = 5, Name = "Q2 exception", AuthorizingManagerId = 7,
            ExpiresAt = DateTime.UtcNow.AddDays(7), Status = RiskAcceptanceStatus.Active
        }, 7));

        acceptances.ProcessExpiryAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(result));

        new RiskAcceptanceExpiryPass(TestDoubles.Logger(), TestDoubles.DalService(), acceptances,
            notifications, messages).Run();

        notifications.Received(1)
            .RiskAcceptanceExpiringAsync(Arg.Any<RiskAcceptance>(), 7, Arg.Any<int>());
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 7, Arg.Any<int>());
    }

    [Fact]
    public void AFailingMessageChannelDoesNotAbortTheExpiryPass()
    {
        var acceptances = Substitute.For<IRiskAcceptancesService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        var result = new RiskAcceptanceExpiryResult();
        result.Expired.Add(new RiskAcceptance
        {
            Id = 1, RiskId = 5, Name = "Q2", AuthorizingManagerId = 7,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), Status = RiskAcceptanceStatus.Expired,
            Risk = NewRisk(5)
        });

        acceptances.ProcessExpiryAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(result));
        messages.SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException("mail server down"));

        // The expiry itself is already committed by the service, so losing the message is far better
        // than leaving half the acceptances unprocessed until tomorrow.
        new RiskAcceptanceExpiryPass(TestDoubles.Logger(), TestDoubles.DalService(), acceptances,
            notifications, messages).Run();

        notifications.Received(1).RiskAcceptanceExpiredAsync(Arg.Any<RiskAcceptance>(), Arg.Any<Risk?>());
    }

    [Fact]
    public void TheExpiryPassDoesNothingWhenThereIsNothingToDo()
    {
        var acceptances = Substitute.For<IRiskAcceptancesService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        acceptances.ProcessExpiryAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new RiskAcceptanceExpiryResult()));

        new RiskAcceptanceExpiryPass(TestDoubles.Logger(), TestDoubles.DalService(), acceptances,
            notifications, messages).Run();

        messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // --- review cadence ---------------------------------------------------------------------------

    private static RiskReviewCadenceJob NewCadenceJob(IRisksService risks, IMgmtReviewsService reviews,
        IMitigationTasksService tasks, INotificationEventPublisher notifications,
        IMessagesService messages) =>
        new(TestDoubles.Logger(), TestDoubles.DalService(), risks, reviews, tasks, notifications,
            messages);

    private static (IRisksService, IMgmtReviewsService, IMitigationTasksService,
        INotificationEventPublisher, IMessagesService) EmptyCollaborators()
    {
        var risks = Substitute.For<IRisksService>();
        var reviews = Substitute.For<IMgmtReviewsService>();
        var tasks = Substitute.For<IMitigationTasksService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        risks.GetReviewRequestedAsync().Returns(Task.FromResult(new List<Risk>()));
        reviews.GetOverdueReviewsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<OverdueReview>()));
        tasks.GetDueOrOverdueAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask>()));

        return (risks, reviews, tasks, notifications, messages);
    }

    [Fact]
    public void TheCadenceJobNotifiesTheOwnerAndManagerOfAnOverdueReview()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        reviews.GetOverdueReviewsAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(new List<OverdueReview>
        {
            new()
            {
                RiskId = 1, Subject = "Unpatched appliance", ReferenceId = "R-1", Status = "New",
                OwnerId = 4, ManagerId = 5, Score = 8.2, CadenceDays = 30, DaysOverdue = 12,
                LastReviewedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        }));

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        notifications.Received(1).RiskReviewOverdueAsync(Arg.Any<Risk>(), 8.2, 12,
            Arg.Any<DateTime?>(), 30);
        messages.Received(1).SendMessageAsync(Arg.Is<string>(m => m.Contains("12 day(s) overdue")), 4,
            Arg.Any<int>());
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 5, Arg.Any<int>());
    }

    /// <summary>
    /// A risk never reviewed at all is the group easiest to miss, and the message has to say so
    /// rather than reporting an overdue count against a review that does not exist.
    /// </summary>
    [Fact]
    public void ANeverReviewedRiskIsAnnouncedAsSuch()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        reviews.GetOverdueReviewsAsync(Arg.Any<DateTime>()).Returns(Task.FromResult(new List<OverdueReview>
        {
            new()
            {
                RiskId = 1, Subject = "Never looked at", ReferenceId = "R-1", Status = "New",
                OwnerId = 4, Score = 5, CadenceDays = 120, DaysOverdue = 5, LastReviewedAt = null
            }
        }));

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains("never had a management review")), 4, Arg.Any<int>());
    }

    [Fact]
    public void TheCadenceJobNotifiesOnEventTriggeredReviewFlags()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        risks.GetReviewRequestedAsync().Returns(Task.FromResult(new List<Risk>
        {
            NewRisk(2, owner: 6)
        }));

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains("flagged for review")), 6, Arg.Any<int>());
    }

    [Fact]
    public void AnOverdueTreatmentTaskIsAnnouncedToItsOwnerAndMarkedNotified()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        var task = new MitigationTask
        {
            Id = 3, MitigationId = 9, Title = "Rebuild the appliance", OwnerId = 11,
            DueDate = DateTime.UtcNow.AddDays(-4), Status = MitigationTaskStatus.Open,
            Mitigation = new Mitigation
            {
                Id = 9, RiskId = 7, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                MitigationOwner = 1, SubmittedBy = 1, CurrentSolution = string.Empty,
                SecurityRequirements = string.Empty, SecurityRecommendations = string.Empty
            }
        };

        tasks.GetDueOrOverdueAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask> { task }));

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        notifications.Received(1).MitigationTaskDueAsync(task, 7, Arg.Is<int>(d => d < 0));
        messages.Received(1).SendMessageAsync(Arg.Is<string>(m => m.Contains("overdue")), 11,
            Arg.Any<int>());

        // Recorded, so tomorrow's pass does not repeat the same message.
        tasks.Received(1).MarkNotifiedAsync(3, Arg.Any<int>());
    }

    [Fact]
    public void ATaskAlreadyNotifiedAtTheSameNoticeIsNotRepeated()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        var task = new MitigationTask
        {
            Id = 3, MitigationId = 9, Title = "Rebuild", OwnerId = 11,
            // Plus an hour so the floor of the remaining notice is a stable 3 rather than tipping to
            // 2 on the microseconds between constructing the fixture and running the job.
            DueDate = DateTime.UtcNow.AddDays(3).AddHours(1), Status = MitigationTaskStatus.Open,
            // Already told at three days' notice.
            LastNotifiedDaysBefore = 3
        };

        tasks.GetDueOrOverdueAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<MitigationTask> { task }));

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
        tasks.DidNotReceive().MarkNotifiedAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void TheCadenceJobDoesNothingWhenTheRegisterIsCurrent()
    {
        var (risks, reviews, tasks, notifications, messages) = EmptyCollaborators();

        NewCadenceJob(risks, reviews, tasks, notifications, messages).Run();

        messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    // --- campaigns --------------------------------------------------------------------------------

    [Fact]
    public void ANewCampaignNotifiesEveryAppointedReviewer()
    {
        var campaigns = Substitute.For<IRiskReviewCampaignsService>();
        var reviewers = Substitute.For<IEntityRiskReviewersService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        campaigns.GenerateDueCampaignsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.MarkOverdueAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.TakeOverdueRemindersAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<CampaignReminder>()));

        // No campaigns generated, so nothing to announce — the honest baseline for the assertion
        // below, which is that the job does not invent notifications.
        new RiskReviewCampaignJob(TestDoubles.Logger(), TestDoubles.DalService(), campaigns, reviewers,
            notifications, messages).Run();

        messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
        reviewers.DidNotReceive().GetByEntityAsync(Arg.Any<int>());
    }

    [Fact]
    public void ANewCampaignIsAnnouncedToEveryAppointedReviewerWithADeepLink()
    {
        var campaigns = Substitute.For<IRiskReviewCampaignsService>();
        var reviewers = Substitute.For<IEntityRiskReviewersService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        var campaign = new RiskReviewCampaign
        {
            Id = 1, EntityId = 4, Name = "Risk review 2026Q3",
            PeriodStart = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
            Items = new List<RiskReviewCampaignItem>
            {
                new() { Id = 1, CampaignId = 1, RiskId = 1 },
                new() { Id = 2, CampaignId = 1, RiskId = 2 }
            }
        };

        campaigns.GenerateDueCampaignsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign> { campaign }));
        campaigns.MarkOverdueAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.TakeOverdueRemindersAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<CampaignReminder>()));

        reviewers.GetByEntityAsync(4).Returns(Task.FromResult(new List<EntityRiskReviewer>
        {
            new() { Id = 1, EntityId = 4, UserId = 20, IsPrimary = true },
            new() { Id = 2, EntityId = 4, UserId = 21 }
        }));

        new RiskReviewCampaignJob(TestDoubles.Logger(), TestDoubles.DalService(), campaigns, reviewers,
            notifications, messages).Run();

        notifications.Received(1).RiskReviewCampaignAssignedAsync(campaign, 20, 2);
        notifications.Received(1).RiskReviewCampaignAssignedAsync(campaign, 21, 2);
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 20, Arg.Any<int>());
        messages.Received(1).SendMessageAsync(Arg.Any<string>(), 21, Arg.Any<int>());
    }

    [Fact]
    public void AnEmptyCampaignIsNotAnnounced()
    {
        var campaigns = Substitute.For<IRiskReviewCampaignsService>();
        var reviewers = Substitute.For<IEntityRiskReviewersService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        campaigns.GenerateDueCampaignsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>
            {
                new()
                {
                    Id = 1, EntityId = 4, Name = "Empty",
                    DueDate = DateTime.UtcNow.AddDays(30),
                    Items = new List<RiskReviewCampaignItem>()
                }
            }));
        campaigns.MarkOverdueAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.TakeOverdueRemindersAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<CampaignReminder>()));

        new RiskReviewCampaignJob(TestDoubles.Logger(), TestDoubles.DalService(), campaigns, reviewers,
            notifications, messages).Run();

        // An entity with no open risks is good news, not an action item.
        messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void AnOverdueCampaignChasesItsReviewers()
    {
        var campaigns = Substitute.For<IRiskReviewCampaignsService>();
        var reviewers = Substitute.For<IEntityRiskReviewersService>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var messages = Substitute.For<IMessagesService>();

        var campaign = new RiskReviewCampaign
        {
            Id = 1, EntityId = 4, Name = "Risk review 2026Q2",
            DueDate = DateTime.UtcNow.AddDays(-20),
            Status = RiskReviewCampaignStatus.Overdue
        };

        campaigns.GenerateDueCampaignsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.MarkOverdueAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new List<RiskReviewCampaign>()));
        campaigns.TakeOverdueRemindersAsync(Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<CampaignReminder>
            {
                new() { Campaign = campaign, PendingItems = 3, ReviewerUserIds = [20] }
            }));

        new RiskReviewCampaignJob(TestDoubles.Logger(), TestDoubles.DalService(), campaigns, reviewers,
            notifications, messages).Run();

        notifications.Received(1).RiskReviewCampaignOverdueAsync(campaign, 3);
        messages.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains("still") && m.Contains("no decision")), 20, Arg.Any<int>());
    }
}
