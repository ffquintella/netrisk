using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Plugins;
using Model.Services;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(PluginsController))]
public class PluginsControllerTest : BaseControllerTest
{
    private readonly IPluginsService _pluginsService = Substitute.For<IPluginsService>();
    private readonly PluginsController _controller;

    public PluginsControllerTest()
    {
        _pluginsService.GetPluginsAsync().Returns(new List<PluginInfo>
        {
            new() { Name = "enabledPlugin", Description = "an enabled plugin", Version = "1.0.0", IsEnabled = true },
            new() { Name = "disabledPlugin", Description = "a disabled plugin", Version = "2.0.0", IsEnabled = false }
        });

        _pluginsService.GetInfoAsync().Returns(new ServiceInformation
        {
            IsServiceAvailable = true,
            ServiceName = "Plugins",
            ServiceVersion = "1.0.0",
            ServiceDescription = "Plugins service",
            ServiceUrl = "https://example.invalid/plugins",
            ServiceNeedsPlugin = false,
            ServicePluginInstalled = true
        });

        _pluginsService.PluginExistsAsync("enabledPlugin").Returns(true);
        _pluginsService.PluginExistsAsync("disabledPlugin").Returns(true);
        _pluginsService.PluginExistsAsync("ghostPlugin").Returns(false);

        _pluginsService.PluginIsEnabledAsync("enabledPlugin").Returns(true);
        _pluginsService.PluginIsEnabledAsync("disabledPlugin").Returns(false);

        _controller = ResolveController<PluginsController>(s => s.AddSingleton(_pluginsService));
    }

    [Fact]
    public async Task TestList()
    {
        var result = await _controller.List();

        var plugins = Assert.IsType<List<PluginInfo>>(result.Value);
        Assert.Equal(2, plugins.Count);
        Assert.Equal("enabledPlugin", plugins[0].Name);
        Assert.True(plugins[0].IsEnabled);
    }

    [Fact]
    public async Task TestGetInfo()
    {
        var result = await _controller.GetInfo();

        var info = Assert.IsType<ServiceInformation>(result.Value);
        Assert.True(info.IsServiceAvailable);
        Assert.Equal("Plugins", info.ServiceName);
    }

    [Fact]
    public async Task TestReload()
    {
        var result = await _controller.Reload();

        Assert.True(result.Value);
        _ = _pluginsService.Received(1).LoadPluginsAsync();
    }

    [Fact]
    public async Task TestPluginExists()
    {
        var result = await _controller.PluginExists("enabledPlugin");

        Assert.True(result.Value);
    }

    [Fact]
    public async Task TestPluginDoesNotExist()
    {
        var result = await _controller.PluginExists("ghostPlugin");

        Assert.False(result.Value);
    }

    [Theory]
    [InlineData("enabledPlugin", true)]
    [InlineData("disabledPlugin", false)]
    public async Task TestPluginIsEnabled(string pluginName, bool expected)
    {
        var result = await _controller.PluginIsEnabled(pluginName);

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task TestPluginIsEnabledForMissingPluginReturnsNotFound()
    {
        var result = await _controller.PluginIsEnabled("ghostPlugin");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestEnablePlugin()
    {
        var result = await _controller.EnablePlugin("disabledPlugin");

        Assert.IsType<OkResult>(result);
        _ = _pluginsService.Received(1).SetPluginEnabledStatusAsync("disabledPlugin", true);
    }

    [Fact]
    public async Task TestEnablePluginForMissingPluginReturnsNotFound()
    {
        var result = await _controller.EnablePlugin("ghostPlugin");

        Assert.IsType<NotFoundResult>(result);
        _ = _pluginsService.DidNotReceive().SetPluginEnabledStatusAsync("ghostPlugin", Arg.Any<bool>());
    }

    [Fact]
    public async Task TestDisablePlugin()
    {
        var result = await _controller.DisablePlugin("enabledPlugin");

        Assert.IsType<OkResult>(result);
        _ = _pluginsService.Received(1).SetPluginEnabledStatusAsync("enabledPlugin", false);
    }

    [Fact]
    public async Task TestDisablePluginForMissingPluginReturnsNotFound()
    {
        var result = await _controller.DisablePlugin("ghostPlugin");

        Assert.IsType<NotFoundResult>(result);
        _ = _pluginsService.DidNotReceive().SetPluginEnabledStatusAsync("ghostPlugin", Arg.Any<bool>());
    }
}
