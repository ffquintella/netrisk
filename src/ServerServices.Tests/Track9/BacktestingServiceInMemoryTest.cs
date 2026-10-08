using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Monitoring;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 §8) — incident backtesting (T196) against the real model, scope filters and audit interceptor on the
/// EF in-memory provider: the outcomes B1–B4, the guards V1–V3, re-assessment B5, scope B6, the report B7 and the panel's
/// M9 (B8).
///
/// The case the track names for this stage is B1 (T199): a scenario registered after the incident is never counted as
/// foreseen — end to end, through the assessment, the incident's outcome, the report's rates and the metrics panel.
/// </summary>
[TestSubject(typeof(BacktestingService))]
public class BacktestingServiceInMemoryTest : DecisionCycleTestBase
{
    private static readonly DateTime Occurred = DateTime.UtcNow.Date.AddDays(-30).AddHours(10);

    private IBacktestingService Backtesting => GetService<IBacktestingService>();

    private void AddIncident(int id, int? unit = UnitA, DateTime? occurred = null, IncidentKind kind = IncidentKind.Incident)
    {
        var at = occurred ?? Occurred;
        SeedUnscoped(ctx => ctx.Incidents.Add(new Incident
        {
            Id = id, Name = $"2026-{id}", Description = "Ransomware on the kiosk.", CreatedById = Author, EntityId = unit,
            StartDate = at, ReportDate = at.AddHours(3), CreationDate = at.AddHours(4), LastUpdate = at.AddHours(4),
            Kind = kind, Year = 2026, Sequence = id
        }));
    }

    private void AddRiskAt(int id, DateTime registeredAt, int? unit = UnitA)
    {
        AddRisk(id, unit);
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == id).SubmissionDate = registeredAt);
    }

    private Task<BacktestIncidentDto> Assess(int incidentId, params int[] riskIds) =>
        Backtesting.AssessAsync(incidentId, new BacktestAssessmentRequest
        {
            RiskIds = riskIds.ToList(), NoCorrespondingScenario = riskIds.Length == 0, Note = "Post-incident review."
        }, Author);

    // --- B1–B4: the outcome --------------------------------------------------------------------------------------

    /// <summary>
    /// B1 (T199) — the incident matched only to a risk registered the day after it occurred reads "registered after the
    /// occurrence": not foreseen, in the incident, in the report's unforeseen rate and in the panel. Matched to a risk
    /// registered the day before, the same incident reads foreseen.
    /// </summary>
    [Fact]
    public async Task TestB1_APostIncidentRegistrationIsNeverCountedAsForeseen()
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(1));   // written after the fact
        AddRiskAt(2, Occurred.AddDays(-1));  // on the register before

        var after = await Assess(1, 1);

        Assert.Equal(BacktestOutcome.RegisteredAfterOccurrence, after.Outcome);
        var link = Assert.Single(after.Risks);
        Assert.False(link.RegisteredBeforeOccurrence);

        var report = await Backtesting.GetReportAsync(null, null, null);
        Assert.Equal((1, 0, 0), (report.RegisteredAfterOccurrence, report.ForeseenTreated, report.ForeseenDismissed));
        Assert.Equal(1d, report.UnforeseenRate);
        Assert.Null(report.FalseNegativeRate);

        var m9 = (await GetService<IMethodologyMetricsService>().GetAsync()).Metrics
            .Single(m => m.Metric == MethodologyMetric.ReopenedAndUnforeseen);
        Assert.Equal((MetricAvailability.Available, 1d, 1d, 1d),
            (m9.Availability, m9.Value!.Value, m9.Numerator!.Value, m9.Denominator!.Value));

        var before = await Assess(1, 2);
        Assert.Equal(BacktestOutcome.ForeseenTreated, before.Outcome);
        Assert.True(Assert.Single(before.Risks).RegisteredBeforeOccurrence);
    }

    /// <summary>B2 — every foreseeing risk had been cut when it happened: a false negative, with the reasons.</summary>
    [Fact]
    public async Task TestB2_AForeseenButCutRiskIsAFalseNegative()
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(-200));
        AddRiskAt(2, Occurred.AddDays(-100));
        SeedUnscoped(ctx =>
        {
            ctx.RiskDecisions.Add(new RiskDecision
            {
                RiskId = 1, Decision = RiskDecisionKind.MonitorAccept, Source = RiskDecisionSource.Declared,
                Reason = "Monitor.", DecidedAt = Occurred.AddDays(-150), CreatedAt = Occurred.AddDays(-150)
            });
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Name = "Exception", RiskId = 2, AuthorizingManagerId = Cro, BusinessJustification = "Insured.",
                StartDate = Occurred.AddDays(-60), ExpiresAt = Occurred.AddDays(30), CreatedAt = Occurred.AddDays(-60),
                Status = RiskAcceptanceStatus.Active
            });
        });

        var dto = await Assess(1, 1, 2);

        Assert.Equal(BacktestOutcome.ForeseenDismissed, dto.Outcome);
        Assert.Equal(new[] { BacktestClassifierReasons.Monitor }, dto.Risks.Single(r => r.RiskId == 1).DismissalReasons);
        Assert.Equal(new[] { BacktestClassifierReasons.Accepted }, dto.Risks.Single(r => r.RiskId == 2).DismissalReasons);

        var report = await Backtesting.GetReportAsync(null, null, null);
        Assert.Equal(1d, report.FalseNegativeRate);
        Assert.Equal(0d, report.UnforeseenRate);
    }

    /// <summary>B3 — an archive in force when the incident occurred counts as cut; one reopened before it does not.</summary>
    [Fact]
    public async Task TestB3_AnArchiveInForceIsACut()
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(-200));
        SeedUnscoped(ctx => ctx.RiskArchives.Add(new RiskArchive
        {
            RiskId = 1, Status = RiskArchiveStatus.Reopened, Justification = "x", PreviousStatus = "New",
            ArchivedAt = Occurred.AddDays(-100), NextReviewDueAt = Occurred.AddDays(-10), ReopenedAt = Occurred.AddDays(-5),
            ReopenOrigin = RiskArchiveReopenOrigin.Manual, ReopenReason = "x", CreatedAt = Occurred.AddDays(-100)
        }));

        Assert.Equal(BacktestOutcome.ForeseenTreated, (await Assess(1, 1)).Outcome);

        SeedUnscoped(ctx => ctx.RiskArchives.Single().ReopenedAt = Occurred.AddDays(5));
        var dto = await Backtesting.GetIncidentAsync(1);
        Assert.Equal(BacktestOutcome.ForeseenDismissed, dto.Outcome);
        Assert.Contains("archived", Assert.Single(dto.Risks).DismissalReasons);
    }

    /// <summary>B4 — no assessment is "not assessed", never "not foreseen"; stating that nothing corresponds is.</summary>
    [Fact]
    public async Task TestB4_NotAssessedIsNotNotForeseen()
    {
        AddIncident(1);
        AddIncident(2, kind: IncidentKind.NearMiss);

        Assert.Equal(BacktestOutcome.NotAssessed, (await Backtesting.GetIncidentAsync(1)).Outcome);

        var dto = await Assess(2);
        Assert.Equal(BacktestOutcome.NotForeseen, dto.Outcome);

        var report = await Backtesting.GetReportAsync(null, null, null);
        Assert.Equal((1, 1, 1, 1, 1), (report.Incidents, report.NearMisses, report.Assessed, report.NotAssessed,
            report.NotForeseen));
        Assert.Equal(0.5, report.AssessedShare);
    }

    // --- V1–V3: guards --------------------------------------------------------------------------------------------

    /// <summary>V1 — every invalid assessment is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("empty-without-statement", "NoCorrespondingScenario")]
    [InlineData("risks-and-statement", "NoCorrespondingScenario")]
    [InlineData("zero-id", "RiskIds")]
    [InlineData("too-many", "RiskIds")]
    [InlineData("long-note", "Note")]
    public async Task TestV1_AnInvalidAssessmentIsRefused(string scenario, string field)
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(-1));
        var request = scenario switch
        {
            "empty-without-statement" => new BacktestAssessmentRequest { RiskIds = [] },
            "risks-and-statement" => new BacktestAssessmentRequest { RiskIds = [1], NoCorrespondingScenario = true },
            "zero-id" => new BacktestAssessmentRequest { RiskIds = [0] },
            "too-many" => new BacktestAssessmentRequest { RiskIds = Enumerable.Range(1, 51).ToList() },
            _ => new BacktestAssessmentRequest { RiskIds = [1], Note = new string('x', 2001) }
        };

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Backtesting.AssessAsync(1, request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.IncidentBacktests.ToList()));
    }

    /// <summary>V2 — a missing incident or risk, or one outside the caller's scope, is not found, and nothing is written.</summary>
    [Fact]
    public async Task TestV2_AMissingOrInvisibleRecordIsNotFound()
    {
        AddIncident(1);
        AddIncident(2, unit: UnitB);
        AddRiskAt(1, Occurred.AddDays(-1));
        AddRiskAt(2, Occurred.AddDays(-1), unit: UnitB);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Assess(99, 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Assess(1, 1, 98));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Assess(2, 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Assess(1, 2));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Backtesting.GetIncidentAsync(2));

        ScopeToEverything();
        Assert.Empty(Read(ctx => ctx.IncidentBacktests.ToList()));
    }

    /// <summary>V3 — the report's period: 'to' before 'from', or longer than ten years, is refused.</summary>
    [Fact]
    public async Task TestV3_AnInvalidPeriodIsRefused()
    {
        Assert.Equal("to", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Backtesting.GetReportAsync(Occurred, Occurred.AddDays(-1), null))).ParameterName);
        Assert.Equal("from", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Backtesting.GetReportAsync(Occurred.AddDays(-3700), Occurred, null))).ParameterName);
        Assert.Equal("entityId", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Backtesting.GetReportAsync(null, null, 0))).ParameterName);
    }

    // --- B5–B8 ----------------------------------------------------------------------------------------------------

    /// <summary>B5 — re-assessing replaces the matches; the removed one stays in the trail, with who did it.</summary>
    [Fact]
    public async Task TestB5_ReassessingReplacesTheMatches()
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(-10));
        AddRiskAt(2, Occurred.AddDays(-10));

        await Assess(1, 1);
        var dto = await Assess(1, 2);

        Assert.Equal(new[] { 2 }, dto.Risks.Select(r => r.RiskId));
        Assert.Single(Read(ctx => ctx.IncidentBacktests.ToList()));
        Assert.Contains(Audit(nameof(IncidentBacktestRisk)), a => a.Action == AuditLogAction.Delete);
        Assert.All(Audit(nameof(IncidentBacktestRisk)), a => Assert.Equal(Author, a.UserId));
    }

    /// <summary>
    /// B6 — the outcome does not depend on who asks: a unit that cannot see the risk the incident was matched to still
    /// reads "foreseen", with the match counted and not disclosed.
    /// </summary>
    [Fact]
    public async Task TestB6_TheOutcomeDoesNotDependOnWhoAsks()
    {
        AddIncident(1, unit: UnitA);
        AddRiskAt(5, Occurred.AddDays(-10), unit: UnitB);
        await Assess(1, 5);

        ScopeTo(UnitA);
        var dto = await Backtesting.GetIncidentAsync(1);

        Assert.Equal(BacktestOutcome.ForeseenTreated, dto.Outcome);
        Assert.Empty(dto.Risks);
        Assert.Equal(1, dto.HiddenRiskCount);
    }

    /// <summary>B7 — the report covers the incidents that occurred in the period, of the entity asked, the newest first.</summary>
    [Fact]
    public async Task TestB7_TheReportCoversThePeriodAndTheEntity()
    {
        AddIncident(1, occurred: Occurred);
        AddIncident(2, occurred: Occurred.AddDays(-400));
        AddIncident(3, unit: UnitB, occurred: Occurred.AddDays(-1));
        AddIncident(4, occurred: Occurred.AddDays(-2));

        var lastYear = await Backtesting.GetReportAsync(null, null, null);
        Assert.Equal(new[] { 1, 3, 4 }, lastYear.Items.Select(i => i.IncidentId));
        Assert.False(lastYear.Truncated);

        var unitA = await Backtesting.GetReportAsync(Occurred.AddDays(-3), Occurred.AddDays(1), UnitA);
        Assert.Equal(new[] { 1, 4 }, unitA.Items.Select(i => i.IncidentId));

        ScopeTo(UnitA);
        Assert.DoesNotContain((await Backtesting.GetReportAsync(null, null, null)).Items, i => i.IncidentId == 3);
    }

    /// <summary>B8 — M9 in the panel also reports the archives reopened in the period.</summary>
    [Fact]
    public async Task TestB8_ThePanelReportsTheReopenedArchives()
    {
        ReviewedRisk(1);
        ReviewedRisk(2);
        var archives = GetService<IRiskArchiveService>();
        await archives.ArchiveAsync(1, ArchiveRequest(), Author);
        await archives.ArchiveAsync(2, ArchiveRequest(), Author);
        await archives.ReopenAsync(1, new RiskArchiveReopenRequest { Reason = "Back in scope." }, Cro);

        var m9 = (await GetService<IMethodologyMetricsService>().GetAsync()).Metrics
            .Single(m => m.Metric == MethodologyMetric.ReopenedAndUnforeseen);

        Assert.Equal(MetricAvailability.Available, m9.Availability);
        Assert.Null(m9.Value);
        Assert.Contains("1 of 2 live in the period were reopened (0 by a condition, 0 at a review, 1 by hand)", m9.Detail);
    }

    /// <summary>
    /// B9 (S50 R11, regression) — back-dating the risk's submission through the risk editor does not make it foresee an
    /// incident it was written after: the registration date is not editable.
    /// </summary>
    [Fact]
    public async Task TestB9_BackDatingTheRiskDoesNotMakeItForeseen()
    {
        AddIncident(1);
        AddRiskAt(1, Occurred.AddDays(5));
        await Assess(1, 1);

        var edited = RiskRow(1);
        edited.SubmissionDate = Occurred.AddDays(-30);
        await Risks.SaveRiskAsync(edited);

        Assert.Equal(Occurred.AddDays(5), RiskRow(1).SubmissionDate);
        Assert.Equal(BacktestOutcome.RegisteredAfterOccurrence, (await Backtesting.GetIncidentAsync(1)).Outcome);
    }

    /// <summary>
    /// B10 (S50 R11, regression) — moving the incident's creation later through the incident editor does not move its
    /// occurrence past a risk written after it.
    /// </summary>
    [Fact]
    public async Task TestB10_MovingTheIncidentLaterDoesNotMakeAPostIncidentRiskForeseen()
    {
        AddIncident(1);
        SeedUnscoped(ctx =>
        {
            var incident = ctx.Incidents.Single(i => i.Id == 1);
            incident.StartDate = null;
            incident.ReportDate = Occurred.AddDays(30);
        });
        AddRiskAt(1, Occurred.AddDays(10));
        await Assess(1, 1);

        var incident = Read(ctx => ctx.Incidents.Single(i => i.Id == 1));
        var created = incident.CreationDate;
        incident.CreationDate = Occurred.AddDays(60);
        await GetService<IIncidentsService>().UpdateAsync(incident, Read(ctx => ctx.Users.Single(u => u.Value == Author)));

        Assert.Equal(created, Read(ctx => ctx.Incidents.Single(i => i.Id == 1)).CreationDate);
        Assert.Equal(BacktestOutcome.RegisteredAfterOccurrence, (await Backtesting.GetIncidentAsync(1)).Outcome);
    }

    /// <summary>The reason strings the classifier writes, named here so the assertions read as the outcome they check.</summary>
    private static class BacktestClassifierReasons
    {
        public const string Monitor = Tools.DecisionCycle.BacktestClassifier.DecidedMonitorAccept;
        public const string Accepted = Tools.DecisionCycle.BacktestClassifier.Accepted;
    }
}
