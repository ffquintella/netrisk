using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Findings;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// SLA policy, due dates and the notification digest (Track 3 milestone 3.4).
///
/// Two properties carry the milestone: a policy change must never rewrite a past compliance number,
/// and a finding crossing its deadline must notify exactly once. Both are easy to get subtly wrong
/// and impossible to notice in production until an auditor asks.
/// </summary>
[TestSubject(typeof(SlaService))]
public class SlaServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ISlaService _svc;

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    public SlaServiceInMemoryTest()
    {
        _svc = GetService<ISlaService>();

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "owner"));
            ctx.SlaConfigurations.AddRange(
                NewSla(4, 2, 15), NewSla(3, 5, 30), NewSla(2, 10, 60), NewSla(1, 15, 90));
        });
    }

    private static User NewUser(int id, string name) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@example.com"
    };

    private static SlaConfiguration NewSla(int severity, int triage, int remediation, int? entityId = null,
        DateTime? from = null) => new()
    {
        Severity = severity, MaxTriageDays = triage, MaxRemediationDays = remediation, EntityId = entityId,
        EffectiveFrom = from ?? Epoch, CreatedAt = Epoch
    };

    private static Vulnerability NewFinding(int id, NormalizedSeverity severity, DateTime firstSeen,
        DateTime? due = null, FindingStatus status = FindingStatus.Active, int? analystId = 1) => new()
    {
        Id = id,
        Title = $"Finding {id}",
        Severity = ((int)severity).ToString(),
        FirstDetection = firstSeen,
        LastDetection = firstSeen,
        DetectionCount = 1,
        Status = 1,
        LifecycleStatus = status,
        AnalystId = analystId,
        SlaDueDate = due
    };

    // --- policy resolution -------------------------------------------------------------------

    [Fact]
    public async Task TestResolvesThePolicyForASeverity()
    {
        var policy = await _svc.ResolveAsync(NormalizedSeverity.Critical, entityId: null, Now);

        Assert.NotNull(policy);
        Assert.Equal(15, policy!.MaxRemediationDays);
        Assert.Equal(2, policy.MaxTriageDays);
    }

    [Fact]
    public async Task TestInformationalFindingsHaveNoPolicyAndSoNoDeadline()
    {
        // An SLA on something nobody has to fix is noise.
        Assert.Null(await _svc.ResolveAsync(NormalizedSeverity.None, null, Now));
        Assert.Null(await _svc.ComputeDueDateAsync(NormalizedSeverity.None, null, Now));
    }

    [Fact]
    public async Task TestAnEntityOverrideBeatsTheGlobalDefault()
    {
        Seed(ctx => ctx.SlaConfigurations.Add(NewSla(4, 1, 7, entityId: 5)));

        var scoped = await _svc.ResolveAsync(NormalizedSeverity.Critical, entityId: 5, Now);
        var global = await _svc.ResolveAsync(NormalizedSeverity.Critical, entityId: 9, Now);

        Assert.Equal(7, scoped!.MaxRemediationDays);
        Assert.Equal(15, global!.MaxRemediationDays);
    }

    [Fact]
    public async Task TestDueDateUsesThePolicyInForceWhenTheFindingAppeared()
    {
        var lastYear = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // The policy tightened this year; a finding from last year keeps last year's deadline.
        await _svc.SetConfigurationAsync(new SlaConfiguration
        {
            Severity = 4, MaxTriageDays = 1, MaxRemediationDays = 7,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, userId: 1);

        Assert.Equal(lastYear.AddDays(15),
            await _svc.ComputeDueDateAsync(NormalizedSeverity.Critical, null, lastYear));

        Assert.Equal(Now.AddDays(7),
            await _svc.ComputeDueDateAsync(NormalizedSeverity.Critical, null, Now));
    }

    [Fact]
    public async Task TestSettingAPolicySupersedesRatherThanEdits()
    {
        await _svc.SetConfigurationAsync(new SlaConfiguration
        {
            Severity = 4, MaxTriageDays = 1, MaxRemediationDays = 7
        }, userId: 1);

        var all = await _svc.GetConfigurationsAsync(includeSuperseded: true);
        var critical = all.Where(c => c.Severity == 4).ToList();

        // Editing in place would silently rewrite last quarter's compliance figures.
        Assert.Equal(2, critical.Count);
        Assert.Single(critical.Where(c => c.EffectiveTo == null));
        Assert.Single(critical.Where(c => c.EffectiveTo != null));

        var current = await _svc.GetConfigurationsAsync();
        Assert.Equal(7, current.Single(c => c.Severity == 4).MaxRemediationDays);
    }

    [Fact]
    public async Task TestPolicyValidation()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetConfigurationAsync(new SlaConfiguration { Severity = 4, MaxTriageDays = 1, MaxRemediationDays = 0 }, 1));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetConfigurationAsync(new SlaConfiguration { Severity = 4, MaxTriageDays = 0, MaxRemediationDays = 7 }, 1));

        // A triage window longer than the remediation window describes a policy nobody can meet.
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetConfigurationAsync(new SlaConfiguration { Severity = 4, MaxTriageDays = 20, MaxRemediationDays = 15 }, 1));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetConfigurationAsync(new SlaConfiguration { Severity = 99, MaxTriageDays = 1, MaxRemediationDays = 7 }, 1));
    }

    // --- days overdue ------------------------------------------------------------------------

    [Fact]
    public void TestDaysOverdueIsDerivedAndFloorsAtZero()
    {
        var overdue = NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-5));
        var inTime = NewFinding(2, NormalizedSeverity.Critical, Now, due: Now.AddDays(5));

        Assert.Equal(5, overdue.DaysOverdue(Now));
        Assert.Equal(0, inTime.DaysOverdue(Now));
    }

    [Fact]
    public void TestSuppressedFindingsPauseTheClock()
    {
        var accepted = NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-60), due: Now.AddDays(-30),
            status: FindingStatus.RiskAccepted);

        // A finding nobody is allowed to work on should not accrue overdue days.
        Assert.Null(accepted.DaysOverdue(Now));
    }

    [Fact]
    public void TestAFindingWithNoDeadlineReportsNoOverdue()
    {
        Assert.Null(NewFinding(1, NormalizedSeverity.None, Now).DaysOverdue(Now));
    }

    // --- recompute ---------------------------------------------------------------------------

    [Fact]
    public async Task TestRecomputeMovesTheDeadlineAndRecordsWhy()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Low, Now.AddDays(-10), due: Now.AddDays(80))));

        Seed(ctx => ctx.Vulnerabilities.Single().Severity = ((int)NormalizedSeverity.Critical).ToString());

        var due = await _svc.RecomputeDueDateAsync(1, userId: 1);

        Assert.Equal(Now.AddDays(-10).AddDays(15), due);

        await using var db = OpenContext();
        // A deadline that moved is exactly the kind of thing an auditor asks about.
        Assert.Contains(db.FindingStatusHistories.ToList(),
            h => h.Justification != null && h.Justification.Contains("recomputed"));
    }

    [Fact]
    public async Task TestRecomputeOnAMissingFindingIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.RecomputeDueDateAsync(404, 1));
    }

    // --- notification digest (3.4.3) ---------------------------------------------------------

    [Fact]
    public async Task TestBreachedFindingsProduceOneDigestPerOwner()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(2, "other"));
            ctx.Vulnerabilities.Add(NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30),
                due: Now.AddDays(-3), analystId: 1));
            ctx.Vulnerabilities.Add(NewFinding(2, NormalizedSeverity.High, Now.AddDays(-40),
                due: Now.AddDays(-1), analystId: 1));
            ctx.Vulnerabilities.Add(NewFinding(3, NormalizedSeverity.High, Now.AddDays(-40),
                due: Now.AddDays(-1), analystId: 2));
        });

        var digests = await _svc.BuildNotificationDigestsAsync(Now);

        // One message per owner, not one per finding: per-finding alerting trains people to filter
        // the alerts, at which point the notification has negative value.
        Assert.Equal(2, digests.Count);

        var first = digests.Single(d => d.RecipientUserId == 1);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, i => Assert.Equal(0, i.ThresholdDays));
        Assert.Equal("owner@example.com", first.RecipientEmail);
    }

    [Fact]
    public async Task TestApproachingFindingsReportTheTightestThresholdCrossed()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-14), due: Now.AddDays(1))));

        var digests = await _svc.BuildNotificationDigestsAsync(Now);

        // T-1, not T-7: the tighter message is the more useful one.
        Assert.Equal(1, Assert.Single(Assert.Single(digests).Items).ThresholdDays);
    }

    [Fact]
    public async Task TestADigestIsNotSentTwiceForTheSameDeadline()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-3))));

        var first = await _svc.BuildNotificationDigestsAsync(Now);
        Assert.Single(first);

        await _svc.RecordNotificationsAsync(first, Now);

        // "Rerunning the job sends nothing new" — the spec's acceptance criterion.
        Assert.Empty(await _svc.BuildNotificationDigestsAsync(Now));
    }

    [Fact]
    public async Task TestMovingTheDeadlineReArmsTheNotification()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-3))));

        await _svc.RecordNotificationsAsync(await _svc.BuildNotificationDigestsAsync(Now), Now);

        Seed(ctx => ctx.Vulnerabilities.Single().SlaDueDate = Now.AddDays(-1));

        // The due date is part of the idempotence key precisely so a legitimately moved deadline
        // warns again rather than staying silent forever.
        Assert.Single(await _svc.BuildNotificationDigestsAsync(Now));
    }

    [Fact]
    public async Task TestSuppressedFindingsAreNotNotifiedAbout()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-60), due: Now.AddDays(-30),
                status: FindingStatus.RiskAccepted)));

        // Notifying about a deadline nobody is allowed to work towards is pure noise.
        Assert.Empty(await _svc.BuildNotificationDigestsAsync(Now));
    }

    [Fact]
    public async Task TestUnownedFindingsGetTheirOwnDigest()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(
            NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-3),
                analystId: null)));

        var digest = Assert.Single(await _svc.BuildNotificationDigestsAsync(Now));

        // An unowned breached critical is the one you most need to hear about, so it must not be
        // dropped for want of a recipient.
        Assert.Null(digest.RecipientUserId);
        Assert.Single(digest.Items);
    }

    [Fact]
    public async Task TestDigestListsBreachedBeforeApproaching()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewFinding(1, NormalizedSeverity.High, Now.AddDays(-10), due: Now.AddDays(2)));
            ctx.Vulnerabilities.Add(NewFinding(2, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-3)));
        });

        var digest = Assert.Single(await _svc.BuildNotificationDigestsAsync(Now));

        Assert.Equal(2, digest.Items[0].FindingId);
        Assert.Equal(0, digest.Items[0].ThresholdDays);
    }

    // --- compliance widget (3.4.2) -----------------------------------------------------------

    [Fact]
    public async Task TestComplianceCountsOpenFindingsBySeverity()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewFinding(1, NormalizedSeverity.Critical, Now.AddDays(-30), due: Now.AddDays(-3)));
            ctx.Vulnerabilities.Add(NewFinding(2, NormalizedSeverity.Critical, Now.AddDays(-5), due: Now.AddDays(10)));
            ctx.Vulnerabilities.Add(NewFinding(3, NormalizedSeverity.High, Now.AddDays(-5), due: Now.AddDays(25)));
            // Suppressed: not open work, so outside the measure entirely.
            ctx.Vulnerabilities.Add(NewFinding(4, NormalizedSeverity.Critical, Now.AddDays(-90),
                due: Now.AddDays(-60), status: FindingStatus.RiskAccepted));
        });

        var buckets = await _svc.GetComplianceBySeverityAsync(Now);

        var critical = buckets.Single(b => b.Severity == NormalizedSeverity.Critical);
        Assert.Equal(2, critical.Total);
        Assert.Equal(1, critical.Breached);
        Assert.Equal(50.0, critical.CompliancePercent);

        var high = buckets.Single(b => b.Severity == NormalizedSeverity.High);
        Assert.Equal(100.0, high.CompliancePercent);
    }

    [Fact]
    public async Task TestAnEmptyBandReportsNoComplianceRatherThanPerfection()
    {
        var buckets = await _svc.GetComplianceBySeverityAsync(Now);

        // 100% for a band with no findings reads as a result when it is an absence of data.
        Assert.All(buckets, b => Assert.Null(b.CompliancePercent));
    }

    // --- severity parsing --------------------------------------------------------------------

    [Theory]
    [InlineData("4", null, NormalizedSeverity.Critical)]
    [InlineData("High", null, NormalizedSeverity.High)]
    [InlineData("Moderate", null, NormalizedSeverity.Medium)]
    [InlineData(null, 9.5, NormalizedSeverity.Critical)]
    [InlineData(null, 5.0, NormalizedSeverity.Medium)]
    [InlineData("nonsense", 8.0, NormalizedSeverity.High)]
    [InlineData(null, null, NormalizedSeverity.None)]
    public void TestSeverityIsParsedFromWhicheverFormTheImporterWrote(string? raw, double? score,
        NormalizedSeverity expected)
    {
        // The register's severity column is free text carrying whatever the importing scanner wrote,
        // so all three forms have to be understood.
        Assert.Equal(expected, SlaService.ParseSeverity(raw, score));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    [InlineData(5, 7)]
    [InlineData(20, null)]
    public void TestThresholdSelection(int daysUntilDue, int? expected)
    {
        var due = Now.AddDays(daysUntilDue);

        Assert.Equal(expected, SlaService.ThresholdFor(due, Now, SlaService.DefaultApproachingThresholds));
    }
}
