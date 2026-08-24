using System;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Registration;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(RegistrationController))]
public class RegistrationControllerTest : BaseControllerTest
{
    private readonly IClientRegistrationService _clientRegistrationService =
        Substitute.For<IClientRegistrationService>();

    private readonly RegistrationController _controller;

    public RegistrationControllerTest()
    {
        _clientRegistrationService.IsAccepted("accepted").Returns(1);
        _clientRegistrationService.IsAccepted("pending").Returns(0);
        _clientRegistrationService.IsAccepted("unknown").Returns(-1);
        _clientRegistrationService.IsAccepted("weird").Returns(-2);
        _clientRegistrationService.IsAccepted("boom")
            .Returns(_ => throw new Exception("registration store offline"));

        _clientRegistrationService.Add(Arg.Is<ClientRegistration>(c => c.ExternalId == "new-client"))
            .Returns(0);
        _clientRegistrationService.Add(Arg.Is<ClientRegistration>(c => c.ExternalId == "dup-client"))
            .Returns(1);
        _clientRegistrationService.Add(Arg.Is<ClientRegistration>(c => c.ExternalId == "odd-client"))
            .Returns(42);
        _clientRegistrationService.Add(Arg.Is<ClientRegistration>(c => c.ExternalId == "boom-client"))
            .Returns(_ => throw new Exception("registration store offline"));

        _controller = ResolveController<RegistrationController>(s => s.AddSingleton(_clientRegistrationService));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("pending")]
    public void TestIsRegistredReturnsTrueForKnownClients(string clientId)
    {
        var result = _controller.IsRegistred(clientId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<bool>(ok.Value));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("weird")]
    [InlineData("boom")]
    public void TestIsRegistredReturnsNotFound(string clientId)
    {
        var result = _controller.IsRegistred(clientId);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.False(Assert.IsType<bool>(notFound.Value));
    }

    [Fact]
    public void TestIsAcceptedReturnsTrue()
    {
        var result = _controller.IsAccepted("accepted");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<bool>(ok.Value));
    }

    [Fact]
    public void TestIsAcceptedReturnsFalseForPendingClient()
    {
        var result = _controller.IsAccepted("pending");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(Assert.IsType<bool>(ok.Value));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("weird")]
    [InlineData("boom")]
    public void TestIsAcceptedReturnsNotFound(string clientId)
    {
        var result = _controller.IsAccepted(clientId);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.False(Assert.IsType<bool>(notFound.Value));
    }

    [Fact]
    public void TestRegisterWithoutIdReturnsBadRequest()
    {
        var result = _controller.Register(new RegistrationRequest
        {
            Id = null,
            Hostname = "host",
            LoggedAccount = "account"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Request Id is null", badRequest.Value);
    }

    [Fact]
    public void TestRegisterOk()
    {
        var result = _controller.Register(new RegistrationRequest
        {
            Id = "new-client",
            Hostname = "host",
            LoggedAccount = "account"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var hashCode = Assert.IsType<string>(ok.Value);
        Assert.False(string.IsNullOrEmpty(hashCode));

        _clientRegistrationService.Received(1)
            .Add(Arg.Is<ClientRegistration>(c => c.ExternalId == "new-client"
                                                && c.Hostname == "host"
                                                && c.LoggedAccount == "account"
                                                && c.Status == "requested"));
    }

    [Fact]
    public void TestRegisterAlreadyExists()
    {
        var result = _controller.Register(new RegistrationRequest
        {
            Id = "dup-client",
            Hostname = "host",
            LoggedAccount = "account"
        });

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(412, objectResult.StatusCode);
        Assert.Equal("Already exists", objectResult.Value);
    }

    [Theory]
    [InlineData("odd-client")]
    [InlineData("boom-client")]
    public void TestRegisterUnknownErrorReturnsInternalServerError(string id)
    {
        var result = _controller.Register(new RegistrationRequest
        {
            Id = id,
            Hostname = "host",
            LoggedAccount = "account"
        });

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.Equal("Unknown error", objectResult.Value);
    }
}
