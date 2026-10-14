using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using SyncContracts;
using Tools.Security;
using WebSiteData;
using WebSiteData.Entities;
using WebSiteData.Services;
using Xunit;

namespace WebSite.Tests.Sync;

/// <summary>
/// The website half of the NR-2026-037 key: what <see cref="LocalIrpService"/> resolves and when it
/// refuses to queue. The controller-level behaviour is in <c>IRTEReportControllerTest</c>; these are the
/// branches a controller test cannot reach deterministically.
/// </summary>
[TestSubject(typeof(LocalIrpService))]
public class LocalIrpServiceTest : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();
    private static readonly string Key = global::Tools.RandomGenerator.RandomToken(IrpTaskReportLink.KeyBytes);

    public LocalIrpServiceTest()
    {
        using var db = _factory.CreateDbContext();
        db.IrpTasks.Add(new LocalIrpTask { Id = 10, PlanId = 1, Name = "Task" });
        db.IrpTaskExecutions.Add(new LocalIrpTaskExecution { Id = 7, TaskId = 10, Status = 1, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
    }

    public void Dispose() => _factory.Dispose();

    private void AddLink(string keyHash)
    {
        using var db = _factory.CreateDbContext();
        db.Links.Add(new LocalLink
        {
            Id = 1, Type = IrpTaskReportLink.Type, KeyHash = keyHash, ExpirationDate = DateTime.UtcNow.AddDays(1),
            DataJson = JsonSerializer.Serialize(new IrpTaskReportLinkData { TaskExecutionId = 7 })
        });
        db.SaveChanges();
    }

    /// <summary>Hands out contexts from the shared store, running a hook before the Nth one.</summary>
    private sealed class HookedFactory(SqliteDbContextFactory inner, int callNumber, Action hook)
        : IDbContextFactory<WebSiteDbContext>
    {
        private int _calls;

        public WebSiteDbContext CreateDbContext()
        {
            if (++_calls == callNumber) hook();
            return inner.CreateDbContext();
        }
    }

    [Fact]
    public async Task TestResolvesTheBoundExecution()
    {
        AddLink(LinkKeyHash.Primary(Key));

        var execution = await new LocalIrpService(_factory, new OutboxService(_factory)).GetTaskExecutionByReportKeyAsync(Key);

        Assert.Equal(7, execution?.Id);
    }

    [Fact]
    public async Task TestALinkStoredUnderTheLegacyMd5DigestDoesNotResolve()
    {
        // Password-reset lookups still accept the MD5 digest for links issued before NR-2026-014; report
        // links were never stored that way, so their lookup does not.
        AddLink(LinkKeyHash.Legacy(Key));

        var execution = await new LocalIrpService(_factory, new OutboxService(_factory)).GetTaskExecutionByReportKeyAsync(Key);

        Assert.Null(execution);
    }

    [Fact]
    public async Task TestAReportThatLosesTheRaceForItsKeyQueuesNothing()
    {
        AddLink(LinkKeyHash.Primary(Key));

        // Call 1 resolves the key; before call 2 (the consuming update) a concurrent report consumes it.
        var factory = new HookedFactory(_factory, 2, () =>
        {
            using var db = _factory.CreateDbContext();
            db.Links.Single().Consumed = true;
            db.SaveChanges();
        });
        var service = new LocalIrpService(factory, new OutboxService(_factory));

        Assert.False(await service.QueueTaskReportAsync(Key, (int)Model.IntStatus.Completed));

        using var check = _factory.CreateDbContext();
        Assert.Empty(check.Outbox);
    }
}
