using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M56, T265 (S40 §7.3, §9.4): every persisted progress step stamps <c>heartbeat_at</c>, and the abandoned-run sweep measures
/// from <c>coalesce(heartbeat_at, started_at)</c> — a long run that still advances is alive, a silent one is settled.
/// </summary>
[TestSubject(typeof(IntegrationSyncLedger))]
public class IntegrationSyncHeartbeatTest : InMemoryServiceTestBase
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private void AddRun(int id, DateTime startedAt, DateTime? heartbeat) =>
        Seed(ctx => ctx.IntegrationSyncLogs.Add(new IntegrationSyncLog
        {
            Id = id, Integration = IntegrationKind.Tenable, ConnectionId = id, ConnectionName = $"c{id}", StartedAt = startedAt,
            HeartbeatAt = heartbeat, Status = IntegrationSyncStatus.Running
        }));

    [Fact]
    public async Task TestALongRunThatStillBeatsIsNotReaped()
    {
        AddRun(1, Now.AddHours(-5), Now.AddMinutes(-10));
        AddRun(2, Now.AddHours(-5), Now.AddHours(-3));
        AddRun(3, Now.AddHours(-3), null);

        await using (var db = OpenContext())
            Assert.Equal(2, await IntegrationSyncLedger.ReapAbandonedAsync(db, IntegrationKind.Tenable, Now));

        await using var check = OpenContext();
        Assert.Equal([IntegrationSyncStatus.Running, IntegrationSyncStatus.Failed, IntegrationSyncStatus.Failed],
            check.IntegrationSyncLogs.OrderBy(l => l.Id).Select(l => l.Status).ToList());
    }

    [Fact]
    public async Task TestTheGlobalSweepAlsoReadsTheHeartbeat()
    {
        AddRun(1, Now.AddHours(-5), Now.AddMinutes(-10));
        AddRun(2, Now.AddHours(-5), null);

        await using var db = OpenContext();
        var reaped = await IntegrationSyncLedger.ReapAllAsync(db, Now);

        Assert.Equal(2, Assert.Single(reaped).LogId);
    }

    [Fact]
    public async Task TestAPersistedStepStampsTheHeartbeat()
    {
        var tracker = GetService<IIntegrationSyncTracker>();
        await using var run = await tracker.BeginAsync(IntegrationKind.Tenable, 1, "c1", "Tenable");

        await run.StepAsync("assets", "chunk 1 read");
        await run.FlushAsync();

        await using var db = OpenContext();
        Assert.NotNull(db.IntegrationSyncLogs.Single().HeartbeatAt);
    }
}
