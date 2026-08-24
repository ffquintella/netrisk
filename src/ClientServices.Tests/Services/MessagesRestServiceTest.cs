using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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

[TestSubject(typeof(MessagesRestService))]
public class MessagesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IMessagesService _service;

    public MessagesRestServiceTest()
    {
        _service = ResolveWith<IMessagesService>(_backend);
    }

    private static Message UserMessage(int id, int status, int chatId) => new()
    {
        Id = id,
        UserId = 4,
        CreatedAt = new DateTime(2024, 5, 5, 12, 0, 0, DateTimeKind.Utc),
        ReceivedAt = null,
        Message1 = "body-" + id,
        Status = status,
        ChatId = chatId,
        Type = 1
    };

    // ---------- GetCountAsync ----------

    [Fact]
    public async Task TestGetCountAsyncReturnsTheCount()
    {
        _backend.OnGet("/Messages/count", 7);

        var count = await _service.GetCountAsync();

        Assert.Equal(7, count);
        Assert.Equal("GET /Messages/count", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetCountAsyncAddsOneChatsQueryParameterPerChat()
    {
        _backend.OnGet("/Messages/count", 3);

        var count = await _service.GetCountAsync(new List<int?> { 11, 22 });

        Assert.Equal(3, count);
        Assert.Equal("/Messages/count", _backend.LastRequest.Path);
        Assert.Contains("chats=11", _backend.LastRequest.Query);
        Assert.Contains("chats=22", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetCountAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Messages/count", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetCountAsync());

        Assert.Equal("Error getting messages count", ex.RestExceptionMessage);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestGetCountAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Messages/count");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetCountAsync());
    }

    // ---------- HasUnreadMessages ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestHasUnreadMessagesReturnsTheServerAnswer(bool unread)
    {
        _backend.OnGet("/Messages/has_unread", unread);

        var result = await _service.HasUnreadMessages();

        Assert.Equal(unread, result);
        Assert.Equal("GET /Messages/has_unread", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestHasUnreadMessagesFiltersByChat()
    {
        _backend.OnGet("/Messages/has_unread", true);

        Assert.True(await _service.HasUnreadMessages(new List<int?> { 5 }));
        Assert.Contains("chats=5", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestHasUnreadMessagesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Messages/has_unread", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.HasUnreadMessages());

        Assert.Equal("Error checking if user has unread messages", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestHasUnreadMessagesWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Messages/has_unread");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.HasUnreadMessages());
    }

    // ---------- GetMessagesAsync ----------

    [Fact]
    public async Task TestGetMessagesAsyncReturnsTheMessagesOrderedByStatus()
    {
        _backend.OnGet("/Messages", new List<Message> { UserMessage(1, 2, 100), UserMessage(2, 1, 100) });

        var messages = await _service.GetMessagesAsync();

        Assert.Equal(2, messages.Count);
        Assert.Equal(1, messages[0].Status);
        Assert.Equal(2, messages[0].Id);
        Assert.Equal("body-1", messages[1].Message1);
        Assert.Equal("GET /Messages", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetMessagesAsyncFiltersByChat()
    {
        _backend.OnGet("/Messages", new List<Message> { UserMessage(1, 1, 100) });

        var messages = await _service.GetMessagesAsync(new List<int?> { 100, 200 });

        Assert.Single(messages);
        Assert.Equal(100, messages[0].ChatId);
        Assert.Contains("chats=100", _backend.LastRequest.Query);
        Assert.Contains("chats=200", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetMessagesAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Messages", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMessagesAsync());

        Assert.Equal("Error getting messages", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetMessagesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Messages", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMessagesAsync());

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestGetMessagesAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Messages");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetMessagesAsync());
    }

    // ---------- ReadMessageAsync ----------

    [Fact]
    public async Task TestReadMessageAsyncPatchesWithTheReadOperation()
    {
        _backend.On(Method.Patch, "/Messages/8", "\"ok\"");

        await _service.ReadMessageAsync(8);

        Assert.Equal("PATCH", _backend.LastRequest.Method);
        Assert.Equal("/Messages/8", _backend.LastRequest.Path);
        Assert.Contains("operation=read", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestReadMessageAsyncSwallowsAnEmptyNotFoundAnswer()
    {
        // Known limitation: ReadMessageAsync ignores the response, so a 404 (which RestSharp
        // surfaces as a null body rather than an error) is indistinguishable from success.
        _backend.OnStatus(Method.Patch, "/Messages/8", HttpStatusCode.NotFound);

        await _service.ReadMessageAsync(8);

        Assert.True(_backend.Sent(Method.Patch, "/Messages/8"));
    }

    [Fact]
    public async Task TestReadMessageAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Patch, "/Messages/8", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.ReadMessageAsync(8));

        Assert.Equal("Error reading message ", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestReadMessageAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Patch, "/Messages/8");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.ReadMessageAsync(8));
    }

    // ---------- DeleteMessageAsync ----------

    [Fact]
    public async Task TestDeleteMessageAsyncSendsTheDelete()
    {
        _backend.OnDelete("/Messages/9", "");

        await _service.DeleteMessageAsync(9);

        Assert.Equal("DELETE", _backend.LastRequest.Method);
        Assert.Equal("/Messages/9", _backend.LastRequest.Path);
    }

    [Fact]
    public async Task TestDeleteMessageAsyncWrapsATransportFailure()
    {
        // DeleteMessageAsync never inspects the status code, so the transport failure is the only
        // way the caller learns the delete did not happen.
        _backend.OnTransportFailure(Method.Delete, "/Messages/9");

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteMessageAsync(9));

        Assert.Equal("Error reading message ", ex.RestExceptionMessage);
    }
}
