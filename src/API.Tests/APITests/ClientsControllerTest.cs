using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(ClientsController))]
public class ClientsControllerTest : BaseControllerTest
{
    private readonly IClientRegistrationService _clientRegistrationService =
        Substitute.For<IClientRegistrationService>();

    private readonly ClientsController _controller;

    public ClientsControllerTest()
    {
        _clientRegistrationService.GetAll().Returns(new List<ClientRegistration>
        {
            new()
            {
                Id = 1,
                Name = "AAAA",
                ExternalId = "external-1",
                Hostname = "host1",
                LoggedAccount = "user1",
                RegistrationDate = new DateTime(2024, 1, 1),
                LastVerificationDate = new DateTime(2024, 1, 2),
                Status = "approved"
            },
            new()
            {
                Id = 2,
                Name = "BBBB",
                ExternalId = "external-2",
                Hostname = "host2",
                LoggedAccount = "user2",
                RegistrationDate = new DateTime(2024, 2, 1),
                LastVerificationDate = new DateTime(2024, 2, 2),
                Status = "requested"
            }
        });

        _clientRegistrationService.DeleteById(1).Returns(0);
        _clientRegistrationService.DeleteById(2).Returns(1);
        _clientRegistrationService.DeleteById(3).Returns(-1);
        _clientRegistrationService.DeleteById(4).Returns(77);

        _clientRegistrationService.Approve(1).Returns(0);
        _clientRegistrationService.Approve(2).Returns(1);
        _clientRegistrationService.Approve(3).Returns(2);
        _clientRegistrationService.Approve(4).Returns(-1);
        _clientRegistrationService.Approve(5).Returns(77);

        _clientRegistrationService.Reject(1).Returns(0);
        _clientRegistrationService.Reject(2).Returns(1);
        _clientRegistrationService.Reject(3).Returns(2);
        _clientRegistrationService.Reject(4).Returns(-1);
        _clientRegistrationService.Reject(5).Returns(77);

        _controller = ResolveController<ClientsController>(s => s.AddSingleton(_clientRegistrationService));
    }

    [Fact]
    public void TestGetAll()
    {
        var result = _controller.GetAll();

        var clients = Assert.IsType<List<Client>>(result.Value);
        Assert.Equal(2, clients.Count);
        Assert.Equal(1, clients[0].Id);
        Assert.Equal("host1", clients[0].Hostname);
        Assert.Equal("requested", clients[1].Status);
    }

    [Fact]
    public void TestDeleteOk()
    {
        var result = _controller.Delete(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Deleted OK", ok.Value);
    }

    [Fact]
    public void TestDeleteNotFound()
    {
        var result = _controller.Delete(2);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Client not found", notFound.Value);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void TestDeleteInternalError(int id)
    {
        var result = _controller.Delete(id);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.Equal("Internal error", objectResult.Value);
    }

    [Fact]
    public void TestApproveOk()
    {
        var result = _controller.Approve(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Approved OK", ok.Value);
    }

    [Fact]
    public void TestApproveNotFound()
    {
        var result = _controller.Approve(2);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Client not found", notFound.Value);
    }

    [Fact]
    public void TestApproveAlreadyApproved()
    {
        var result = _controller.Approve(3);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, objectResult.StatusCode);
        Assert.Equal("Already approved", objectResult.Value);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void TestApproveInternalError(int id)
    {
        var result = _controller.Approve(id);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public void TestRejectOk()
    {
        var result = _controller.Reject(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Rejected OK", ok.Value);
    }

    [Fact]
    public void TestRejectNotFound()
    {
        var result = _controller.Reject(2);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Client not found", notFound.Value);
    }

    [Fact]
    public void TestRejectAlreadyRejected()
    {
        var result = _controller.Reject(3);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, objectResult.StatusCode);
        Assert.Equal("Already rejected", objectResult.Value);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void TestRejectInternalError(int id)
    {
        var result = _controller.Reject(id);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
    }
}
