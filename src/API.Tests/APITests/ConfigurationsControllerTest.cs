using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(ConfigurationsController))]
public class ConfigurationsControllerTest : BaseControllerTest
{
    private readonly IConfigurationsService _configurationsService = Substitute.For<IConfigurationsService>();
    private readonly ConfigurationsController _controller;

    public ConfigurationsControllerTest()
    {
        _configurationsService.GetBackupPassword().Returns("a-secret");
        _configurationsService.GetWebsiteSyncConfig().Returns(new WebsiteSyncConfigDto
        {
            IntervalMinutes = 60,
            FastIntervalMinutes = 2,
            Url = "https://site:6443",
            Insecure = false
        });

        _controller = ResolveController<ConfigurationsController>(s => s.AddSingleton(_configurationsService));
    }

    [Fact]
    public void TestVerifyBackupPassword()
    {
        var result = _controller.VerifyBackupPassword();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Backup password already set", ok.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    // string?, because null is exactly one of the two cases under test: the service returns null when
    // no backup password has ever been set, and "" when one was cleared.
    public void TestVerifyBackupPasswordNotSet(string? stored)
    {
        _configurationsService.GetBackupPassword().Returns(stored);

        var result = _controller.VerifyBackupPassword();

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void TestUpdateBackupPassword()
    {
        var result = _controller.UpdateBackupPassword(new PasswordDto { Password = "new-secret" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Backup password updated", ok.Value);
        _configurationsService.Received(1).UpdateBackupPassword("new-secret");
    }

    [Fact]
    public void TestGetWebsiteSyncConfig()
    {
        var result = _controller.GetWebsiteSyncConfig();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var config = Assert.IsType<WebsiteSyncConfigDto>(ok.Value);
        Assert.Equal(60, config.IntervalMinutes);
        Assert.Equal("https://site:6443", config.Url);
    }

    [Fact]
    public void TestUpdateWebsiteSyncConfig()
    {
        var config = new WebsiteSyncConfigDto
        {
            IntervalMinutes = 30, FastIntervalMinutes = 1, Url = "https://other:6443", Insecure = true
        };

        var result = _controller.UpdateWebsiteSyncConfig(config);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Website sync configuration updated", ok.Value);
        _configurationsService.Received(1).UpdateWebsiteSyncConfig(config);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(60, 0)]
    [InlineData(-1, -1)]
    public void TestUpdateWebsiteSyncConfigRejectsTooShortIntervals(int interval, int fastInterval)
    {
        var result = _controller.UpdateWebsiteSyncConfig(new WebsiteSyncConfigDto
        {
            IntervalMinutes = interval, FastIntervalMinutes = fastInterval, Url = "https://site:6443"
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _configurationsService.DidNotReceive().UpdateWebsiteSyncConfig(Arg.Any<WebsiteSyncConfigDto>());
    }
}
