using System;
using System.Collections.Generic;
using System.Net;
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
public class PluginsRestServiceTest : BaseServiceTest
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
}
