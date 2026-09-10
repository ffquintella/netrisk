using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.Plugins;
using NSubstitute;
using ReliableRestClient.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(PluginsRestService))]
public class PluginsRestServiceTest : BaseServiceTest, IDisposable
{
    private readonly StubRestBackend _backend = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IPluginsService _service;

    public PluginsRestServiceTest()
    {
        // The service reaches for IAuthenticationService on a 401, so it gets a double the test
        // can assert against rather than the real REST implementation.
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IPluginsService>();
    }

    private static List<PluginInfo> TwoPlugins() =>
    [
        new() { Name = "Nessus", Description = "Nessus importer", Version = "1.0.0", IsEnabled = true },
        new() { Name = "Jira", Description = "Jira sync", Version = "2.1.0", IsEnabled = false }
    ];

    // ---------------- GetPluginsAsync ----------------

    [Fact]
    public async Task TestGetPluginsAsync()
    {
        _backend.OnGet("/Plugins", TwoPlugins());

        var plugins = await _service.GetPluginsAsync();

        Assert.Equal(2, plugins.Count);
        Assert.Equal("Nessus", plugins[0].Name);
        Assert.True(plugins[0].IsEnabled);
        Assert.Equal("2.1.0", plugins[1].Version);
        Assert.False(plugins[1].IsEnabled);
        Assert.Equal("GET /Plugins", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetPluginsAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, "/Plugins", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestException>(() => _service.GetPluginsAsync());
        Assert.Equal(500, ex.HttpCode);
    }

    [Fact]
    public async Task TestGetPluginsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Plugins", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetPluginsAsync());
        _authentication.DidNotReceive().DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestGetPluginsAsyncDiscardsTheTokenOnUnauthorized()
    {
        _backend.OnStatus(Method.Get, "/Plugins", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetPluginsAsync());
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestGetPluginsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Plugins");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetPluginsAsync());
    }

    // ---------------- SetPluginEnabledAsync ----------------

    [Theory]
    [InlineData(true, "/Plugins/enable/Nessus")]
    [InlineData(false, "/Plugins/disable/Nessus")]
    public async Task TestSetPluginEnabledAsyncCallsTheMatchingRoute(bool enabled, string expectedPath)
    {
        _backend.On(Method.Get, expectedPath, "true");

        await _service.SetPluginEnabledAsync("Nessus", enabled);

        Assert.Equal("GET " + expectedPath, _backend.LastRequest.ToString());
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestSetPluginEnabledAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Plugins/enable/Nessus", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SetPluginEnabledAsync("Nessus", true));
        Assert.Equal("Error setting plugin status", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestSetPluginEnabledAsyncDiscardsTheTokenOnUnauthorized()
    {
        _backend.OnStatus(Method.Get, "/Plugins/disable/Nessus", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SetPluginEnabledAsync("Nessus", false));
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestSetPluginEnabledAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Plugins/enable/Nessus");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SetPluginEnabledAsync("Nessus", true));
    }

    // ---------------- RequestPluginsReloadAsync ----------------

    [Fact]
    public async Task TestRequestPluginsReloadAsync()
    {
        _backend.On(Method.Get, "/Plugins/reload", "true");

        await _service.RequestPluginsReloadAsync();

        Assert.Equal("GET /Plugins/reload", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestRequestPluginsReloadAsyncThrowsWhenTheCallIsNotSuccessful()
    {
        // NotFound does not raise inside RestSharp, so the service's own IsSuccessful check runs.
        _backend.OnStatus(Method.Get, "/Plugins/reload", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestException>(() => _service.RequestPluginsReloadAsync());
        Assert.Equal(500, ex.HttpCode);
    }

    [Fact]
    public async Task TestRequestPluginsReloadAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Plugins/reload", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.RequestPluginsReloadAsync());
        Assert.Equal("Error reloading all plugins", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestRequestPluginsReloadAsyncDiscardsTheTokenOnUnauthorized()
    {
        _backend.OnStatus(Method.Get, "/Plugins/reload", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RequestPluginsReloadAsync());
        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestRequestPluginsReloadAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Plugins/reload");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RequestPluginsReloadAsync());
    }

    // ---------------- UploadPluginAsync ----------------

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException)
            {
                // A leaked temp file is not a test failure.
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A real file on disk, because RestSharp's AddFile takes a path and reads it.</summary>
    private string TempPackage(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nr-pkg-{Guid.NewGuid():N}-{name}");
        _tempFiles.Add(path);

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        archive.CreateEntry("MyVault.Plugin.dll");

        return path;
    }

    [Fact]
    public async Task TestUploadPluginAsync()
    {
        _backend.OnPost("/Plugins/upload", new PluginInstallResult
        {
            Success = true,
            PackageName = "MyVault.Plugin",
            Message = "Installed MyVault.Plugin.",
            LoadedPlugins = ["MyVault"]
        });

        var result = await _service.UploadPluginAsync(TempPackage("MyVault.Plugin.zip"));

        Assert.True(result.Success);
        Assert.Equal("MyVault.Plugin", result.PackageName);
        Assert.Equal("POST /Plugins/upload", _backend.LastRequest.ToString());
    }

    /// <summary>
    /// The server refuses a package with a 400 whose body says why. That message is the whole value
    /// of the response — it names what the operator has to change — so the service must read the
    /// body rather than turning the status code into a generic failure.
    /// </summary>
    [Fact]
    public async Task TestUploadPluginAsyncReturnsTheServersRefusalReason()
    {
        _backend.OnPost("/Plugins/upload", new PluginInstallResult
        {
            Success = false,
            PackageName = "notaplugin",
            Message = "The package has no *Plugin.dll at its top level."
        }, HttpStatusCode.BadRequest);

        var result = await _service.UploadPluginAsync(TempPackage("notaplugin.zip"));

        Assert.False(result.Success);
        Assert.Contains("Plugin.dll", result.Message);
    }

    [Fact]
    public async Task TestUploadPluginAsyncReportsAFailureWithNoBody()
    {
        _backend.OnStatus(Method.Post, "/Plugins/upload", HttpStatusCode.InternalServerError);

        var result = await _service.UploadPluginAsync(TempPackage("MyVault.Plugin.zip"));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>
    /// The realistic expired-session case: a plain 401 response. It matters separately from the
    /// transport-failure one below because this client does not throw on a non-2xx — the token
    /// discard has to happen on the response path or it does not happen at all.
    /// </summary>
    [Fact]
    public async Task TestUploadPluginAsyncDiscardsTheTokenOnAnUnauthorizedResponse()
    {
        _backend.OnStatus(Method.Post, "/Plugins/upload", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() =>
            _service.UploadPluginAsync(TempPackage("MyVault.Plugin.zip")));

        _authentication.Received(1).DiscardAuthenticationToken();
    }

    [Fact]
    public async Task TestUploadPluginAsyncDiscardsTheTokenOnUnauthorized()
    {
        _backend.OnTransportFailure(Method.Post, "/Plugins/upload",
            new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<RestComunicationException>(() =>
            _service.UploadPluginAsync(TempPackage("MyVault.Plugin.zip")));

        _authentication.Received(1).DiscardAuthenticationToken();
    }
}
