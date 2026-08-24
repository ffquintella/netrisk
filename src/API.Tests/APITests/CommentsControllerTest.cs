using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(CommentsController))]
public class CommentsControllerTest : BaseControllerTest
{
    private readonly ICommentsService _commentsService = Substitute.For<ICommentsService>();
    private readonly CommentsController _controller;

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
        _commentsService.GetFixRequestCommentsAsync(4).Returns(new List<Comment> { SampleComment(3) });
        _commentsService.GetFixRequestCommentsAsync(999).Returns(new List<Comment>());
        _commentsService.CreateCommentsAsync(
                Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<int?>())
            .Returns(SampleComment(9));

        _controller = ResolveController<CommentsController>(s => s.AddSingleton(_commentsService));
    }

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

    [Fact]
    public async Task TestCreateAsync()
    {
        var comment = SampleComment(0);

        var result = await _controller.CreateAsync(comment);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var created = Assert.IsType<Comment>(ok.Value);
        Assert.Equal(9, created.Id);
        await _commentsService.Received(1).CreateCommentsAsync(
            1, Arg.Any<DateTime>(), null, "FixRequest", false, "testUser", "comment 0", 4, null, null, null);
    }

    [Fact]
    public async Task TestCreateAnonymousComment()
    {
        var comment = SampleComment(0);
        comment.IsAnonymous = true;

        var result = await _controller.CreateAsync(comment);

        Assert.IsType<OkObjectResult>(result.Result);
        await _commentsService.Received(1).CreateCommentsAsync(
            Arg.Any<int?>(), Arg.Any<DateTime>(), Arg.Any<int?>(), Arg.Any<string>(), true,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
            Arg.Any<int?>());
    }
}
