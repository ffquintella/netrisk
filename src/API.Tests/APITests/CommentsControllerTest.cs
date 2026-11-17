using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// <c>/Comments</c>. The create route is covered end to end — the real controller over the real
/// <see cref="CommentsService"/> on an in-memory database — for T375: the author used to come from the request body,
/// and nothing checked the caller could see the record. The logged-in user is <c>testUser</c>, id 1.
/// </summary>
[TestSubject(typeof(CommentsController))]
public class CommentsControllerTest : BaseControllerTest
{
    private const int EntityA = 3;
    private const int EntityB = 4;
    private const int Caller = 1;
    private const int Spoofed = 999;

    private const int RiskId = 10;
    private const int OtherEntityRiskId = 11;
    private const int VulnerabilityId = 20;
    private const int OtherEntityVulnerabilityId = 21;
    private const int HostId = 30;
    private const int FixRequestId = 40;
    private const int UnclaimedFixRequestId = 41;
    private const int TeamFixRequestId = 42;
    private const int AnalystFixRequestId = 43;
    private const int Requester = 5;
    private const int TeamId = 7;

    private readonly ICommentsService _commentsService = Substitute.For<ICommentsService>();
    private readonly CommentsController _controller;

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IPermissionsService _permissions = Substitute.For<IPermissionsService>();
    private readonly IMessagesService _messages = Substitute.For<IMessagesService>();
    private readonly HashSet<int> _seededComments = [];

    private static Comment SampleComment(int id) => new()
    {
        Id = id,
        UserId = 1,
        IsAnonymous = false,
        CommenterName = "testUser",
        Date = new DateTime(2024, 1, 1),
        Type = "FixRequest",
        Text = "comment " + id,
        FixRequestId = 4
    };

    public CommentsControllerTest()
    {
        _commentsService.GetUserCommentsAsync(1).Returns(new List<Comment>
        {
            SampleComment(1),
            SampleComment(2)
        });
        _commentsService.GetFixRequestCommentsForUserAsync(Arg.Any<User>(), 4)
            .Returns(new List<Comment> { SampleComment(3) });
        _commentsService.GetFixRequestCommentsForUserAsync(Arg.Any<User>(), 999).Returns(new List<Comment>());
        _commentsService.CreateUserCommentAsync(
                Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(SampleComment(9));

        _controller = ResolveController<CommentsController>(s => s.AddSingleton(_commentsService));

        Grant();
        SeedRecords();
    }

    // --- reads ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestGetUserComments()
    {
        var result = await _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Comment>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetUserCommentsIgnoresChatFilter()
    {
        var result = await _controller.Get(new List<int?> { 1, 2 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsType<List<Comment>>(ok.Value).Count);
    }

    [Fact]
    public async Task TestGetHostComments()
    {
        _commentsService.GetHostCommentsAsync(7).Returns(new List<Comment>
        {
            new() { Id = 5, Type = "Host", HostId = 7, Text = "h" }
        });

        var result = await _controller.GetHostComments(7);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Comment>>(ok.Value);
        Assert.Equal(5, Assert.Single(list).Id);
    }

    [Fact]
    public async Task TestGetFixRequestComments()
    {
        var result = await _controller.Get(4);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Comment>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(3, list[0].Id);
    }

    [Fact]
    public async Task TestGetFixRequestCommentsWhenNone()
    {
        var result = await _controller.Get(999);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsType<List<Comment>>(ok.Value));
    }

    // --- create: what the controller hands the service -----------------------------------------------------------

    [Theory]
    [InlineData("Risk")]
    [InlineData("Vulnerability")]
    [InlineData("FixRequest")]
    [InlineData("Host")]
    public async Task TestCreatePassesTheAuthenticatedUserToTheService(string type)
    {
        var spoofed = new Comment
        {
            Type = type, Text = "x", UserId = Spoofed, CommenterName = "Mallory", Date = new DateTime(2001, 1, 1),
            FixRequestId = 4, RiskId = 5, VulnerabilityId = 6, HostId = 7
        };

        var result = await _controller.CreateAsync(spoofed);

        Assert.IsType<OkObjectResult>(result.Result);
        await _commentsService.Received(1).CreateUserCommentAsync(
            Arg.Is<User>(u => u.Value == Caller), type, null, false, "x", 4, 5, 6, 7);
        await _commentsService.DidNotReceiveWithAnyArgs().CreateCommentsAsync(
            default, default, default, default!, default, default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task TestCreateAsync()
    {
        var result = await _controller.CreateAsync(SampleComment(0));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(9, Assert.IsType<Comment>(ok.Value).Id);
        await _commentsService.Received(1).CreateUserCommentAsync(
            Arg.Is<User>(u => u.Value == Caller), "FixRequest", null, false, "comment 0", 4, null, null, null);
    }

    [Fact]
    public async Task TestCreateAnonymousComment()
    {
        var comment = SampleComment(0);
        comment.IsAnonymous = true;

        var result = await _controller.CreateAsync(comment);

        Assert.IsType<OkObjectResult>(result.Result);
        await _commentsService.Received(1).CreateUserCommentAsync(
            Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<int?>(), true, Arg.Any<string?>(),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task TestCreateWithoutBodyIsBadRequest()
    {
        var result = await _controller.CreateAsync(null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestCreateUnexpectedFailureIsA500WithoutTheMessage()
    {
        _commentsService.CreateUserCommentAsync(
                Arg.Any<User>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<string?>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(Task.FromException<Comment>(new InvalidOperationException("db password is hunter2")));

        var result = await _controller.CreateAsync(SampleComment(0));

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // --- create, end to end: authorship --------------------------------------------------------------------------

    [Theory]
    [InlineData("Risk")]
    [InlineData("Vulnerability")]
    [InlineData("FixRequest")]
    [InlineData("Host")]
    public async Task TestCreateIsAttributedToTheCallerWhateverTheBodySays(string type)
    {
        Grant("riskmanagement", "vulnerabilities", "hosts");

        var result = await Controller().CreateAsync(Body(type, userId: Spoofed, commenterName: "Mallory",
            date: new DateTime(2001, 1, 1)));

        var created = Assert.IsType<Comment>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var stored = Assert.Single(StoredComments(type));
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal(Caller, stored.UserId);
        Assert.Equal("testUser", stored.CommenterName);
        Assert.NotEqual(2001, stored.Date.Year);
    }

    [Fact]
    public async Task TestCreateStoresOnlyTheRecordTheTypeNames()
    {
        Grant("hosts");

        var body = Body("Host");
        body.RiskId = OtherEntityRiskId;
        body.VulnerabilityId = OtherEntityVulnerabilityId;
        body.FixRequestId = FixRequestId;

        Assert.IsType<OkObjectResult>((await Controller().CreateAsync(body)).Result);

        var stored = Assert.Single(StoredComments("Host"));
        Assert.Equal(HostId, stored.HostId);
        Assert.Null(stored.RiskId);
        Assert.Null(stored.VulnerabilityId);
        Assert.Null(stored.FixRequestId);
    }

    [Fact]
    public async Task TestCreateAnonymousCommentKeepsTheRealAuthorAndShowsNoName()
    {
        Grant("hosts");

        var body = Body("Host", commenterName: "Somebody else");
        body.IsAnonymous = true;

        Assert.IsType<OkObjectResult>((await Controller().CreateAsync(body)).Result);

        var stored = Assert.Single(StoredComments("Host"));
        Assert.True(stored.IsAnonymous);
        Assert.Equal(Caller, stored.UserId);
        Assert.Null(stored.CommenterName);
    }

    [Fact]
    public async Task TestCreateOnUnclaimedFixRequestMakesTheCallerTheRequesterNotTheBodyUser()
    {
        Grant("vulnerabilities");

        var body = Body("FixRequest", userId: Spoofed);
        body.FixRequestId = UnclaimedFixRequestId;

        Assert.IsType<OkObjectResult>((await Controller().CreateAsync(body)).Result);

        Assert.Equal(Caller, StoredFixRequest(UnclaimedFixRequestId).RequestingUserId);
        await _messages.Received(1).SendMessageAsync(Arg.Any<string>(), Caller, Arg.Any<int?>(), Arg.Any<int>());
        await _messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Spoofed, Arg.Any<int?>(), Arg.Any<int>());
    }

    [Fact]
    public async Task TestCreateOnFixRequestNotifiesTheStoredRequesterOnly()
    {
        Grant("vulnerabilities");

        Assert.IsType<OkObjectResult>((await Controller().CreateAsync(Body("FixRequest", userId: Spoofed))).Result);

        Assert.Equal(Requester, StoredFixRequest(FixRequestId).RequestingUserId);
        await _messages.Received(1).SendMessageAsync(Arg.Any<string>(), Requester, Arg.Any<int?>(), Arg.Any<int>());
        await _messages.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Spoofed, Arg.Any<int?>(), Arg.Any<int>());
    }

    // --- create, end to end: object-level authorization ----------------------------------------------------------

    [Theory]
    [InlineData("Host", "riskmanagement", "vulnerabilities")]
    [InlineData("Risk", "hosts", "vulnerabilities")]
    [InlineData("Vulnerability", "hosts", "riskmanagement")]
    public async Task TestCreateWithoutTheRecordsPermissionIsForbidden(string type, string held1, string held2)
    {
        Grant(held1, held2);

        var result = await Controller().CreateAsync(Body(type));

        AssertForbidden(result);
        Assert.Empty(StoredComments(type));
        await _messages.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Theory]
    [InlineData("Risk", OtherEntityRiskId)]
    [InlineData("Vulnerability", OtherEntityVulnerabilityId)]
    public async Task TestCreateOnARecordOutsideTheCallersEntitiesIsNotFound(string type, int id)
    {
        Grant("riskmanagement", "vulnerabilities");
        _dal.Scope = EntityScope.ForEntities([EntityA]);

        var body = Body(type);
        body.RiskId = body.VulnerabilityId = id;

        Assert.IsType<NotFoundResult>((await Controller(admin: true).CreateAsync(body)).Result);
        Assert.Empty(StoredComments(type));
    }

    [Fact]
    public async Task TestCreateOnAFixRequestOfAnotherEntitysFindingIsNotFound()
    {
        Grant("vulnerabilities");
        _dal.Scope = EntityScope.ForEntities([EntityB]);

        Assert.IsType<NotFoundResult>((await Controller(admin: true).CreateAsync(Body("FixRequest"))).Result);
        Assert.Empty(StoredComments("FixRequest"));
    }

    [Theory]
    [InlineData(FixRequestId)]     // requested by user 5
    [InlineData(TeamFixRequestId)] // sent to team 7, which the caller is not in
    public async Task TestCreateOnSomebodyElsesFixRequestIsNotFound(int fixRequestId)
    {
        Grant("hosts", "riskmanagement");

        var body = Body("FixRequest");
        body.FixRequestId = fixRequestId;

        // The same answer as an unknown fix request, so the refusal confirms nothing about which ids exist.
        Assert.IsType<NotFoundResult>((await Controller().CreateAsync(body)).Result);
        Assert.Empty(StoredComments("FixRequest"));
        await _messages.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default);
    }

    [Theory]
    [InlineData(FixRequestId, Caller, false, false)]       // the caller requested it
    [InlineData(TeamFixRequestId, Requester, true, false)] // the caller is in the team it was sent to
    [InlineData(AnalystFixRequestId, Requester, false, true)] // the caller is the finding's analyst
    public async Task TestCreateOnFixRequestIsAllowedToItsParticipants(int fixRequestId, int requester, bool inTeam,
        bool analyst)
    {
        Grant();
        SeedParticipant(fixRequestId, requester, inTeam, analyst);

        var body = Body("FixRequest");
        body.FixRequestId = fixRequestId;

        Assert.IsType<OkObjectResult>((await Controller().CreateAsync(body)).Result);
        Assert.Equal(Caller, Assert.Single(StoredComments("FixRequest")).UserId);
    }

    [Theory]
    [InlineData("Risk")]
    [InlineData("Vulnerability")]
    [InlineData("FixRequest")]
    [InlineData("Host")]
    public async Task TestCreateOnAnUnknownRecordIsNotFound(string type)
    {
        var body = Body(type);
        body.RiskId = body.VulnerabilityId = body.FixRequestId = body.HostId = 12345;

        Assert.IsType<NotFoundResult>((await Controller(admin: true).CreateAsync(body)).Result);
        Assert.Empty(StoredComments(type));
    }

    // --- create, end to end: validation --------------------------------------------------------------------------

    [Fact]
    public async Task TestCreateReplyToACommentOnAnotherRecordIsBadRequest()
    {
        var elsewhere = SeedComment(new Comment { Type = "Host", HostId = HostId + 1, Text = "other host" });

        var body = Body("Host");
        body.ReplyTo = elsewhere;

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "replyTo");
        Assert.Empty(StoredComments("Host"));
    }

    [Fact]
    public async Task TestCreateReplyToACommentOfAnotherTypeIsBadRequest()
    {
        var onRisk = SeedComment(new Comment { Type = "Risk", RiskId = HostId, Text = "same id, other kind" });

        var body = Body("Host");
        body.ReplyTo = onRisk;

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "replyTo");
    }

    [Fact]
    public async Task TestCreateReplyToAnUnknownCommentIsBadRequest()
    {
        var body = Body("Host");
        body.ReplyTo = 4242;

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "replyTo");
    }

    [Fact]
    public async Task TestCreateReplyToACommentOnTheSameRecordIsStored()
    {
        var parent = SeedComment(new Comment { Type = "Host", HostId = HostId, Text = "first" });

        var body = Body("Host");
        body.ReplyTo = parent;

        var created = Assert.IsType<Comment>(
            Assert.IsType<OkObjectResult>((await Controller(admin: true).CreateAsync(body)).Result).Value);
        Assert.Equal(parent, created.ReplyTo);
    }

    [Theory]
    [InlineData("Incident")]
    [InlineData("host")]
    [InlineData("")]
    [InlineData(null)]
    public async Task TestCreateWithAnUnknownTypeIsBadRequest(string? type)
    {
        var body = Body("Host");
        body.Type = type;

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "type");
    }

    [Fact]
    public async Task TestCreateWithoutTheTargetIdIsBadRequest()
    {
        var body = Body("Risk");
        body.RiskId = null;

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "riskId");
    }

    [Fact]
    public async Task TestCreateWithOverlongTextIsBadRequest()
    {
        var body = Body("Host", text: new string('a', CommentsService.MaxTextLength + 1));

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "text");
        Assert.Empty(StoredComments("Host"));
    }

    [Fact]
    public async Task TestCreateWithTextAtTheLimitIsStored()
    {
        var body = Body("Host", text: new string('a', CommentsService.MaxTextLength));

        Assert.IsType<OkObjectResult>((await Controller(admin: true).CreateAsync(body)).Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TestCreateWithoutTextIsBadRequest(string? text)
    {
        AssertInvalid(await Controller(admin: true).CreateAsync(Body("Host", text: text)), "text");
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0x1b)]
    [InlineData(0x7f)]
    public async Task TestCreateWithControlCharactersIsBadRequest(int code)
    {
        var body = Body("Host", text: "before" + (char)code + "after");

        AssertInvalid(await Controller(admin: true).CreateAsync(body), "text");
        Assert.Empty(StoredComments("Host"));
    }

    [Fact]
    public async Task TestCreateKeepsLineBreaksAndTabs()
    {
        var body = Body("Host", text: "line one\r\nline two\tindented");

        Assert.IsType<OkObjectResult>((await Controller(admin: true).CreateAsync(body)).Result);
        Assert.Equal("line one\r\nline two\tindented", Assert.Single(StoredComments("Host")).Text);
    }

    // --- reads, end to end: anonymity ----------------------------------------------------------------------------

    /// <summary>
    /// T375 review: an anonymous comment stores its real author, and no other reader may get that id back. The author
    /// posts as user 1; the reader is user 2, who may see the fix request and the host.
    /// </summary>
    [Fact]
    public async Task TestAnonymousAuthorIsWithheldFromOtherReaders()
    {
        Grant("vulnerabilities", "hosts");
        var author = Controller();

        var onFixRequest = Body("FixRequest");
        onFixRequest.IsAnonymous = true;
        var onHost = Body("Host");
        onHost.IsAnonymous = true;
        var anonymous = new[]
        {
            Created(await author.CreateAsync(onFixRequest)).Id, Created(await author.CreateAsync(onHost)).Id
        };
        var signed = Created(await author.CreateAsync(Body("FixRequest"))).Id;

        var reader = ControllerAs(new User { Value = Reader, Name = "reader", Admin = false });
        var fixRequestComments = Listed(await reader.Get(FixRequestId));
        var hostComments = Listed(await reader.GetHostComments(HostId));

        Assert.Null(Assert.Single(fixRequestComments, c => c.Id == anonymous[0]).UserId);
        Assert.Null(Assert.Single(hostComments, c => c.Id == anonymous[1]).UserId);
        Assert.Equal(Caller, Assert.Single(fixRequestComments, c => c.Id == signed).UserId);
        Assert.DoesNotContain(Listed(await reader.Get()), c => anonymous.Contains(c.Id));
    }

    [Fact]
    public async Task TestAnonymousAuthorStillSeesTheirOwnCommentInTheirList()
    {
        Grant("hosts");
        var body = Body("Host");
        body.IsAnonymous = true;
        var id = Created(await Controller().CreateAsync(body)).Id;

        var own = Listed(await ControllerAs(new User { Value = Caller, Name = "testUser", Admin = false }).Get());

        Assert.Equal(Caller, Assert.Single(own, c => c.Id == id).UserId);
    }

    // --- reads, end to end: who may read a fix request's comments ------------------------------------------------

    [Theory]
    [InlineData(FixRequestId)]     // requested by user 5; the caller has no permission and takes no part
    [InlineData(TeamFixRequestId)] // sent to team 7, which the caller is not in
    [InlineData(12345)]            // does not exist: the same answer
    public async Task TestFixRequestCommentsAreNotFoundForOutsiders(int fixRequestId)
    {
        Grant("hosts", "riskmanagement");
        SeedComment(new Comment { Type = "FixRequest", FixRequestId = fixRequestId, Text = "private" });

        Assert.IsType<NotFoundResult>((await Controller().Get(fixRequestId)).Result);
    }

    [Fact]
    public async Task TestFixRequestCommentsOfAnotherEntitysFindingAreNotFound()
    {
        _dal.Scope = EntityScope.ForEntities([EntityB]);

        Assert.IsType<NotFoundResult>((await Controller(admin: true).Get(FixRequestId)).Result);
    }

    [Theory]
    [InlineData(FixRequestId, Caller, false, false, false)]       // the requester
    [InlineData(TeamFixRequestId, Requester, true, false, false)] // a member of the fix team
    [InlineData(AnalystFixRequestId, Requester, false, true, false)] // the finding's analyst
    [InlineData(FixRequestId, Requester, false, false, true)]     // a holder of vulnerabilities
    public async Task TestFixRequestCommentsAreListedForParticipants(int fixRequestId, int requester, bool inTeam,
        bool analyst, bool holdsVulnerabilities)
    {
        if (holdsVulnerabilities) Grant("vulnerabilities"); else Grant();
        SeedParticipant(fixRequestId, requester, inTeam, analyst);
        var id = SeedComment(new Comment { Type = "FixRequest", FixRequestId = fixRequestId, Text = "status?" });

        Assert.Equal(id, Assert.Single(Listed(await Controller().Get(fixRequestId))).Id);
    }

    // --- reads, end to end: a host's comments follow the host's entity scope -------------------------------------

    [Fact]
    public async Task TestHostCommentsOutsideTheCallersEntitiesAreNotFound()
    {
        SeedComment(new Comment { Type = "Host", HostId = HostId, Text = "entity A only" });
        _dal.Scope = EntityScope.ForEntities([EntityB]);

        Assert.IsType<NotFoundResult>((await Controller(admin: true).GetHostComments(HostId)).Result);
    }

    [Fact]
    public async Task TestHostCommentsOfAnUnknownHostAreNotFound()
    {
        Assert.IsType<NotFoundResult>((await Controller(admin: true).GetHostComments(12345)).Result);
    }

    [Fact]
    public async Task TestHostCommentsInsideTheCallersEntitiesAreListed()
    {
        var id = SeedComment(new Comment { Type = "Host", HostId = HostId, Text = "entity A" });
        _dal.Scope = EntityScope.ForEntities([EntityA]);

        Assert.Equal(id, Assert.Single(Listed(await Controller().GetHostComments(HostId))).Id);
    }

    // --- helpers -------------------------------------------------------------------------------------------------

    private const int Reader = 2;

    private static Comment Created(ActionResult<Comment> result) =>
        Assert.IsType<Comment>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static List<Comment> Listed(ActionResult<List<Comment>> result) =>
        Assert.IsType<List<Comment>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    /// <summary>The controller over the real service, signed in as <paramref name="user"/>.</summary>
    private CommentsController ControllerAs(User user)
    {
        var users = Substitute.For<IUsersService>();
        user.Lang = "en";
        users.GetUser("testUser").Returns(user);
        users.GetUserAsync("testUser").Returns(user);

        var service = new CommentsService(Serilog.Log.Logger, _dal, _messages, _permissions);
        return ResolveController<CommentsController>(s =>
        {
            s.AddSingleton<ICommentsService>(service);
            s.AddSingleton(users);
        });
    }

    /// <summary>The permissions the logged-in user holds for the rest of the test.</summary>
    private void Grant(params string[] permissions) =>
        _permissions.GetUserPermissionsAsync(Arg.Any<User>()).Returns(_ => Task.FromResult(permissions.ToList()));

    private CommentsController Controller(bool admin = false)
    {
        var service = new CommentsService(Serilog.Log.Logger, _dal, _messages, _permissions);

        return ResolveController<CommentsController>(s =>
        {
            s.AddSingleton<ICommentsService>(service);
            s.AddSingleton(admin ? MockedUsersService.Create() : MockedNonAdminUsersService.Build());
        });
    }

    private static Comment Body(string type, int? userId = null, string? commenterName = null, DateTime? date = null,
        string? text = "the patch is in") => new()
    {
        Type = type,
        Text = text,
        UserId = userId,
        CommenterName = commenterName,
        Date = date ?? default,
        RiskId = type == "Risk" ? RiskId : null,
        VulnerabilityId = type == "Vulnerability" ? VulnerabilityId : null,
        FixRequestId = type == "FixRequest" ? FixRequestId : null,
        HostId = type == "Host" ? HostId : null
    };

    private void SeedRecords()
    {
        using var db = _dal.GetContext(bypassEntityScope: true);

        db.Risks.Add(NewRisk(RiskId, EntityA));
        db.Risks.Add(NewRisk(OtherEntityRiskId, EntityB));
        db.Vulnerabilities.Add(NewVulnerability(VulnerabilityId, EntityA));
        db.Vulnerabilities.Add(NewVulnerability(OtherEntityVulnerabilityId, EntityB));
        db.Hosts.Add(new Host { Id = HostId, Ip = "10.0.0.1", Source = "manual", EntityId = EntityA });
        db.Hosts.Add(new Host { Id = HostId + 1, Ip = "10.0.0.2", Source = "manual", EntityId = EntityA });
        db.Teams.Add(new Team { Value = TeamId, Name = "Ops" });

        db.FixRequests.Add(NewFixRequest(FixRequestId, Requester));
        db.FixRequests.Add(NewFixRequest(UnclaimedFixRequestId, null));
        db.FixRequests.Add(NewFixRequest(TeamFixRequestId, Requester, TeamId));
        db.FixRequests.Add(NewFixRequest(AnalystFixRequestId, Requester, vulnerabilityId: OtherEntityVulnerabilityId));

        db.SaveChanges();
    }

    /// <summary>Makes the caller the requester, a member of the fix team, or the finding's analyst.</summary>
    private void SeedParticipant(int fixRequestId, int requester, bool inTeam, bool analyst)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);

        db.FixRequests.Single(f => f.Id == fixRequestId).RequestingUserId = requester;

        if (inTeam)
            db.Users.Add(new User
            {
                Value = Caller, Name = "testUser", Type = "local", Email = "t@x.io", Password = [1], RoleId = 1,
                Enabled = true, Teams = { db.Teams.Single(t => t.Value == TeamId) }
            });

        if (analyst)
            db.Vulnerabilities.Single(v => v.Id == OtherEntityVulnerabilityId).AnalystId = Caller;

        db.SaveChanges();
    }

    private int SeedComment(Comment comment)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        comment.Date = new DateTime(2026, 1, 1);
        db.Comments.Add(comment);
        db.SaveChanges();
        _seededComments.Add(comment.Id);
        return comment.Id;
    }

    private static Risk NewRisk(int id, int entityId) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EntityId = entityId
    };

    private static Vulnerability NewVulnerability(int id, int entityId) => new()
    {
        Id = id, Title = $"V{id}", Status = 1, EntityId = entityId,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 1), DetectionCount = 1
    };

    private static FixRequest NewFixRequest(int id, int? requester, int? teamId = null,
        int vulnerabilityId = VulnerabilityId) => new()
    {
        Id = id, VulnerabilityId = vulnerabilityId, Identifier = $"key-{id}", CreationDate = new DateTime(2026, 1, 1),
        RequestingUserId = requester, FixTeamId = teamId, IsTeamFix = teamId != null
    };

    /// <summary>Every comment of the type the test created, whatever the current entity scope.</summary>
    private List<Comment> StoredComments(string type)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.Comments.Where(c => c.Type == type).AsEnumerable()
            .Where(c => !_seededComments.Contains(c.Id)).ToList();
    }

    private FixRequest StoredFixRequest(int id)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.FixRequests.Single(f => f.Id == id);
    }

    private static void AssertForbidden(ActionResult<Comment> result)
    {
        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    private static void AssertInvalid(ActionResult<Comment> result, string parameter)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var body = bad.Value!;
        Assert.Equal(parameter, body.GetType().GetProperty("ParameterName")!.GetValue(body));
    }
}
