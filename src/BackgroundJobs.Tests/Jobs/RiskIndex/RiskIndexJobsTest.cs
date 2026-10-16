using System;
using System.Linq;
using System.Threading.Tasks;
using BackgroundJobs.Jobs;
using BackgroundJobs.Jobs.RiskIndex;
using BackgroundJobs.Tests.DI;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.RiskIndex;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Profiles;
using Hangfire;
using ServerServices.Services;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.RiskIndex;

/// <summary>
/// M53, T247 (S40 §9.1–§9.3, §9.6, §12.6): the index's snapshot and retention jobs as the job host composes them — on
/// the schedule, resolved by the activator, reading every entity under the host's unrestricted principal (T237), writing
/// a day once, and never throwing into Hangfire.
/// </summary>
[TestSubject(typeof(RiskIndexSnapshotJob))]
public class RiskIndexJobsTest : IDisposable
{
    private const int UnitA = 100, UnitB = 200;
    private static readonly DateTime Pilot = new(2026, 9, 1, 5, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _sp = JobHostContainer.Build(Guid.NewGuid().ToString());

    public void Dispose() => _sp.Dispose();

    private T Activate<T>() where T : class => (T)new HangfireActivator(_sp).ActivateJob(typeof(T))!;

    private DAL.Context.AuditableContext Unscoped() =>
        _sp.GetRequiredService<DalService>().GetContext(bypassEntityScope: true);

    /// <summary>"m h * * *" — minute and hour of a daily cron.</summary>
    private static TimeSpan At(string cron)
    {
        var parts = cron.Split(' ');
        Assert.Equal(new[] { "*", "*", "*" }, parts[2..]);
        return new TimeSpan(int.Parse(parts[1]), int.Parse(parts[0]), 0);
    }

    private TimeSpan Scheduled(string id) => At(Assert.Single(JobHostContainer.Schedule(_sp), j => j.Id == id).Cron);

    /// <summary>Two units, a host with a finding and a risk in each, and a profile in force since the pilot.</summary>
    private void SeedOrganization(bool withProfile = true)
    {
        using var db = Unscoped();
        foreach (var id in new[] { UnitA, UnitB })
        {
            db.Entities.Add(new Entity
            {
                Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.7", Status = "active",
                Created = Pilot.AddYears(-1), Updated = Pilot.AddYears(-1)
            });
            db.Hosts.Add(new Host
            {
                Id = id, HostName = $"srv-{id}", Source = "test", Status = 1, EntityId = id, Criticality = 4,
                RegistrationDate = Pilot.AddDays(-100)
            });
            db.Vulnerabilities.Add(new Vulnerability
            {
                Id = id, HostId = id, ImportSource = "nessus", Cvss3BaseScore = 8.1f, Cves = $"CVE-2026-{id}",
                LifecycleStatus = FindingStatus.Active, FirstDetection = DateTime.UtcNow.AddDays(-20),
                LastDetection = DateTime.UtcNow.AddDays(-1), Title = $"finding {id}", Status = 1, DetectionCount = 1
            });
            db.Risks.Add(new Risk
            {
                Id = id, EntityId = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}", Assessment = "",
                Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = Pilot.AddMonths(-6),
                LastUpdate = Pilot.AddMonths(-6), Owner = 1, SubmittedBy = 1
            });
            db.RiskScorings.Add(new RiskScoring { Id = id, CalculatedRisk = 7f, ResidualRisk = 6f, ClassicImpact = 4, ScoringMethod = 1 });
        }

        if (withProfile)
            db.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = 1, Version = 1, Name = "Pilot", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Active,
                Parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters()), Justification = "pilot",
                SubmittedAt = Pilot.AddDays(-7), ApprovedAt = Pilot.AddDays(-1), CommitteeDecisionRef = "CRTI-2026-09",
                EffectiveFrom = Pilot, CreatedAt = Pilot.AddDays(-10)
            });
        db.SaveChanges();
    }

    // --- schedule ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// S40 §9.1, §12.6 (<c>JobScheduleOrderTest</c>): the snapshot at 05:00, after the residual pass and both posture syncs
    /// and before the expiry pass and the digests; the retention at 02:40, after the governance retention.
    /// </summary>
    [Fact]
    public void TestTheSnapshotRunsAtFiveAfterItsInputsAndTheRetentionAtTwoForty()
    {
        var snapshot = Scheduled(RiskIndexSnapshotJob.JobId);
        Assert.Equal(new TimeSpan(5, 0, 0), snapshot);
        Assert.True(Scheduled("ResidualRiskCalculation") < snapshot);
        Assert.True(Scheduled("TrendMicroSync") < snapshot);
        Assert.True(Scheduled("SecurityScorecardSync") < snapshot);
        Assert.True(snapshot < Scheduled("RiskAcceptanceExpiry"));
        Assert.True(snapshot < Scheduled("SlaBreachNotification"));

        var retention = Scheduled(RiskIndexRetentionJob.JobId);
        Assert.Equal(new TimeSpan(2, 40, 0), retention);
        Assert.True(Scheduled("GovernanceRetention") < retention);
        Assert.True(retention < snapshot);
    }

    [Fact]
    public void TestTheJobHostRunsThreeWorkers() => Assert.Equal(3, ConfigurationManager.HangfireWorkerCount);

    /// <summary>T250 (S40 §9.1, §9.6): the previews run every minute, single-flight.</summary>
    [Fact]
    public void TestThePreviewRunsEveryMinuteOneAtATime()
    {
        var job = Assert.Single(JobHostContainer.Schedule(_sp), j => j.Id == RiskIndexPreviewJob.JobId);
        Assert.Equal("* * * * *", job.Cron);
        Assert.NotNull(typeof(RiskIndexPreviewJob).GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), true)
            .SingleOrDefault());
    }

    [Theory]
    [InlineData(typeof(RiskIndexSnapshotJob))]
    [InlineData(typeof(RiskIndexRetentionJob))]
    [InlineData(typeof(RiskIndexPreviewJob))]
    public void TestTheIndexJobsAreScheduledAndResolve(Type jobType)
    {
        Assert.Contains(JobHostContainer.Schedule(_sp), j => j.Type == jobType);
        Assert.IsAssignableFrom<BaseJob>(new HangfireActivator(_sp).ActivateJob(jobType));
    }

    // --- the job host's scope (T237) ---------------------------------------------------------------------------------

    /// <summary>Both jobs, as Hangfire builds them, read the index rows of every entity — the context the services use.</summary>
    [Fact]
    public void TestTheIndexJobsReadTheIndexRowsOfEveryEntity()
    {
        using (var db = Unscoped())
        {
            foreach (var entity in new int?[] { UnitA, UnitB, null })
                db.RiskIndexSnapshots.Add(new RiskIndexSnapshot
                {
                    SnapshotDate = new DateOnly(2026, 10, 1), ProfileId = 1, Mode = RiskIndexMode.Interim,
                    ScopeKind = entity is null ? RiskIndexScopeKind.Organization : RiskIndexScopeKind.Entity,
                    ScopeRef = entity?.ToString() ?? "org", EntityId = entity, Category = RiskIndexCategory.Icr,
                    ExternalInputsHash = new string('A', 64), CreatedAt = DateTime.UtcNow
                });
            db.RiskIndexClassDays.AddRange(
                new RiskIndexClassDay { SnapshotDate = new DateOnly(2026, 10, 1), ProfileId = 1, NodeEntityId = UnitA, TabGroup = RiskIndexTabGroup.Devices, Category = RiskIndexCategory.Exp, Value = 10, CreatedAt = DateTime.UtcNow },
                new RiskIndexClassDay { SnapshotDate = new DateOnly(2026, 10, 1), ProfileId = 1, NodeEntityId = UnitB, TabGroup = RiskIndexTabGroup.Devices, Category = RiskIndexCategory.Exp, Value = 10, CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        foreach (var job in new BaseJob[] { Activate<RiskIndexSnapshotJob>(), Activate<RiskIndexRetentionJob>() })
        {
            using var db = job.DalService.GetContext();
            Assert.Equal(3, db.RiskIndexSnapshots.Count());
            Assert.Equal(2, db.RiskIndexClassDays.Count());
        }

        using var services = _sp.GetRequiredService<IDalService>().GetContext();
        Assert.Equal(3, services.RiskIndexSnapshots.Count());
    }

    // --- the snapshot job ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheSnapshotJobWritesTheDayForEveryEntityOnceAndLogsIt()
    {
        SeedOrganization();

        var first = await Activate<RiskIndexSnapshotJob>().RunAsync();
        var second = await Activate<RiskIndexSnapshotJob>().RunAsync();

        Assert.Equal(RiskIndexSnapshotResult.Written, first!.Result);
        Assert.Equal(RiskIndexSnapshotResult.AlreadyComputed, second!.Result);

        await using var db = Unscoped();
        var date = first.Date;
        Assert.Contains(db.RiskIndexSnapshots, s => s.SnapshotDate == date && s.EntityId == UnitA && s.Category == RiskIndexCategory.Icr);
        Assert.Contains(db.RiskIndexSnapshots, s => s.SnapshotDate == date && s.EntityId == UnitB && s.Category == RiskIndexCategory.Icr);
        Assert.Contains(db.RiskIndexObjectDays, o => o.ObjectKind == RiskIndexObjectKind.Host && o.EntityId == UnitA);
        Assert.Contains(db.RiskIndexObjectDays, o => o.ObjectKind == RiskIndexObjectKind.Host && o.EntityId == UnitB);
        Assert.Contains(db.RiskIndexNodeMembers, m => m.NodeEntityId == UnitB && m.MemberKind == RiskIndexNodeMemberKind.Scope && m.MemberEntityId == UnitB);
        Assert.Equal(first.Counts!.Snapshots, db.RiskIndexSnapshots.Count());

        var log = Assert.Single(db.IntegrationSyncLogs, l => l.Integration == IntegrationKind.RiskIndex);
        Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
        Assert.Equal(first.Counts.Total, log.CreatedCount);
    }

    [Fact]
    public async Task TestWithoutAProfileTheSnapshotJobWritesNothingAndDoesNotFail()
    {
        SeedOrganization(withProfile: false);

        var outcome = await Activate<RiskIndexSnapshotJob>().RunAsync();

        Assert.Equal(RiskIndexSnapshotResult.NoProfileInForce, outcome!.Result);
        await using var db = Unscoped();
        Assert.Empty(db.RiskIndexSnapshots);
        Assert.Empty(db.IntegrationSyncLogs);
    }

    [Fact]
    public void TestAFailingSnapshotIsLoggedNotThrown()
    {
        var service = Substitute.For<IRiskIndexSnapshotService>();
        service.RunAsync(Arg.Any<DateTime>()).ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() => new RiskIndexSnapshotJob(TestDoubles.Logger(), TestDoubles.DalService(), service).Run()));
        service.Received(1).RunAsync(Arg.Any<DateTime>());
    }

    // --- the retention job -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRetentionJobPurgesOnlyPastTheWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = Unscoped())
        {
            foreach (var (date, reference) in new[] { (today.AddDays(-401), "old"), (today.AddDays(-1), "new") })
                db.RiskIndexObjectDays.Add(new RiskIndexObjectDay
                {
                    SnapshotDate = date, ProfileId = 1, ObjectKind = RiskIndexObjectKind.Host, ObjectRef = reference,
                    Category = RiskIndexCategory.Exp, Population = RiskIndexPopulation.Assets, Weight = 1, Criticality = 3,
                    CriticalitySource = "default", State = RiskIndexObjectState.Assessed, EntityId = UnitA,
                    Attribution = "[]", Factors = "[]", CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var counts = await Activate<RiskIndexRetentionJob>().RunAsync();

        Assert.Equal(1, counts!.ObjectDays);
        await using var check = Unscoped();
        Assert.Equal("new", Assert.Single(check.RiskIndexObjectDays).ObjectRef);
    }

    [Fact]
    public async Task TestThePreviewJobDrainsTheQueueAndStopsWhenIdle()
    {
        var service = Substitute.For<IRiskIndexPreviewService>();
        service.ProcessNextAsync(Arg.Any<DateTime>()).Returns(RiskIndexPreviewRunResult.Completed, RiskIndexPreviewRunResult.Failed,
            RiskIndexPreviewRunResult.Idle);

        var processed = await new RiskIndexPreviewJob(TestDoubles.Logger(), TestDoubles.DalService(), service).RunAsync();

        Assert.Equal(2, processed);
        await service.Received(3).ProcessNextAsync(Arg.Any<DateTime>());
    }

    [Fact]
    public void TestAFailingPreviewIsLoggedNotThrown()
    {
        var service = Substitute.For<IRiskIndexPreviewService>();
        service.ProcessNextAsync(Arg.Any<DateTime>()).ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() => new RiskIndexPreviewJob(TestDoubles.Logger(), TestDoubles.DalService(), service).Run()));
    }

    /// <summary>The job host's real graph computes a queued preview end to end (S40 §9.6).</summary>
    [Fact]
    public async Task TestThePreviewJobComputesAQueuedPreview()
    {
        SeedOrganization(withProfile: false);
        var parameters = RiskIndexParametersJson.Serialize(new RiskIndexParameters());
        await using (var db = Unscoped())
        {
            db.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = 9, Version = 1, Name = "Draft", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
                Parameters = parameters, CreatedAt = DateTime.UtcNow
            });
            db.RiskIndexPreviewRequests.Add(new RiskIndexPreviewRequest
            {
                ProfileId = 9, ParametersHash = RiskIndexPreviewService.ParametersHash(RiskIndexMode.Interim, parameters),
                Status = RiskIndexPreviewStatus.Queued, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await Activate<RiskIndexPreviewJob>().RunAsync());

        await using var check = Unscoped();
        var request = Assert.Single(check.RiskIndexPreviewRequests);
        Assert.Equal(RiskIndexPreviewStatus.Completed, request.Status);
        Assert.NotNull(request.Report);
    }

    [Fact]
    public void TestAFailingRetentionIsLoggedNotThrown()
    {
        var service = Substitute.For<IRiskIndexRetentionService>();
        service.ApplyAsync(Arg.Any<DateTime>()).ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() => new RiskIndexRetentionJob(TestDoubles.Logger(), TestDoubles.DalService(), service).Run()));
    }
}
