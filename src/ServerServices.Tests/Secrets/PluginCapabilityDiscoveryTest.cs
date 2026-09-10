using System;
using System.IO;
using System.Threading.Tasks;
using Contracts.Secrets;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// Plugin discovery on a host that has no plugins — which is every test host, and most installations.
///
/// This matters more than it did before the vault feature: <c>ISecretResolver</c> now sits on the
/// credential read path of every integration, and it asks the plugin service a question on any field
/// that holds a vault reference. So "there are no plugins" has to be an answer rather than a fault.
/// </summary>
[Collection(PluginDirectoryCollection.Name)]
[TestSubject(typeof(PluginsService))]
public class PluginCapabilityDiscoveryTest : InMemoryServiceTestBase
{
    /// <summary>
    /// Resolved once, not per access. <c>IPluginsService</c> is registered transient, and its
    /// initialized/loaded state lives on the instance — a property that resolved a fresh one on every
    /// use would make "load, then observe" untestable.
    /// </summary>
    private readonly IPluginsService _plugins;

    public PluginCapabilityDiscoveryTest()
    {
        _plugins = GetService<IPluginsService>();

        // These tests are about a host with no plugins, and the host here is the test binary's own
        // directory — which SecretVaultPluginLoadingTest fills with a real plugin. Both classes are in
        // the PluginsDirectory collection so they never run at the same time, and each starts from a
        // known state rather than from whatever the previous one left.
        PluginDirectoryCollection.Clear();
    }

    [Fact]
    public async Task AHostWithNoPluginsReportsNoSecretVaultPlugins()
    {
        Assert.Empty(await _plugins.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>());
    }

    [Fact]
    public async Task LookingUpAPluginByNameReturnsNullRatherThanThrowingWhenItIsAbsent()
    {
        // The difference from GetPluginAsync, and the reason the overload exists: "the plugin this
        // connection needs is not installed" is a state to report to an operator, not an exception to
        // propagate out of a sync job.
        Assert.Null(await _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>("FixtureVaultPlugin"));
    }

    [Fact]
    public async Task LoadingPluginsSucceedsWhenThePluginsDirectoryDoesNotExist()
    {
        // The regression: Directory.GetDirectories threw on a missing directory, so LoadPluginsAsync
        // failed outright and every caller asking "is a plugin available" got an exception instead of
        // "no" — including, now, the resolver on a credential read.
        var pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

        if (Directory.Exists(pluginsPath)) Directory.Delete(pluginsPath, recursive: true);

        await _plugins.LoadPluginsAsync();

        Assert.True(_plugins.IsInitialized());
        Assert.Empty(await _plugins.GetPluginsAsync());

        // Recreated, so a plugin dropped in later is found without the directory having to be made by
        // hand — and so a concurrently running test sees it too.
        Assert.True(Directory.Exists(pluginsPath));
    }

    [Fact]
    public async Task AnEmptyPluginsDirectoryYieldsNoPlugins()
    {
        var pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        Directory.CreateDirectory(Path.Combine(pluginsPath, "Secrets"));

        await _plugins.LoadPluginsAsync();

        Assert.Empty(await _plugins.GetPluginsAsync());
        Assert.False(await _plugins.PluginExistsAsync("FixtureVaultPlugin"));
        Assert.False(await _plugins.PluginIsEnabledAsync("FixtureVaultPlugin"));
    }
}
