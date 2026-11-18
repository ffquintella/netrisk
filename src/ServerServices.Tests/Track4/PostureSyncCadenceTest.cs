using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Integrations;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M57, T276 (S39 D-13): a posture sync is due by a fixed grid of interval-long slots, never by the end of the last run.
/// The pre-fix rule — last run's end plus the interval — made a daily job at 03:00 whose run ended at 03:20 skip the next
/// day, so a 24-hour interval synced every other day; the two service tests below fail on that code.
/// </summary>
[TestSubject(typeof(PostureSyncCadence))]
public class PostureSyncCadenceTest : InMemoryServiceTestBase
{
    private static readonly DateTime Day1 = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TestADailySlotEndsAtTheNextMidnightWhateverTimeTheRunEnded()
    {
        Assert.Equal(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), PostureSyncCadence.NextDue(Day1, 24));
        Assert.Equal(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), PostureSyncCadence.NextDue(Day1.AddHours(20), 24));
        Assert.True(PostureSyncCadence.IsDue(PostureSyncCadence.NextDue(Day1.AddMinutes(20), 24), Day1.AddDays(1)));
    }

    [Theory]
    [InlineData(6, "2026-11-10T06:00:00Z")]
    [InlineData(1, "2026-11-10T04:00:00Z")]
    [InlineData(0, "2026-11-10T04:00:00Z")]
    [InlineData(168, "2026-11-12T00:00:00Z")]
    [InlineData(500, "2026-11-12T00:00:00Z")]
    public void TestTheSlotFollowsTheClampedInterval(int hours, string expected)
    {
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), PostureSyncCadence.NextDue(Day1, hours));
    }

    [Fact]
    public void TestAConnectionNeverRunOnScheduleIsDueAndAFutureDueDateIsNot()
    {
        Assert.True(PostureSyncCadence.IsDue(null, Day1));
        Assert.True(PostureSyncCadence.IsDue(Day1, Day1));
        Assert.False(PostureSyncCadence.IsDue(Day1.AddSeconds(1), Day1));
    }

    [Fact]
    public async Task TestADailyVisionOneConnectionSyncsEveryDayAlthoughEachRunEndsAfterTheTick()
    {
        var svc = GetService<ITrendMicroService>();
        var view = await svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "V1", Region = "eu", BaseUrl = "", Enabled = true, SyncIntervalHours = 24, SyncVulnerabilities = false,
            SyncRiskScores = false
        }, "api-key");
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[{"id":"agent-1","name":"srv"}]}""");

        for (var day = 0; day < 7; day++)
        {
            var tick = Day1.AddDays(day);
            var result = await svc.SyncDueConnectionsAsync(tick);
            Assert.True(result.HostsCreated + result.HostsUpdated == 1, $"day {day} was skipped");

            // The run "ended" 20 minutes after the tick — the pre-fix rule counted the next due date from here.
            await EndedAtAsync(db => db.TrendMicroConnections.Single(c => c.Id == view.Id), tick.AddMinutes(20));
        }
    }

    [Fact]
    public async Task TestADailySecurityScorecardConnectionSyncsEveryDayAlthoughEachRunEndsAfterTheTick()
    {
        var svc = GetService<ISecurityScorecardService>();
        var view = await svc.CreateConnectionAsync(new SecurityScorecardConnection
        {
            Name = "SSC", Domain = "acme.com", BaseUrl = "https://api.securityscorecard.io", Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = false, SyncIssues = false, HistoryBackfillDays = 0
        }, "api-token");
        FakeOutboundHttpClient
            .RuleFor("/factors", """{"entries":[{"name":"dns_health","score":90}]}""")
            .RuleFor("/companies/acme.com", """{"domain":"acme.com","score":88,"grade":"B"}""");

        for (var day = 0; day < 7; day++)
        {
            var tick = Day1.AddDays(day);
            var result = await svc.SyncDueConnectionsAsync(tick);
            Assert.True(result.PostureRowsWritten == 2, $"day {day} was skipped");

            await EndedAtAsync(db => db.SecurityScorecardConnections.Single(c => c.Id == view.Id), tick.AddMinutes(20));
        }
    }

    [Fact]
    public async Task TestAManualSyncDoesNotMoveTheScheduledDueDate()
    {
        var svc = GetService<ITrendMicroService>();
        var view = await svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "V1", Region = "eu", BaseUrl = "", Enabled = true, SyncIntervalHours = 24, SyncVulnerabilities = false,
            SyncRiskScores = false
        }, "api-key");
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[{"id":"agent-1","name":"srv"}]}""");

        await svc.SyncDueConnectionsAsync(Day1);
        await svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Equal(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
            db.TrendMicroConnections.Single(c => c.Id == view.Id).NextDueAt);
    }

    private async Task EndedAtAsync<T>(Func<DAL.Context.AuditableContext, T> connection, DateTime endedAt)
    {
        await using var db = OpenContext();
        switch (connection(db))
        {
            case TrendMicroConnection v1: v1.LastSyncAt = endedAt; break;
            case SecurityScorecardConnection ssc: ssc.LastSyncAt = endedAt; break;
        }

        await db.SaveChangesAsync();
    }
}
