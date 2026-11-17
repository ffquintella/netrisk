using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// T375, service half: who becomes a fix request's requester when it is commented on, and who is told about the
/// comment. Both used to follow the author id the request body named.
/// </summary>
[TestSubject(typeof(CommentsService))]
public class CommentsServiceInMemoryTest : InMemoryServiceTestBase
{
    private const int Requester = 5;
    private const int Author = 7;
    private const int WebsiteFixer = 9;
    private const int Claimed = 1;
    private const int Unclaimed = 2;

    private readonly IMessagesService _messages = Substitute.For<IMessagesService>();
    private readonly IPermissionsService _permissions = Substitute.For<IPermissionsService>();
    private readonly CommentsService _service;

    public CommentsServiceInMemoryTest()
    {
        _service = new CommentsService(Log.Logger, GetService<IDalService>(), _messages, _permissions);
        _permissions.GetUserPermissionsAsync(Arg.Any<User>()).Returns(Task.FromResult(new List<string> { "vulnerabilities" }));

        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(new Vulnerability
            {
                Id = 1, Title = "V", Status = 1, FirstDetection = new DateTime(2026, 1, 1),
                LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
            });
            ctx.FixRequests.Add(NewFixRequest(Claimed, Requester));
            ctx.FixRequests.Add(NewFixRequest(Unclaimed, null));
        });
    }

    private static FixRequest NewFixRequest(int id, int? requester) => new()
    {
        Id = id, VulnerabilityId = 1, Identifier = $"key-{id}", CreationDate = new DateTime(2026, 1, 1),
        RequestingUserId = requester
    };

    private static User NewAuthor() => new() { Value = Author, Name = "Author", Admin = false };

    private int? RequesterOf(int fixRequestId)
    {
        using var db = OpenContext();
        return db.FixRequests.Single(f => f.Id == fixRequestId).RequestingUserId;
    }

    private int CommentCount()
    {
        using var db = OpenContext();
        return db.Comments.Count();
    }

    [Fact]
    public async Task TestUserCommentOnUnclaimedFixRequestMakesTheAuthenticatedAuthorTheRequester()
    {
        var comment = await _service.CreateUserCommentAsync(NewAuthor(), "FixRequest", null, false, "on it",
            Unclaimed, null, null, null);

        Assert.Equal(Author, comment.UserId);
        Assert.Equal(Author, RequesterOf(Unclaimed));
        await _messages.Received(1).SendMessageAsync(Arg.Any<string>(), Author, Arg.Any<int?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task TestUserCommentOnClaimedFixRequestKeepsAndNotifiesTheRequester()
    {
        await _service.CreateUserCommentAsync(NewAuthor(), "FixRequest", null, false, "on it",
            Claimed, null, null, null);

        Assert.Equal(Requester, RequesterOf(Claimed));
        await _messages.Received(1).SendMessageAsync(Arg.Any<string>(), Requester, Arg.Any<int?>(), Arg.Any<int>());
        await _messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Author, Arg.Any<int?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task TestUserCommentIsRefusedWithoutAReasonToSeeTheFixRequest()
    {
        _permissions.GetUserPermissionsAsync(Arg.Any<User>()).Returns(Task.FromResult(new List<string> { "hosts" }));

        // Not taking part is answered like a missing fix request, so the refusal is no existence oracle.
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.CreateUserCommentAsync(NewAuthor(),
            "FixRequest", null, false, "on it", Unclaimed, null, null, null));

        Assert.Null(RequesterOf(Unclaimed));
        Assert.Equal(0, CommentCount());
        await _messages.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Fact]
    public async Task TestWebsiteCommentDoesNotAdoptTheFixerItNamesAsRequester()
    {
        // The public fix report lets the fixer pick a name from a list; that id is not an authenticated identity.
        var comment = await _service.CreateCommentsAsync(WebsiteFixer, DateTime.UtcNow, null, "FixRequest", false,
            "fixer", "fixed", Unclaimed, null, null, null);

        Assert.Equal(WebsiteFixer, comment.UserId);
        Assert.Null(RequesterOf(Unclaimed));
        await _messages.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Fact]
    public async Task TestWebsiteCommentNotifiesTheStoredRequesterOnly()
    {
        await _service.CreateCommentsAsync(WebsiteFixer, DateTime.UtcNow, null, "FixRequest", true,
            "fixer@example.com", "fixed", Claimed, null, null, null);

        await _messages.Received(1).SendMessageAsync(Arg.Any<string>(), Requester, Arg.Any<int?>(), Arg.Any<int>());
        await _messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), WebsiteFixer, Arg.Any<int?>(),
            Arg.Any<int>());
    }

    [Fact]
    public async Task TestAnonymousWebsiteCommentOnUnclaimedFixRequestIsStoredWithoutNotifying()
    {
        // Before T375 this dereferenced a null requester and the website sync retried it forever.
        var comment = await _service.CreateCommentsAsync(null, DateTime.UtcNow, null, "FixRequest", true,
            "fixer@example.com", "fixed", Unclaimed, null, null, null);

        Assert.Null(comment.UserId);
        Assert.Equal(1, CommentCount());
        await _messages.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Fact]
    public async Task TestANotificationFailureDoesNotLoseTheComment()
    {
        _messages.SendMessageAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int>())
            .Returns(Task.FromException(new InvalidOperationException("message store down")));

        var comment = await _service.CreateUserCommentAsync(NewAuthor(), "FixRequest", null, false, "on it",
            Claimed, null, null, null);

        using var db = OpenContext();
        Assert.Equal(comment.Id, Assert.Single(db.Comments).Id);
    }

    [Fact]
    public async Task TestCommentOnUnknownFixRequestIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.CreateCommentsAsync(WebsiteFixer,
            DateTime.UtcNow, null, "FixRequest", false, "fixer", "fixed", 404, null, null, null));
    }

    [Fact]
    public async Task TestWebsiteCommenterNameIsBoundedByItsColumn()
    {
        var name = new string('a', CommentsService.MaxCommenterNameLength + 1);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => _service.CreateCommentsAsync(null,
            DateTime.UtcNow, null, "FixRequest", true, name, "fixed", Claimed, null, null, null));

        Assert.Equal("commenterName", ex.ParameterName);
    }

    [Fact]
    public async Task TestReturnedCommentCarriesNoNavigations()
    {
        var comment = await _service.CreateUserCommentAsync(NewAuthor(), "FixRequest", null, false, "on it",
            Claimed, null, null, null);

        Assert.Null(comment.FixRequest);
        Assert.Null(comment.User);
    }
}
