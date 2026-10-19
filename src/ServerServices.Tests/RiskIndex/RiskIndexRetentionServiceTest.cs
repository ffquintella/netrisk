using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.RiskIndex;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M53, T247 (S40 §7.7, §9.6, §12.6): only past the windows — 400 days for object rows, attributions, class days and
/// frozen closures, 1825 for snapshots and the node members that authorize them — the JSON detail cleared after 400 days,
/// in batches.
/// </summary>
[TestSubject(typeof(RiskIndexRetentionService))]
public class RiskIndexRetentionServiceTest : InMemoryServiceTestBase
{
    private static readonly DateTime Now = new(2026, 10, 16, 2, 40, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private RiskIndexRetentionService Service(int batchSize = RiskIndexRetentionService.DefaultBatchSize) =>
        new(GetService<IDalService>(), GetService<Serilog.ILogger>()) { BatchSize = batchSize };

    private static RiskIndexSnapshot Snapshot(DateOnly date, string scopeRef, bool detail = true) => new()
    {
        SnapshotDate = date, ProfileId = 1, Mode = RiskIndexMode.Interim, ScopeKind = RiskIndexScopeKind.Entity,
        ScopeRef = scopeRef, EntityId = 10, Category = RiskIndexCategory.Icr, ExternalInputsHash = new string('A', 64),
        DisplayState = RiskIndexDisplayState.Point, TopContributions = detail ? "[]" : null,
        ChangeAttribution = detail ? "{}" : null, CreatedAt = Now
    };

    private static RiskIndexObjectDay ObjectDay(DateOnly date, string objectRef) => new()
    {
        SnapshotDate = date, ProfileId = 1, ObjectKind = RiskIndexObjectKind.Host, ObjectRef = objectRef,
        Category = RiskIndexCategory.Exp, Population = RiskIndexPopulation.Assets, Weight = 1, Criticality = 3,
        CriticalitySource = "default", State = RiskIndexObjectState.Assessed, EntityId = 10, Attribution = "[10]",
        Factors = "[]", CreatedAt = Now
    };

    private void SeedAges()
    {
        var past400 = Today.AddDays(-401);
        var within400 = Today.AddDays(-399);
        var past1825 = Today.AddDays(-1826);
        var within1825 = Today.AddDays(-1824);

        SeedUnscoped(ctx =>
        {
            for (var i = 0; i < 5; i++) ctx.RiskIndexObjectDays.Add(ObjectDay(past400, $"old-{i}"));
            ctx.RiskIndexObjectDays.Add(ObjectDay(within400, "recent"));

            ctx.RiskIndexObjectAttributions.AddRange(
                new RiskIndexObjectAttribution { SnapshotDate = past400, ProfileId = 1, ObjectKind = RiskIndexObjectKind.Host, ObjectRef = "old", AttributedEntityId = 10, ScopeEntityId = 10, CreatedAt = Now },
                new RiskIndexObjectAttribution { SnapshotDate = within400, ProfileId = 1, ObjectKind = RiskIndexObjectKind.Host, ObjectRef = "recent", AttributedEntityId = 10, ScopeEntityId = 10, CreatedAt = Now });

            ctx.RiskIndexClassDays.AddRange(
                new RiskIndexClassDay { SnapshotDate = past400, ProfileId = 1, NodeEntityId = 10, TabGroup = RiskIndexTabGroup.Devices, Category = RiskIndexCategory.Exp, Value = 40, CreatedAt = Now },
                new RiskIndexClassDay { SnapshotDate = within400, ProfileId = 1, NodeEntityId = 10, TabGroup = RiskIndexTabGroup.Devices, Category = RiskIndexCategory.Exp, Value = 40, CreatedAt = Now });

            ctx.RiskIndexSnapshots.AddRange(
                Snapshot(past1825, "10"), Snapshot(within1825, "10"), Snapshot(past400, "10"), Snapshot(within400, "10"));

            // The members of a snapshot past 400 days stay with it: a scoped official read still compares against them.
            ctx.RiskIndexNodeMembers.AddRange(
                new RiskIndexNodeMember { SnapshotDate = past1825, ProfileId = 1, NodeEntityId = 10, MemberKind = RiskIndexNodeMemberKind.Closure, MemberEntityId = 10, CreatedAt = Now },
                new RiskIndexNodeMember { SnapshotDate = past400, ProfileId = 1, NodeEntityId = 10, MemberKind = RiskIndexNodeMemberKind.Closure, MemberEntityId = 10, CreatedAt = Now });

            ctx.RiskIndexIncidentClosures.AddRange(
                new RiskIndexIncidentClosure { IncidentId = 1, ClosedAt = Now.AddDays(-401), FrozenOn = past400, CreatedAt = Now },
                new RiskIndexIncidentClosure { IncidentId = 2, ClosedAt = Now.AddDays(-30), FrozenOn = Today.AddDays(-29), CreatedAt = Now });
        });
    }

    [Fact]
    public async Task TestOnlyRowsPastTheirWindowAreRemoved()
    {
        SeedAges();

        var counts = await Service().ApplyAsync(Now);

        Assert.Equal(5, counts.ObjectDays);
        Assert.Equal(1, counts.Attributions);
        Assert.Equal(1, counts.ClassDays);
        Assert.Equal(1, counts.Snapshots);
        Assert.Equal(1, counts.NodeMembers);
        Assert.Equal(1, counts.IncidentClosures);

        await using var db = OpenContext();
        Assert.Equal("recent", Assert.Single(db.RiskIndexObjectDays).ObjectRef);
        Assert.Equal("recent", Assert.Single(db.RiskIndexObjectAttributions).ObjectRef);
        Assert.Equal(Today.AddDays(-399), Assert.Single(db.RiskIndexClassDays).SnapshotDate);
        Assert.Equal(3, db.RiskIndexSnapshots.Count());
        Assert.Equal(Today.AddDays(-401), Assert.Single(db.RiskIndexNodeMembers).SnapshotDate);
        Assert.Equal(2, Assert.Single(db.RiskIndexIncidentClosures).IncidentId);
    }

    [Fact]
    public async Task TestTheDetailOfSnapshotsPast400DaysIsClearedAndTheValuesKept()
    {
        SeedAges();

        var counts = await Service().ApplyAsync(Now);

        Assert.Equal(2, counts.DetailCleared); // the 1824- and the 401-day-old rows
        await using var db = OpenContext();
        var rows = db.RiskIndexSnapshots.AsNoTracking().ToList();
        Assert.All(rows.Where(r => r.SnapshotDate < Today.AddDays(-400)), r =>
        {
            Assert.Null(r.TopContributions);
            Assert.Null(r.ChangeAttribution);
            Assert.Equal(RiskIndexDisplayState.Point, r.DisplayState);
        });
        var recent = rows.Single(r => r.SnapshotDate == Today.AddDays(-399));
        Assert.NotNull(recent.TopContributions);
        Assert.NotNull(recent.ChangeAttribution);
    }

    [Fact]
    public async Task TestTheRemovalRunsInBatches()
    {
        SeedAges();
        var before = SaveChangesCount;

        var counts = await Service(batchSize: 2).ApplyAsync(Now);

        Assert.Equal(5, counts.ObjectDays);
        Assert.True(SaveChangesCount - before >= 3, $"{SaveChangesCount - before} saves");
        await using var db = OpenContext();
        Assert.Single(db.RiskIndexObjectDays);
    }

    [Fact]
    public async Task TestASecondPassFindsNothing()
    {
        SeedAges();
        await Service().ApplyAsync(Now);

        var counts = await Service().ApplyAsync(Now);

        Assert.Equal(0, counts.Deleted);
        Assert.Equal(0, counts.DetailCleared);
    }

    /// <summary>T250 (S40 §9.6): a finished preview keeps its report 90 days; the attached one lives on the profile.</summary>
    [Fact]
    public async Task TestPreviewReportsAreClearedAfterNinetyDays()
    {
        SeedUnscoped(ctx =>
        {
            ctx.RiskIndexProfiles.Add(new RiskIndexProfile
            {
                Id = 1, Version = 1, Name = "Draft", Mode = RiskIndexMode.Interim, Status = RiskIndexProfileStatus.Draft,
                Parameters = "{}", CreatedAt = Now.AddDays(-200)
            });
            foreach (var (id, finished) in new[] { (1, Now.AddDays(-91)), (2, Now.AddDays(-89)) })
                ctx.RiskIndexPreviewRequests.Add(new RiskIndexPreviewRequest
                {
                    Id = id, ProfileId = 1, ParametersHash = new string('A', 64), Status = RiskIndexPreviewStatus.Completed,
                    CreatedAt = finished, FinishedAt = finished, Report = "{}"
                });
        });

        var counts = await Service().ApplyAsync(Now);

        Assert.Equal(1, counts.PreviewReportsCleared);
        await using var db = OpenContext();
        Assert.Null(db.RiskIndexPreviewRequests.AsNoTracking().Single(r => r.Id == 1).Report);
        Assert.NotNull(db.RiskIndexPreviewRequests.AsNoTracking().Single(r => r.Id == 2).Report);
    }
}
