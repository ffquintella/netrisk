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

    // --- comments from the public fix report (T375 review) -------------------------------------------------------

    private const int FixRequestId = 1;

    private void SeedFixRequest() => Seed(ctx =>
    {
        ctx.Vulnerabilities.Add(new Vulnerability
        {
            Id = 1, Title = "V", Status = 1, FirstDetection = new DateTime(2026, 1, 1),
            LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
        });
        ctx.FixRequests.Add(new FixRequest
        {
            Id = FixRequestId, VulnerabilityId = 1, Identifier = "key-1", CreationDate = new DateTime(2026, 1, 1)
        });
    });

    private static OutboxActionDto CommentAction(int fixRequestId, string text) => new()
    {
        ClientActionId = Guid.NewGuid(),
        ActionType = SyncActionTypes.CommentCreate,
        ActionTimeUtc = DateTime.UtcNow,
        PayloadJson = JsonSerializer.Serialize(new CommentCreateDto
        {
            FixRequestId = fixRequestId, UserId = null, IsAnonymous = true, CommenterName = "fixer@example.com",
            Text = text, Date = DateTime.UtcNow
        })
    };

    private int Ledger(OutboxActionDto action)
    {
        using var ctx = OpenContext();
        return ctx.ProcessedSyncActions.Count(p => p.ClientActionId == action.ClientActionId.ToString());
    }

    private int StoredComments()
    {
        using var ctx = OpenContext();
        return ctx.Comments.Count();
    }

    [Fact]
    public async Task ValidWebsiteCommentIsAppliedAndAcked()
    {
        SeedFixRequest();
        var action = CommentAction(FixRequestId, "fixed on the 3rd");

        var applied = await BuildIngest().ApplyAsync(new[] { action });

        Assert.Contains(action.ClientActionId, applied);
        Assert.Equal(1, StoredComments());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("form\ffeed")]
    [InlineData("vertical\vtab")]
    public async Task WebsiteCommentTheServiceRefusesIsRejectedAndAckedNotRetried(string text)
    {
        SeedFixRequest();
        var action = CommentAction(FixRequestId, text);

        var applied = await BuildIngest().ApplyAsync(new[] { action });

        Assert.Contains(action.ClientActionId, applied);
        Assert.Equal(1, Ledger(action));
        Assert.Equal(0, StoredComments());
    }

    [Fact]
    public async Task OverlongWebsiteCommentIsRejectedAndAcked()
    {
        SeedFixRequest();
        var action = CommentAction(FixRequestId, new string('a', CommentsService.MaxTextLength + 1));

        Assert.Contains(action.ClientActionId, await BuildIngest().ApplyAsync(new[] { action }));
        Assert.Equal(1, Ledger(action));
        Assert.Equal(0, StoredComments());
    }

    [Fact]
    public async Task WebsiteCommentOnAGoneFixRequestIsRejectedAndAcked()
    {
        var action = CommentAction(404, "fixed");

        var applied = await BuildIngest().ApplyAsync(new[] { action });

        Assert.Contains(action.ClientActionId, applied);
        Assert.Equal(1, Ledger(action));
        Assert.Equal(0, StoredComments());
    }

    // --- what the website is sent (T375 review) ------------------------------------------------------------------

    [Fact]
    public async Task PushNeverCarriesTheAuthorOfAnAnonymousComment()
    {
        SeedFixRequest();
        Seed(ctx =>
        {
            ctx.Comments.Add(new Comment
            {
                Id = 1, Type = "FixRequest", FixRequestId = FixRequestId, UserId = 7, IsAnonymous = true,
                Text = "anonymous", Date = new DateTime(2026, 1, 1)
            });
            ctx.Comments.Add(new Comment
            {
                Id = 2, Type = "FixRequest", FixRequestId = FixRequestId, UserId = 8, IsAnonymous = false,
                CommenterName = "Signed", Text = "signed", Date = new DateTime(2026, 1, 2)
            });
        });

        var push = new SyncPushBuilder(GetService<ILogger>(), GetService<IDalService>(),
            GetService<IFixRequestsService>(), GetService<ICommentsService>(), GetService<ITeamsService>(),
            GetService<IUsersService>(), GetService<IIncidentResponsePlansService>(), GetService<ILinksService>());

        var payload = await push.BuildBulkAsync();

        Assert.Null(Assert.Single(payload.Comments, c => c.Id == 1).UserId);
        Assert.Equal(8, Assert.Single(payload.Comments, c => c.Id == 2).UserId);
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
