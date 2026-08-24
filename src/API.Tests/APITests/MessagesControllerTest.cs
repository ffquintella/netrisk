using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.DI;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(MessagesController))]
public class MessagesControllerTest : BaseControllerTest
{
    private readonly IMessagesService _messagesService = Substitute.For<IMessagesService>();
    private readonly MessagesController _controller;

    private static Message SampleMessage(int id) => new()
    {
        Id = id,
        UserId = 1,
        CreatedAt = new DateTime(2024, 1, 1),
        Message1 = "message " + id,
        Status = 0,
        Type = 1
    };

    public MessagesControllerTest()
    {
        _messagesService.GetAllAsync(1, Arg.Any<List<int?>>()).Returns(new List<Message>
        {
            SampleMessage(1),
            SampleMessage(2)
        });
        _messagesService.HasUnreadMessagesAsync(1, Arg.Any<List<int?>>()).Returns(true);
        _messagesService.GetMessageAsync(1).Returns(SampleMessage(1));

        _controller = ResolveController<MessagesController>(s => s.AddSingleton(_messagesService));
    }

    [Fact]
    public async Task TestGet()
    {
        var result = await _controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Message>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetWithChatFilter()
    {
        var result = await _controller.Get(new List<int?> { 3, 4 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<List<Message>>(ok.Value);
    }

    [Fact]
    public async Task TestGetCount()
    {
        var result = await _controller.GetCount();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, Assert.IsType<int>(ok.Value));
    }

    [Fact]
    public async Task TestHasUnreadMessages()
    {
        var result = await _controller.HasUnreadMessages();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<bool>(ok.Value));
    }

    [Fact]
    public async Task TestReadMessage()
    {
        var result = await _controller.ReadMessage(1, "read");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Message read", ok.Value);
        await _messagesService.Received(1).SaveMessageAsync(
            Arg.Is<Message>(m => m.Status == (int)IntStatus.Read && m.ReceivedAt != null));
    }

    [Fact]
    public async Task TestReadMessageRejectsUnknownOperation()
    {
        var result = await _controller.ReadMessage(1, "unread");

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await _messagesService.DidNotReceive().SaveMessageAsync(Arg.Any<Message>());
    }

    [Fact]
    public async Task TestDeleteMessageAsAdmin()
    {
        var result = await _controller.DeleteMessage(2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Deleted", ok.Value);
        await _messagesService.Received(1).DeleteMessageAsync(2);
    }

    [Fact]
    public async Task TestDeleteOwnMessageAsNonAdmin()
    {
        var controller = NonAdminController();

        var result = await controller.DeleteMessage(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Deleted", ok.Value);
    }

    [Fact]
    public async Task TestDeleteForeignMessageAsNonAdminIsRejected()
    {
        var controller = NonAdminController();

        var result = await controller.DeleteMessage(777);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        await _messagesService.DidNotReceive().DeleteMessageAsync(777);
    }

    /// <summary>
    /// The shared <see cref="IUsersService"/> mock builds a fresh admin <c>User</c> per provider, so
    /// demoting it here only affects the controller resolved from this provider.
    /// </summary>
    private MessagesController NonAdminController()
    {
        var provider = ServiceRegistration.GetServiceProvider(s => s.AddSingleton(_messagesService));
        provider.GetRequiredService<IUsersService>().GetUser("testUser").Admin = false;
        return provider.GetRequiredService<MessagesController>();
    }
}
