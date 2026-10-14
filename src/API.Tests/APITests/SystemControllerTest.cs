using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.ClientData;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
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
                { "mac", "https://example.invalid/netrisk-mac-a64.dmg" },
                { "mac-a64", "https://example.invalid/netrisk-mac-a64.dmg" },
                // A server whose ClientInformation.yaml predates T341 still has this key. It must
                // never be served: there is no Intel build behind it any more.
                { "mac-x64", "https://example.invalid/netrisk-mac-x64.dmg" }
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
    [InlineData("mac", "https://example.invalid/netrisk-mac-a64.dmg")]
    // What an Apple Silicon client actually sends. It used to be refused, so no Apple Silicon
    // client could ever download its own upgrade.
    [InlineData("mac-a64", "https://example.invalid/netrisk-mac-a64.dmg")]
    [InlineData("MAC-A64", "https://example.invalid/netrisk-mac-a64.dmg")]
    public async Task TestClientDownloadLocation(string osFamily, string expected)
    {
        var result = await _controller.ClientDownloadLocation(osFamily);

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task TestClientDownloadLocationWithUnsupportedFamilyReturnsBadRequest()
    {
        var result = await _controller.ClientDownloadLocation("solaris");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ClientPlatforms.UnknownPlatformMessage, badRequest.Value);
    }

    [Theory]
    [InlineData("mac-x64")]
    [InlineData("MAC-X64")]
    public async Task TestClientDownloadLocationRefusesAnIntelMacEvenWhenTheFileStillNamesOne(string osFamily)
    {
        var result = await _controller.ClientDownloadLocation(osFamily);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ClientPlatforms.MacIntelUnsupportedMessage, badRequest.Value);
        Assert.Null(result.Value);
        await _systemService.DidNotReceive().GetClientInformation(Arg.Any<string>());
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

/// <summary>
/// The same endpoints over the real <see cref="SystemService"/>, reading the
/// <c>ClientInformation.yaml</c> and update scripts the API ships (copied beside these binaries by
/// the API project reference). The test above pins the controller's own branches; this pins what an
/// Intel or Apple Silicon client is actually answered (T341).
/// </summary>
[TestSubject(typeof(SystemController))]
public class SystemControllerClientPlatformTest : BaseControllerTest
{
    private readonly SystemController _controller = ResolveController<SystemController>(s =>
        s.AddSingleton<ISystemService>(new SystemService(
            Substitute.For<Serilog.ILogger>(), Substitute.For<IDalService>())));

    [Theory]
    [InlineData("mac")]
    [InlineData("mac-a64")]
    public async Task AnAppleSiliconClientIsSentTheAppleSiliconDmg(string osFamily)
    {
        var result = await _controller.ClientDownloadLocation(osFamily);

        Assert.EndsWith("/installers/GUIClient-Mac-a64.dmg", result.Value);
    }

    [Theory]
    [InlineData("mac")]
    [InlineData("mac-a64")]
    public async Task AnAppleSiliconClientIsSentTheMacUpdateScript(string osFamily)
    {
        var result = await _controller.UpdateScript(osFamily);

        Assert.Null(result.Result);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task AnIntelMacIsToldItIsUnsupportedInsteadOfBeingSentAnInstaller()
    {
        var result = await _controller.ClientDownloadLocation("mac-x64");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ClientPlatforms.MacIntelUnsupportedMessage, badRequest.Value);
    }

    [Fact]
    public async Task AnIntelMacIsToldItIsUnsupportedInsteadOfBeingSentAnUpdateScript()
    {
        var result = await _controller.UpdateScript("mac-x64");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(ClientPlatforms.MacIntelUnsupportedMessage, badRequest.Value);
    }
}
