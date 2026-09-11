using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using API.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
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

    #region UPLOAD

    /// <summary>
    /// An <see cref="IFormFile"/> over an in-memory buffer. The controller only opens the stream and
    /// reads the name and length, so this stays a stub rather than a mock of the whole interface.
    /// </summary>
    private sealed class InMemoryFormFile(string fileName, byte[] content) : IFormFile
    {
        public string ContentType { get; set; } = "application/zip";
        public string ContentDisposition { get; set; } = string.Empty;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => content.Length;
        public string Name => "file";
        public string FileName => fileName;

        public Stream OpenReadStream() => new MemoryStream(content);
        public void CopyTo(Stream target) => target.Write(content);

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            target.WriteAsync(content, cancellationToken).AsTask();
    }

    [Fact]
    public async Task TestUploadInstallsThePackage()
    {
        _pluginsService.InstallPluginPackageAsync(Arg.Any<Stream>(), "MyVault.Plugin.zip")
            .Returns(new PluginInstallResult
            {
                Success = true,
                PackageName = "MyVault.Plugin",
                Message = "Installed MyVault.Plugin.",
                LoadedPlugins = ["MyVault"]
            });

        var file = new InMemoryFormFile("MyVault.Plugin.zip", Encoding.UTF8.GetBytes("PK-not-really"));

        var result = await _controller.Upload(file);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var value = Assert.IsType<PluginInstallResult>(ok.Value);

        Assert.True(value.Success);
        Assert.Equal("MyVault.Plugin", value.PackageName);
        Assert.Equal(["MyVault"], value.LoadedPlugins);
    }

    [Fact]
    public async Task TestUploadWithNoFileIsRejectedWithoutTouchingTheService()
    {
        var result = await _controller.Upload(null);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var value = Assert.IsType<PluginInstallResult>(bad.Value);

        Assert.False(value.Success);
        _ = _pluginsService.DidNotReceive()
            .InstallPluginPackageAsync(Arg.Any<Stream>(), Arg.Any<string>());
    }

    [Fact]
    public async Task TestUploadWithAnEmptyFileIsRejectedWithoutTouchingTheService()
    {
        var result = await _controller.Upload(new InMemoryFormFile("empty.zip", []));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _ = _pluginsService.DidNotReceive()
            .InstallPluginPackageAsync(Arg.Any<Stream>(), Arg.Any<string>());
    }

    /// <summary>
    /// A package the installer refuses comes back as a 400 that still carries the result, because
    /// its <c>Message</c> ("no *Plugin.dll at the top level") is the only thing that tells the
    /// operator what to change. A bare status code would throw that away.
    /// </summary>
    [Fact]
    public async Task TestUploadReturnsTheRefusalReason()
    {
        _pluginsService.InstallPluginPackageAsync(Arg.Any<Stream>(), "notaplugin.zip")
            .Returns(new PluginInstallResult
            {
                Success = false,
                PackageName = "notaplugin",
                Message = "The package has no *Plugin.dll at its top level."
            });

        var file = new InMemoryFormFile("notaplugin.zip", Encoding.UTF8.GetBytes("junk"));

        var result = await _controller.Upload(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var value = Assert.IsType<PluginInstallResult>(bad.Value);

        Assert.False(value.Success);
        Assert.Contains("Plugin.dll", value.Message);
    }

    #endregion

    // ---------------- DELETE ----------------

    [Fact]
    public async Task TestUninstallRemovesThePlugin()
    {
        _pluginsService.UninstallPluginAsync("enabledPlugin")
            .Returns(new PluginUninstallResult
            {
                Success = true,
                PluginName = "enabledPlugin",
                PackageName = "EnabledPlugin",
                Message = "Plugin enabledPlugin was removed."
            });

        var result = await _controller.Uninstall("enabledPlugin");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var value = Assert.IsType<PluginUninstallResult>(ok.Value);

        Assert.True(value.Success);
        Assert.False(value.RemovalPending);
        Assert.Equal("EnabledPlugin", value.PackageName);
    }

    /// <summary>
    /// A removal whose files are still locked by the server process is a success with
    /// <c>RemovalPending</c> set, not a failure: the plugin is off and delisted, and only the files
    /// wait for a restart. Reporting it as an error would have operators re-running a delete that
    /// already worked.
    /// </summary>
    [Fact]
    public async Task TestUninstallReportsAPendingFileRemovalAsASuccess()
    {
        _pluginsService.UninstallPluginAsync("enabledPlugin")
            .Returns(new PluginUninstallResult
            {
                Success = true,
                PluginName = "enabledPlugin",
                RemovalPending = true,
                Message = "Its files are still in use by the server."
            });

        var result = await _controller.Uninstall("enabledPlugin");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var value = Assert.IsType<PluginUninstallResult>(ok.Value);

        Assert.True(value.Success);
        Assert.True(value.RemovalPending);
    }

    [Fact]
    public async Task TestUninstallOfAnUnknownPluginIsNotFoundAndTouchesNothing()
    {
        var result = await _controller.Uninstall("ghostPlugin");

        Assert.IsType<NotFoundResult>(result.Result);
        _ = _pluginsService.DidNotReceive().UninstallPluginAsync(Arg.Any<string>());
    }

    /// <summary>
    /// A refused removal keeps its reason, for the same reason a refused upload does: "check the
    /// server's permissions on its Plugins directory" is the whole content of the response.
    /// </summary>
    [Fact]
    public async Task TestUninstallReturnsTheRefusalReason()
    {
        _pluginsService.UninstallPluginAsync("disabledPlugin")
            .Returns(new PluginUninstallResult
            {
                Success = false,
                PluginName = "disabledPlugin",
                Message = "The directory could neither be deleted nor marked for removal."
            });

        var result = await _controller.Uninstall("disabledPlugin");

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var value = Assert.IsType<PluginUninstallResult>(bad.Value);

        Assert.False(value.Success);
        Assert.Contains("could neither be deleted", value.Message);
    }
}
