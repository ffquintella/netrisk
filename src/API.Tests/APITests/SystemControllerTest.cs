using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.ClientData;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(SystemController))]
public class SystemControllerTest : BaseControllerTest
{
    private readonly ISystemService _systemService = Substitute.For<ISystemService>();
    private readonly SystemController _controller;

    public SystemControllerTest()
    {
        var clientInformation = new ClientInformation
        {
            Version = "1.2.3",
            DownloadLocation = new Dictionary<string, string>
            {
                { "windows", "https://example.invalid/netrisk-win.zip" },
                { "linux", "https://example.invalid/netrisk-linux.zip" },
                { "mac", "https://example.invalid/netrisk-mac.zip" }
            }
        };

        _systemService.GetClientInformation(Arg.Any<string>()).Returns(clientInformation);

        _systemService.GetUpdateScriptAsync("linux").Returns("#!/bin/sh\necho update");
        _systemService.GetUpdateScriptAsync("solaris")
            .Returns<Task<string>>(_ => throw new InvalidParameterException("osFamily", "OS Family not supported"));
        _systemService.GetUpdateScriptAsync("boom")
            .Returns<Task<string>>(_ => throw new ApplicationException("disk on fire"));

        _controller = ResolveController<SystemController>(s => s.AddSingleton(_systemService));
    }

    [Fact]
    public void TestPing()
    {
        var result = _controller.Ping();

        Assert.Equal("Pong", result.Value);
    }

    [Fact]
    public void TestVersion()
    {
        var result = _controller.Version();

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("windows", "https://example.invalid/netrisk-win.zip")]
    [InlineData("linux", "https://example.invalid/netrisk-linux.zip")]
    [InlineData("mac", "https://example.invalid/netrisk-mac.zip")]
    public async Task TestClientDownloadLocation(string osFamily, string expected)
    {
        var result = await _controller.ClientDownloadLocation(osFamily);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task TestClientDownloadLocationWithUnsupportedFamilyThrows()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _controller.ClientDownloadLocation("solaris"));
    }

    [Fact]
    public async Task TestUpdateScript()
    {
        var result = await _controller.UpdateScript("linux");

        Assert.Equal("#!/bin/sh\necho update", result.Value);
    }

    [Fact]
    public async Task TestUpdateScriptWithEmptyFamilyThrows()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _controller.UpdateScript(""));
    }

    [Fact]
    public async Task TestUpdateScriptWithInvalidParameterReturnsBadRequest()
    {
        var result = await _controller.UpdateScript("solaris");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("OS Family not supported", badRequest.Value);
    }

    [Fact]
    public async Task TestUpdateScriptWithUnexpectedErrorReturnsInternalServerError()
    {
        var result = await _controller.UpdateScript("boom");

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.Equal("disk on fire", objectResult.Value);
    }
}
