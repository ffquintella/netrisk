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
/// Track 8 milestone 8.5 — the treatment task line items and the pending-risk triage that repairs
/// the dead assessment intake.
///
/// The intake half is the one worth stating plainly: before this, <c>AssessmentAnswer.SubmitRisk</c>
/// wrote <c>pending_risks</c> rows and <em>no live code path ever read them</em>. An organization
/// running assessments accumulated a queue nothing drained and nobody could see.
/// </summary>
[TestSubject(typeof(MitigationTasksService))]
public class GovernanceIntakeAndTasksInMemoryTest : InMemoryServiceTestBase
{
    private readonly IMitigationTasksService _tasks;
    private readonly IRisksService _risks;

    public GovernanceIntakeAndTasksInMemoryTest()
    {
        _tasks = GetService<IMitigationTasksService>();
        _risks = GetService<IRisksService>();
    }

    private static User NewUser(int id, string name) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Mitigation NewMitigation(int id, int riskId) => new()
    {
        Id = id, RiskId = riskId, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
        MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = 0,
        CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
        SecurityRecommendations = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningDate = new DateOnly(2026, 6, 1)
    };

    private void SeedRiskWithMitigation()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "planner"));
            ctx.Users.Add(NewUser(2, "engineer"));
            ctx.Risks.Add(NewRisk(1));
            ctx.Mitigations.Add(NewMitigation(1, 1));
        });
    }

    // --- 8.5.3 treatment tasks ---------------------------------------------------------------------

    [Fact]
    public async Task TestCreatingATaskRequiresATitle()
    {
        SeedRiskWithMitigation();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _tasks.CreateAsync(new MitigationTaskRequest { MitigationId = 1, Title = "  " }, 1));
    }

    [Fact]
    public async Task TestCreatingATaskAgainstAnUnknownMitigationIsNotFound()
    {
        SeedRiskWithMitigation();

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _tasks.CreateAsync(new MitigationTaskRequest { MitigationId = 99, Title = "Do the thing" }, 1));
    }

    [Fact]
    public async Task TestCreatingATaskWithAnUnknownOwnerIsNotFound()
    {
        SeedRiskWithMitigation();

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _tasks.CreateAsync(new MitigationTaskRequest
                { MitigationId = 1, Title = "Do the thing", OwnerId = 404 }, 1));
    }

    [Fact]
    public async Task TestATaskIsReachableFromItsRisk()
    {
        SeedRiskWithMitigation();

        await _tasks.CreateAsync(new MitigationTaskRequest
        {
            MitigationId = 1, Title = "Rotate the shared account", OwnerId = 2,
            DueDate = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)
        }, 1);

        var byRisk = await _tasks.GetByRiskAsync(1);
        Assert.Single(byRisk);
        Assert.Equal(MitigationTaskStatus.Open, byRisk[0].Status);
    }

    /// <summary>
    /// The completion timestamp follows the status rather than being sent by the client. A caller
    /// that can set "completed at" independently of "completed" can date the work whenever it likes,
    /// and this row is evidence.
    /// </summary>
    [Fact]
    public async Task TestCompletionTimestampFollowsTheStatus()
    {
        SeedRiskWithMitigation();

        var created = await _tasks.CreateAsync(new MitigationTaskRequest
            { MitigationId = 1, Title = "Patch the appliance", OwnerId = 2 }, 1);

        Assert.Null(created.CompletedAt);

        var completed = await _tasks.UpdateAsync(new MitigationTaskRequest
        {
            Id = created.Id, MitigationId = 1, Title = "Patch the appliance", OwnerId = 2,
            Status = MitigationTaskStatus.Completed
        }, 1);

        Assert.NotNull(completed.CompletedAt);

        var reopened = await _tasks.UpdateAsync(new MitigationTaskRequest
        {
            Id = created.Id, MitigationId = 1, Title = "Patch the appliance", OwnerId = 2,
            Status = MitigationTaskStatus.InProgress
        }, 1);

        Assert.Null(reopened.CompletedAt);
        // A task back in play starts its notification clock again.
        Assert.Null(reopened.LastNotifiedDaysBefore);
    }

    [Fact]
    public async Task TestDueQueryIgnoresCompletedAndCancelledWork()
    {
        SeedRiskWithMitigation();

        var open = await _tasks.CreateAsync(new MitigationTaskRequest
        {
            MitigationId = 1, Title = "Open", OwnerId = 2,
            DueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
        }, 1);

        var done = await _tasks.CreateAsync(new MitigationTaskRequest
        {
            MitigationId = 1, Title = "Done", OwnerId = 2,
            DueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
        }, 1);

        await _tasks.UpdateAsync(new MitigationTaskRequest
        {
            Id = done.Id, MitigationId = 1, Title = "Done", OwnerId = 2,
            Status = MitigationTaskStatus.Completed
        }, 1);

        var due = await _tasks.GetDueOrOverdueAsync(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 0);

        Assert.Single(due);
        Assert.Equal(open.Id, due[0].Id);
    }

    [Fact]
    public async Task TestMarkNotifiedIsRecordedSoTheJobDoesNotRepeatItself()
    {
        SeedRiskWithMitigation();

        var task = await _tasks.CreateAsync(new MitigationTaskRequest
        {
            MitigationId = 1, Title = "Open", OwnerId = 2,
            DueDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
        }, 1);

        await _tasks.MarkNotifiedAsync(task.Id, -3);

        await using var db = OpenContext();
        Assert.Equal(-3, db.MitigationTasks.Single(t => t.Id == task.Id).LastNotifiedDaysBefore);
    }

    // --- 8.5.2 pending-risk triage ------------------------------------------------------------------

    private void SeedPendingRisk(int id = 1)
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "triager"));
            ctx.PendingRisks.Add(new PendingRisk
            {
                Id = id,
                AssessmentId = 3,
                AssessmentAnswerId = 4,
                Subject = Encoding.UTF8.GetBytes("Shared credentials in the deployment script"),
                Score = 6.5f,
                Comment = "Raised by the quarterly assessment.",
                AffectedAssets = "build-01",
                SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = PendingRiskStatus.Pending
            });
            ctx.Categories.Add(new Category { Value = 1, Name = "Operational" });
            ctx.Sources.Add(new Source { Value = 1, Name = "Assessment" });
        });
    }

    [Fact]
    public async Task TestPendingRisksAreListedWithTheirSubjectDecoded()
    {
        SeedPendingRisk();

        var pending = await _risks.GetPendingRisksAsync();

        var row = Assert.Single(pending);
        // The column is a legacy BLOB; a listing that showed byte[] would be unusable.
        Assert.Equal("Shared credentials in the deployment script", row.Subject);
        Assert.Equal(PendingRiskStatus.Pending, row.Status);
    }

    [Fact]
    public async Task TestPromotionCreatesARiskWithAScoringRowAndTraceability()
    {
        SeedPendingRisk();

        var risk = await _risks.PromotePendingRiskAsync(1, new PendingRiskPromotion
        {
            Notes = "Confirmed with the platform team.", CategoryId = 1, SourceId = 1, OwnerId = 1
        }, actingUserId: 1);

        Assert.Equal("Shared credentials in the deployment script", risk.Subject);
        Assert.Equal("ASMT-3-4", risk.ReferenceId);

        await using var db = OpenContext();

        // Without a scoring row the promoted risk is invisible in every list and heatmap, which is
        // indistinguishable from not having promoted it.
        Assert.NotNull(db.RiskScorings.SingleOrDefault(s => s.Id == risk.Id));

        var pending = db.PendingRisks.Single(p => p.Id == 1);
        Assert.Equal(PendingRiskStatus.Promoted, pending.Status);
        Assert.Equal(risk.Id, pending.PromotedRiskId);
        Assert.Equal(1, pending.TriagedById);
    }

    [Fact]
    public async Task TestPromotingTwiceIsRefused()
    {
        SeedPendingRisk();

        await _risks.PromotePendingRiskAsync(1, new PendingRiskPromotion(), 1);

        var ex = await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _risks.PromotePendingRiskAsync(1, new PendingRiskPromotion(), 1));

        Assert.Contains("already been triaged", ex.Message);
    }

    [Fact]
    public async Task TestDismissalNeedsAReason()
    {
        SeedPendingRisk();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _risks.DismissPendingRiskAsync(1, "   ", 1));
    }

    [Fact]
    public async Task TestDismissalRecordsWhoAndWhy()
    {
        SeedPendingRisk();

        await _risks.DismissPendingRiskAsync(1, "Duplicate of R-17.", 1);

        await using var db = OpenContext();
        var pending = db.PendingRisks.Single(p => p.Id == 1);

        Assert.Equal(PendingRiskStatus.Dismissed, pending.Status);
        Assert.Equal("Duplicate of R-17.", pending.DismissalReason);
        Assert.NotNull(pending.TriagedAt);
    }

    [Fact]
    public async Task TestADismissedRowLeavesThePendingQueue()
    {
        SeedPendingRisk();

        await _risks.DismissPendingRiskAsync(1, "Not applicable.", 1);

        Assert.Empty(await _risks.GetPendingRisksAsync());
        Assert.Single(await _risks.GetPendingRisksAsync(PendingRiskStatus.Dismissed));
    }

    // --- 8.5.1 event-triggered review ---------------------------------------------------------------

    [Fact]
    public async Task TestRequestingAReviewFlagsTheRiskOnceAndKeepsTheFirstReason()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        Assert.True(await _risks.RequestReviewAsync(1, "A new Critical vulnerability was linked."));

        // The second request is a no-op: overwriting the timestamp would make "how long has this been
        // waiting" unanswerable, and that is the number that matters.
        Assert.False(await _risks.RequestReviewAsync(1, "Something else happened."));

        await using var db = OpenContext();
        var risk = db.Risks.Single(r => r.Id == 1);

        Assert.True(risk.ReviewRequested);
        Assert.Equal("A new Critical vulnerability was linked.", risk.ReviewRequestedReason);
    }

    [Fact]
    public async Task TestReviewRequestedListExcludesClosedRisks()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            var closed = NewRisk(2);
            closed.Status = "Closed";
            ctx.Risks.Add(closed);
        });

        await _risks.RequestReviewAsync(1, "reason");
        await _risks.RequestReviewAsync(2, "reason");

        var flagged = await _risks.GetReviewRequestedAsync();

        Assert.Single(flagged);
        Assert.Equal(1, flagged[0].Id);
    }

    [Fact]
    public async Task TestRequestingAReviewOnAnUnknownRiskIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _risks.RequestReviewAsync(404, "reason"));
    }
}
