using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using SyncContracts;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class SyncIngestServiceInMemoryTest : InMemoryServiceTestBase
{
    private ISyncIngestService BuildIngest() => new SyncIngestService(
        GetService<ILogger>(), GetService<IDalService>(),
        GetService<IFixRequestsService>(), GetService<ICommentsService>(), GetService<IMessagesService>(),
        GetService<IUsersService>(), GetService<ILinksService>(), GetService<IIncidentResponsePlansService>());

    private static OutboxActionDto PasswordChangeAction(Guid id, int userId, string password) => new()
    {
        ClientActionId = id,
        ActionType = SyncActionTypes.PasswordChange,
        ActionTimeUtc = DateTime.UtcNow,
        PayloadJson = JsonSerializer.Serialize(new PasswordChangeDto { UserId = userId, NewPassword = password })
    };

    [Fact]
    public async Task ApplyingSameActionTwiceIsIdempotent()
    {
        Seed(ctx => ctx.Users.Add(new User
        {
            Value = 500, Login = "tester", Name = "Tester", Type = "local", Enabled = true,
            Email = "t@x.io", Password = new byte[] { 1 }
        }));

        var ingest = BuildIngest();
        var actionId = Guid.NewGuid();
        var action = PasswordChangeAction(actionId, 500, "Abcdef1!");

        var first = await ingest.ApplyAsync(new[] { action });
        var second = await ingest.ApplyAsync(new[] { action });

        Assert.Contains(actionId, first);
        Assert.Contains(actionId, second); // re-acked, not re-applied

        await using var ctx = OpenContext();
        var ledger = ctx.ProcessedSyncActions.Count(p => p.ClientActionId == actionId.ToString());
        Assert.Equal(1, ledger);
    }

    [Fact]
    public async Task UnknownUserPasswordChangeIsNotAcked()
    {
        var ingest = BuildIngest();
        var action = PasswordChangeAction(Guid.NewGuid(), 99999, "Abcdef1!");

        var applied = await ingest.ApplyAsync(new[] { action });

        // ChangePassword returns false for a missing user → action stays un-acked for retry.
        Assert.Empty(applied);
    }
}
