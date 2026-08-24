using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(CommentsRestService))]
public class CommentsRestServiceStubTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly ICommentsService _service;

    public CommentsRestServiceStubTest()
    {
        _service = ResolveWith<ICommentsService>(_backend);
    }

    private static List<Comment> TwoComments() =>
    [
        new() { Id = 1, Type = "FixRequest", Text = "T1", FixRequestId = 1, UserId = 1, CommenterName = "Name1" },
        new() { Id = 2, Type = "FixRequest", Text = "T2", FixRequestId = 1, UserId = 2, CommenterName = "Name2" }
    ];

    [Fact]
    public async Task TestGetAllUserCommentsAsync()
    {
        _backend.OnGet("/Comments", TwoComments());

        var comments = await _service.GetAllUserCommentsAsync();

        Assert.Equal(2, comments.Count);
        Assert.Equal("T1", comments[0].Text);
        Assert.Equal("GET /Comments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllUserCommentsAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Comments", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllUserCommentsAsync());
    }

    [Fact]
    public async Task TestGetAllUserCommentsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Comments", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllUserCommentsAsync());
    }

    [Fact]
    public async Task TestGetAllUserCommentsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Comments");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllUserCommentsAsync());
    }

    [Fact]
    public async Task TestGetFixRequestCommentsAsync()
    {
        _backend.OnGet("/Comments/fixrequest/1", TwoComments());

        var comments = await _service.GetFixRequestCommentsAsync(1);

        Assert.Equal(2, comments.Count);
        Assert.True(_backend.Sent(Method.Get, "/Comments/fixrequest/1"));
    }

    [Fact]
    public async Task TestGetFixRequestCommentsAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Get, "/Comments/fixrequest/9", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetFixRequestCommentsAsync(9));
    }

    [Fact]
    public async Task TestCreateCommentAsyncPostsTheCommentAndReturnsTheSavedOne()
    {
        _backend.OnPost("/Comments", new Comment { Id = 7, Type = "FixRequest", Text = "new", FixRequestId = 1 });

        var created = await _service.CreateCommentAsync(new Comment { Type = "FixRequest", Text = "new", FixRequestId = 1 });

        Assert.Equal(7, created.Id);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Contains("\"new\"", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateCommentAsyncThrowsWhenTheServerReturnsNothing()
    {
        // RestSharp treats 404 as a legitimate empty answer rather than an error, so PostAsync<T>
        // hands back null and the service raises its own exception.
        _backend.OnStatus(Method.Post, "/Comments", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateCommentAsync(new Comment { Type = "FixRequest", Text = "x" }));
    }

    [Fact]
    public async Task TestCreateCommentAsyncWrapsAServerError()
    {
        // Any other failing status surfaces from RestSharp as HttpRequestException, which the
        // service wraps.
        _backend.OnStatus(Method.Post, "/Comments", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateCommentAsync(new Comment { Type = "FixRequest", Text = "x" }));
    }

    [Fact]
    public async Task TestCreateCommentAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Comments");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateCommentAsync(new Comment { Type = "FixRequest", Text = "x" }));
    }
}
