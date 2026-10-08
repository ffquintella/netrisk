using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Monitoring;
using Model.RiskFlags;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 §8) — the archive (T195) against the real model, scope filters, audit interceptor and the Stage 9.8
/// reassessment machinery on the EF in-memory provider: archiving A1–A9, the reopening condition R1–R6, the quarterly
/// review Q1–Q5 and manual reopening M1–M3.
///
/// The case the track names for this stage is R1/R2 (T199): a reopening condition fires once and is recorded, instead of
/// reopening the risk again and again — for a declared event and for a KRI that stays breached for thirty days.
///
/// The in-memory provider does not enforce unique indexes: the database half of "once" (one trigger per event and risk)
/// is Track9KriReassessmentSchemaTests Q2 and Track9DecisionCycleSchemaTests Q2, which need Docker.
/// </summary>
[TestSubject(typeof(RiskArchiveService))]
public class RiskArchiveServiceInMemoryTest : DecisionCycleTestBase
{
    private IRiskArchiveService Archives => GetService<IRiskArchiveService>();
    private IRiskFlagsService Flags => GetService<IRiskFlagsService>();

    private RiskArchiveService NewArchives(INotificationEventPublisher publisher, Func<DateTime> clock) =>
        new(GetService<Serilog.ILogger>(), GetService<IDalService>(), GetService<IRiskWorkflowService>(), publisher)
            { Clock = clock };

    private MonitoringService NewMonitoring(INotificationEventPublisher publisher, Func<DateTime>? clock = null) =>
        new(GetService<Serilog.ILogger>(), GetService<IDalService>(), publisher) { Clock = clock ?? (() => DateTime.UtcNow) };

    private static ReassessmentEventRequest Event(ReassessmentTriggerType type, params int[] risks) => new()
    {
        Type = type, Title = $"{type} in force", OccurredAt = DateTime.UtcNow.AddMinutes(-1), RiskIds = risks.ToList()
    };

    private static KriRequest Kri() => new()
    {
        Name = "Kiosk hours down", Category = KriCategory.Unavailability, Source = "Zabbix", Unit = "hours",
        Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 8m, ToleranceRationale = "Board minute 2026/07.",
        MaxReadingAgeDays = 31
    };

    // --- A1–A9: archiving ---------------------------------------------------------------------------------------

    /// <summary>
    /// A1 — archiving closes the risk through its own closure, records the Phase 4 "archive" decision, the conditions and
    /// the first quarterly review, keeps the status to restore, and is audited with the person.
    /// </summary>
    [Fact]
    public async Task TestA1_ArchivingClosesTheRiskAndRecordsTheDecision()
    {
        ReviewedRisk(1);

        var dto = await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation,
            ReassessmentTriggerType.NewDataOrKriBreach), Author);

        var risk = RiskRow(1);
        var archive = Assert.Single(ArchivesOf(1));
        var closure = Read(ctx => ctx.Closures.Single(c => c.RiskId == 1));

        Assert.Equal(("Closed", RiskStatus.Closed, closure.Id), (risk.Status, risk.StatusId, risk.CloseId));
        Assert.Equal((closure.Id, NotRelevant, Author), (archive.ClosureId!.Value, closure.CloseReason, closure.UserId));
        Assert.Equal(("Mgmt Reviewed", RiskArchiveStatus.Archived), (archive.PreviousStatus, archive.Status));
        Assert.Equal(archive.ArchivedAt.AddMonths(3), archive.NextReviewDueAt);

        Assert.Equal(RiskArchiveState.Live, dto.State);
        Assert.Equal(new[] { ReassessmentTriggerType.NewRegulation, ReassessmentTriggerType.NewDataOrKriBreach },
            dto.Conditions.Select(c => c.TriggerType));
        Assert.False(dto.ReviewOverdue);

        var decision = Read(ctx => ctx.RiskDecisions.Single(d => d.RiskId == 1));
        Assert.Equal((RiskDecisionKind.Archive, RiskDecisionSource.Declared, (int?)Author),
            (decision.Decision, decision.Source, decision.DecidedById));

        Assert.Contains(Audit(nameof(RiskArchive)), a => a.UserId == Author && a.EntityId == archive.Id);
        Assert.Equal(2, Audit(nameof(RiskArchiveCondition)).Count);
    }

    /// <summary>A2 — every invalid request is refused naming the field, and nothing is written: the risk stays open.</summary>
    [Theory]
    [InlineData("no-justification", "Justification")]
    [InlineData("long-justification", "Justification")]
    [InlineData("no-close-reason", "CloseReason")]
    [InlineData("unknown-close-reason", "CloseReason")]
    [InlineData("no-condition", "Conditions")]
    [InlineData("undefined-condition", "Conditions")]
    [InlineData("duplicate-condition", "Conditions")]
    [InlineData("long-condition", "Conditions")]
    public async Task TestA2_AnInvalidArchiveIsRefusedNamingTheField(string scenario, string field)
    {
        ReviewedRisk(1);
        var request = ArchiveRequest();
        switch (scenario)
        {
            case "no-justification": request.Justification = " "; break;
            case "long-justification": request.Justification = new string('x', 4001); break;
            case "no-close-reason": request.CloseReason = null; break;
            case "unknown-close-reason": request.CloseReason = 77; break;
            case "no-condition": request.Conditions = []; break;
            case "undefined-condition": request.Conditions![0].TriggerType = (ReassessmentTriggerType)9; break;
            case "duplicate-condition":
                request.Conditions!.Add(new RiskArchiveConditionRequest { TriggerType = ReassessmentTriggerType.NewRegulation });
                break;
            default: request.Conditions![0].Description = new string('x', 1001); break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Archives.ArchiveAsync(1, request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(ArchivesOf(1));
        Assert.Equal("Mgmt Reviewed", RiskRow(1).Status);
    }

    /// <summary>A3 — a closed risk is not archived; a missing or out-of-scope one is not found.</summary>
    [Fact]
    public async Task TestA3_AClosedRiskIsRefusedAndAnInvisibleOneIsNotFound()
    {
        ReviewedRisk(1, status: "Closed");
        ReviewedRisk(2, unit: UnitB);

        var closed = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ArchiveAsync(1, ArchiveRequest(), Author));
        Assert.Equal(RiskArchiveService.RiskClosedRule, closed.RuleName);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Archives.ArchiveAsync(999, ArchiveRequest(), Author));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Archives.ArchiveAsync(2, ArchiveRequest(), Author));
        Assert.Empty(ArchivesOf(2));
    }

    /// <summary>A4 — archiving is closing: with no management review the Track 8 state machine refuses it, nothing written.</summary>
    [Fact]
    public async Task TestA4_TheStateMachineStillDecidesWhetherTheRiskMayClose()
    {
        AddRisk(1, UnitA);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() => Archives.ArchiveAsync(1, ArchiveRequest(), Author));

        Assert.Empty(ArchivesOf(1));
        Assert.Equal("New", RiskRow(1).Status);
        Assert.Equal(0, Read(ctx => ctx.Closures.Count()));
    }

    /// <summary>A5 — Gate A: a risk carrying a non-discretionary condition is never archived (archiving is discarding).</summary>
    [Fact]
    public async Task TestA5_GateARefusesArchiving()
    {
        ReviewedRisk(1);
        await Flags.DeclareAsync(1, RiskFlagCode.HumanSafety,
            new RiskFlagDeclarationRequest { Reason = "The kiosk also controls the door." }, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ArchiveAsync(1, ArchiveRequest(), Author));

        Assert.Equal("gate_a_non_discretionary", ex.RuleName);
        Assert.Empty(ArchivesOf(1));
    }

    /// <summary>A6 — the risk's own people do not archive it: the segregation of duties of every Phase 4 decision.</summary>
    [Fact]
    public async Task TestA6_TheOwnerCannotArchiveTheirOwnRisk()
    {
        ReviewedRisk(1);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ArchiveAsync(1, ArchiveRequest(), Owner));

        Assert.Equal("segregation_of_duties", ex.RuleName);
        Assert.Empty(ArchivesOf(1));
    }

    /// <summary>A7 — the third line does not archive, administrator or not.</summary>
    [Fact]
    public async Task TestA7_TheThirdLineCannotArchive()
    {
        ReviewedRisk(1);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ArchiveAsync(1, ArchiveRequest(), Auditor));

        Assert.Equal(ThirdLineAssurance.CannotApproveRule, ex.RuleName);
        Assert.Empty(ArchivesOf(1));
    }

    /// <summary>
    /// A8 — watching KRI breaches while a linked KRI is breached now: the condition already holds, so the archive is
    /// refused; without that condition the same risk may be archived.
    /// </summary>
    [Fact]
    public async Task TestA8_AConditionThatAlreadyHoldsIsRefused()
    {
        ReviewedRisk(1);
        var monitoring = NewMonitoring(Substitute.For<INotificationEventPublisher>());
        var kri = (await monitoring.CreateKriAsync(Kri(), Cro)).Id;
        await monitoring.LinkRiskAsync(kri, 1, Author);
        await monitoring.RecordReadingAsync(kri,
            new KriReadingRequest { Value = 12m, ObservedAt = DateTime.UtcNow.AddHours(-1) }, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewDataOrKriBreach), Author));
        Assert.Equal(RiskArchiveService.ConditionMetRule, ex.RuleName);
        Assert.Empty(ArchivesOf(1));

        var archived = await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);
        Assert.Equal(RiskArchiveState.Live, archived.State);
    }

    /// <summary>A9 — a scoped caller archives a risk of their own unit and lists only their unit's archives.</summary>
    [Fact]
    public async Task TestA9_TheListFollowsTheScope()
    {
        ReviewedRisk(1);
        ReviewedRisk(2, unit: UnitB);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);
        await Archives.ArchiveAsync(2, ArchiveRequest(), Author);

        ScopeTo(UnitA);
        var visible = await Archives.GetArchivesAsync(dueOnly: false, includeEnded: false);

        Assert.Equal(new[] { 1 }, visible.Select(a => a.RiskId));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Archives.GetRiskArchivesAsync(2));
    }

    // --- R1–R6: the reopening condition -------------------------------------------------------------------------

    /// <summary>
    /// R1 (T199) — a declared event of a watched type reopens the archive once and records it: the risk returns to its
    /// status before archiving, its closure goes, a reassessment trigger is raised and the reopening announced once. A
    /// second event of the same type reaches an open risk — a trigger, not a second reopening — and applying the first
    /// event again reopens nothing and triggers nothing.
    /// </summary>
    [Fact]
    public async Task TestR1_ADeclaredConditionFiresOnceAndIsRecorded()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);
        var publisher = Substitute.For<INotificationEventPublisher>();
        var monitoring = NewMonitoring(publisher);

        var first = await monitoring.DeclareEventAsync(Event(ReassessmentTriggerType.NewRegulation, 1), Author);

        Assert.Equal(new[] { 1 }, first.ReopenedArchivedRiskIds);
        Assert.Empty(first.SkippedClosedRiskIds);
        var archive = Assert.Single(ArchivesOf(1));
        Assert.Equal((RiskArchiveStatus.Reopened, RiskArchiveReopenOrigin.Condition, (int?)first.Id),
            (archive.Status, archive.ReopenOrigin, archive.ReopenEventId));
        Assert.Null(archive.ReopenedById);
        Assert.Contains("new regulation", archive.ReopenReason);

        var risk = RiskRow(1);
        Assert.Equal(("Mgmt Reviewed", RiskStatus.ManagementReview, (int?)null), (risk.Status, risk.StatusId, risk.CloseId));
        Assert.True(risk.ReviewRequested);
        Assert.Equal(0, Read(ctx => ctx.Closures.Count(c => c.RiskId == 1)));
        Assert.Single(Read(ctx => ctx.RiskReassessmentTriggers.Where(t => t.RiskId == 1).ToList()));

        // Again: a new event of the same type, then the first one re-applied.
        var second = await monitoring.DeclareEventAsync(Event(ReassessmentTriggerType.NewRegulation, 1), Author);
        var again = await monitoring.AddEventRisksAsync(first.Id, new ReassessmentRisksRequest { RiskIds = [1] }, Author);

        Assert.Empty(second.ReopenedArchivedRiskIds);
        Assert.Empty(again.ReopenedArchivedRiskIds);
        Assert.Equal(new[] { 1 }, again.AlreadyTriggeredRiskIds);
        Assert.Single(ArchivesOf(1));
        Assert.Equal(2, Read(ctx => ctx.RiskReassessmentTriggers.Count(t => t.RiskId == 1)));

        await publisher.Received(1).RiskArchiveReopenedAsync(Arg.Any<Risk>(), Arg.Any<double?>(), Arg.Any<RiskArchive>(),
            Arg.Any<ReassessmentEvent>());
    }

    /// <summary>
    /// R2 (T199) — a KRI linked to an archived risk breaches and stays breached for thirty days, evaluated every day: the
    /// archive is reopened once, with one trigger, announced once.
    /// </summary>
    [Fact]
    public async Task TestR2_AKriConditionFiresOnceOverThirtyDays()
    {
        ReviewedRisk(1);
        var publisher = Substitute.For<INotificationEventPublisher>();
        var day0 = DateTime.UtcNow.AddDays(-35);
        var day = day0;
        var monitoring = NewMonitoring(publisher, () => day);

        var kri = (await monitoring.CreateKriAsync(Kri(), Cro)).Id;
        await monitoring.LinkRiskAsync(kri, 1, Author);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewDataOrKriBreach), Author);

        await monitoring.RecordReadingAsync(kri, new KriReadingRequest { Value = 12m, ObservedAt = day0 }, Author);
        for (var d = 1; d <= 30; d++)
        {
            day = day0.AddDays(d);
            var summary = await monitoring.EvaluateAllAsync();
            Assert.Equal(0, summary.ArchivesReopened);
        }

        var archive = Assert.Single(ArchivesOf(1));
        Assert.Equal(RiskArchiveStatus.Reopened, archive.Status);
        Assert.Equal(RiskArchiveReopenOrigin.Condition, archive.ReopenOrigin);
        Assert.Equal("Mgmt Reviewed", RiskRow(1).Status);
        var trigger = Assert.Single(Read(ctx => ctx.RiskReassessmentTriggers.Where(t => t.RiskId == 1).ToList()));
        Assert.Equal(archive.ReopenEventId, trigger.EventId);

        await publisher.Received(1).RiskArchiveReopenedAsync(Arg.Any<Risk>(), Arg.Any<double?>(), Arg.Any<RiskArchive>(),
            Arg.Any<ReassessmentEvent>());
        await publisher.Received(1).RiskReassessmentTriggeredAsync(Arg.Any<Risk>(), Arg.Any<double?>(),
            Arg.Any<ReassessmentEvent>());
    }

    /// <summary>R3 — a type the archive does not watch leaves it closed, skipped and listed; with no open risk it is 422.</summary>
    [Fact]
    public async Task TestR3_AnUnwatchedTypeLeavesTheArchiveAlone()
    {
        ReviewedRisk(1);
        ReviewedRisk(2);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);
        var monitoring = NewMonitoring(Substitute.For<INotificationEventPublisher>());

        var dto = await monitoring.DeclareEventAsync(Event(ReassessmentTriggerType.NewAiModel, 1, 2), Author);

        Assert.Equal(new[] { 1 }, dto.SkippedClosedRiskIds);
        Assert.Empty(dto.ReopenedArchivedRiskIds);
        Assert.Equal(RiskArchiveStatus.Archived, Assert.Single(ArchivesOf(1)).Status);
        Assert.Equal("Closed", RiskRow(1).Status);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            monitoring.DeclareEventAsync(Event(ReassessmentTriggerType.NewAiModel, 1), Author));
        Assert.Equal(MonitoringService.NoOpenRiskRule, ex.RuleName);
    }

    /// <summary>R4 — an event naming only an archived risk that watches it is accepted: the reopening is what it reassesses.</summary>
    [Fact]
    public async Task TestR4_AnEventNamingOnlyAWatchingArchiveIsAccepted()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.SupplierAcquisitionOrMigration), Author);

        var dto = await NewMonitoring(Substitute.For<INotificationEventPublisher>())
            .DeclareEventAsync(Event(ReassessmentTriggerType.SupplierAcquisitionOrMigration, 1), Author);

        Assert.Equal(new[] { 1 }, dto.ReopenedArchivedRiskIds);
        Assert.Equal(new[] { 1 }, dto.Triggers.Select(t => t.RiskId));
    }

    /// <summary>
    /// R5 — reopened outside the archive (the legacy <c>DELETE /Risks/{id}/Closure</c>): the archive is superseded, takes no
    /// review and no reopening, and an event of its type reaches the open risk as a plain trigger.
    /// </summary>
    [Fact]
    public async Task TestR5_ALegacyReopenSupersedesTheArchive()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);

        Risks.DeleteRiskClosure(1);
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == 1).Status = "Mitigation Planned");

        Assert.Equal(RiskArchiveState.Superseded, Assert.Single(await Archives.GetRiskArchivesAsync(1)).State);
        Assert.Empty(await Archives.GetArchivesAsync(dueOnly: false, includeEnded: false));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Archives.ReopenAsync(1, new RiskArchiveReopenRequest { Reason = "again" }, Author));
        Assert.Equal(RiskArchiveService.NotLiveRule, ex.RuleName);

        var dto = await NewMonitoring(Substitute.For<INotificationEventPublisher>())
            .DeclareEventAsync(Event(ReassessmentTriggerType.NewRegulation, 1), Author);
        Assert.Empty(dto.ReopenedArchivedRiskIds);
        Assert.Equal(RiskArchiveStatus.Archived, Assert.Single(ArchivesOf(1)).Status);
    }

    /// <summary>R6 — the status to restore: one the register knows comes back; anything else comes back as New.</summary>
    [Fact]
    public async Task TestR6_TheStatusBeforeArchivingIsRestored()
    {
        ReviewedRisk(1, status: "Mitigation Planned");
        ReviewedRisk(2, status: "Accepted (legacy)");
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);
        await Archives.ArchiveAsync(2, ArchiveRequest(ReassessmentTriggerType.NewRegulation), Author);

        await NewMonitoring(Substitute.For<INotificationEventPublisher>())
            .DeclareEventAsync(Event(ReassessmentTriggerType.NewRegulation, 1, 2), Author);

        Assert.Equal(("Mitigation Planned", RiskStatus.MitigationPlanned), (RiskRow(1).Status, RiskRow(1).StatusId));
        Assert.Equal(("New", RiskStatus.New), (RiskRow(2).Status, RiskRow(2).StatusId));
    }

    // --- Q1–Q5: the quarterly review ----------------------------------------------------------------------------

    /// <summary>
    /// Q1 — the review falls due a quarter after archiving; the daily sweep announces it once per due date; keeping the
    /// archive moves the date a quarter on and the next due date is announced again.
    /// </summary>
    [Fact]
    public async Task TestQ1_TheQuarterlyReviewIsAnnouncedOncePerDueDate()
    {
        ReviewedRisk(1);
        var publisher = Substitute.For<INotificationEventPublisher>();
        var now = DateTime.UtcNow;
        var archives = NewArchives(publisher, () => now);
        await archives.ArchiveAsync(1, ArchiveRequest(), Author);

        Assert.Equal(0, (await archives.NotifyDueReviewsAsync()).Due);

        now = now.AddMonths(3).AddMinutes(1);
        Assert.True(Assert.Single(await archives.GetArchivesAsync(dueOnly: true, includeEnded: false)).ReviewOverdue);
        Assert.Equal((1, 1, 1), Summary(await archives.NotifyDueReviewsAsync()));
        Assert.Equal((1, 1, 0), Summary(await archives.NotifyDueReviewsAsync()));
        await publisher.Received(1).RiskArchiveReviewDueAsync(Arg.Any<Risk>(), Arg.Any<RiskArchive>());

        var kept = await archives.ReviewAsync(1, new RiskArchiveReviewRequest
            { Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Still isolated; decommissioning in Q1." }, Author);
        Assert.Equal(now.AddMonths(3), kept.NextReviewDueAt);
        Assert.Equal(RiskArchiveReviewOutcome.KeepArchived, Assert.Single(kept.Reviews).Outcome);
        Assert.Equal((1, 0, 0), Summary(await archives.NotifyDueReviewsAsync()));

        now = now.AddMonths(3);
        Assert.Equal((1, 1, 1), Summary(await archives.NotifyDueReviewsAsync()));
        await publisher.Received(2).RiskArchiveReviewDueAsync(Arg.Any<Risk>(), Arg.Any<RiskArchive>());

        static (int, int, int) Summary(RiskArchiveReviewSweepSummary s) => (s.Live, s.Due, s.Notified);
    }

    /// <summary>Q2 — the review may reopen: origin quarterly review, status restored, flagged for review.</summary>
    [Fact]
    public async Task TestQ2_TheReviewMayReopen()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);

        var dto = await Archives.ReviewAsync(1, new RiskArchiveReviewRequest
            { Outcome = RiskArchiveReviewOutcome.Reopen, Note = "The kiosk is back on the corporate network." }, Cro);

        Assert.Equal((RiskArchiveState.Reopened, RiskArchiveReopenOrigin.QuarterlyReview, (int?)Cro),
            (dto.State, dto.ReopenOrigin, dto.ReopenedById));
        Assert.Null(Assert.Single(dto.Reviews).NextReviewDueAt);
        Assert.Equal("Mgmt Reviewed", RiskRow(1).Status);
        Assert.Contains("quarterly review", RiskRow(1).ReviewRequestedReason);
    }

    /// <summary>Q3 — keeping it archived is a decision: the owner may not, nor the third line; a note is required.</summary>
    [Fact]
    public async Task TestQ3_KeepingItIsADecisionWithItsGuards()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);
        var keep = new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Fine." };

        Assert.Equal("segregation_of_duties",
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ReviewAsync(1, keep, Owner))).RuleName);
        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ReviewAsync(1, keep, Auditor))).RuleName);
        Assert.Equal("Note", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Archives.ReviewAsync(1, new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.KeepArchived }, Cro)))
            .ParameterName);
        Assert.Equal("Outcome", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Archives.ReviewAsync(1, new RiskArchiveReviewRequest { Note = "x" }, Cro))).ParameterName);

        Assert.Empty(Read(ctx => ctx.RiskArchiveReviews.ToList()));
    }

    /// <summary>Q4 — a Gate A condition that appeared after archiving forbids keeping it archived; reopening stays open.</summary>
    [Fact]
    public async Task TestQ4_GateASinceArchivingForbidsKeepingIt()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);
        await Flags.DeclareAsync(1, RiskFlagCode.HumanSafety,
            new RiskFlagDeclarationRequest { Reason = "Now drives the door lock." }, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ReviewAsync(1,
            new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Keep." }, Cro));
        Assert.Equal("gate_a_non_discretionary", ex.RuleName);

        var reopened = await Archives.ReviewAsync(1,
            new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.Reopen, Note = "Gate A." }, Cro);
        Assert.Equal(RiskArchiveState.Reopened, reopened.State);
    }

    /// <summary>Q5 — a review needs a live archive.</summary>
    [Fact]
    public async Task TestQ5_AReviewNeedsALiveArchive()
    {
        ReviewedRisk(1);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Archives.ReviewAsync(1,
            new RiskArchiveReviewRequest { Outcome = RiskArchiveReviewOutcome.KeepArchived, Note = "Keep." }, Cro));

        Assert.Equal(RiskArchiveService.NotLiveRule, ex.RuleName);
    }

    // --- M1–M3: reopening by hand -------------------------------------------------------------------------------

    /// <summary>M1 — reopening by hand needs a reason, records who and why, and restores the risk.</summary>
    [Fact]
    public async Task TestM1_ReopeningByHand()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);

        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Archives.ReopenAsync(1, new RiskArchiveReopenRequest(), Cro))).ParameterName);

        var dto = await Archives.ReopenAsync(1, new RiskArchiveReopenRequest { Reason = "Audit finding 2026-14." }, Cro);

        Assert.Equal((RiskArchiveReopenOrigin.Manual, (int?)Cro, "Audit finding 2026-14."),
            (dto.ReopenOrigin, dto.ReopenedById, dto.ReopenReason));
        Assert.Equal("Mgmt Reviewed", RiskRow(1).Status);
        Assert.True(RiskRow(1).ReviewRequested);
        Assert.Contains(Audit(nameof(RiskArchive)), a => a.UserId == Cro && a.EntityId == dto.Id);
    }

    /// <summary>M2 — the third line does not reopen either.</summary>
    [Fact]
    public async Task TestM2_TheThirdLineCannotReopen()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Archives.ReopenAsync(1, new RiskArchiveReopenRequest { Reason = "I would like to." }, Auditor));

        Assert.Equal(ThirdLineAssurance.CannotApproveRule, ex.RuleName);
        Assert.Equal("Closed", RiskRow(1).Status);
    }

    /// <summary>M3 — archived again after a reopening: a new archive, the old one kept as the record.</summary>
    [Fact]
    public async Task TestM3_ArchivedAgainAfterAReopening()
    {
        ReviewedRisk(1);
        await Archives.ArchiveAsync(1, ArchiveRequest(), Author);
        await Archives.ReopenAsync(1, new RiskArchiveReopenRequest { Reason = "Re-scoped." }, Cro);
        await Archives.ArchiveAsync(1, ArchiveRequest(ReassessmentTriggerType.NewAiModel), Author);

        var history = await Archives.GetRiskArchivesAsync(1);

        Assert.Equal(new[] { RiskArchiveState.Live, RiskArchiveState.Reopened }, history.Select(h => h.State));
        Assert.Equal(2, Read(ctx => ctx.RiskDecisions.Count(d => d.RiskId == 1 && d.Decision == RiskDecisionKind.Archive)));
    }
}
