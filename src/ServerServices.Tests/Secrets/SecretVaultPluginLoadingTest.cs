using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Contracts.Secrets;
using JetBrains.Annotations;
using Model.Secrets;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The real BastionVault plugin, loaded off disk by the real <see cref="PluginsService"/>.
///
/// Every other test in this area substitutes the plugin. This one does not, and it is here for one
/// specific failure that substitution cannot reach: if <c>Contracts.dll</c> is copied beside the
/// plugin, the loader gives it a second copy of the assembly, the plugin's
/// <c>INetriskSecretVaultPlugin</c> becomes a different type from the host's,
/// <c>IsAssignableFrom</c> returns false — and the plugin is silently ignored. No exception, no log
/// line, no plugin. The plugin project excludes Contracts from its runtime output precisely to avoid
/// that, and this test is what proves the exclusion is still in place.
///
/// It also covers the plumbing between the two: that the assembly's name ends in <c>Plugin.dll</c> so
/// discovery sees it, that a directory under <c>Plugins</c> is scanned, and that an unsigned plugin
/// loads with a warning rather than being refused under the default policy.
/// </summary>
[Collection(PluginDirectoryCollection.Name)]
[TestSubject(typeof(PluginsService))]
public class SecretVaultPluginLoadingTest : InMemoryServiceTestBase, IDisposable
{
    private const string PluginName = "BastionVaultPlugin";

    private readonly IPluginsService _plugins;

    public SecretVaultPluginLoadingTest()
    {
        _plugins = GetService<IPluginsService>();

        PluginDirectoryCollection.Clear();
        InstallPluginFixture();
    }

    /// <summary>
    /// Takes the plugin back out again. Without this, every later test that asserts "this host has no
    /// plugins" would depend on which class ran first — and the collection only guarantees they do not
    /// overlap, not the order.
    /// </summary>
    public void Dispose()
    {
        PluginDirectoryCollection.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Copies the built plugin into the test host's <c>Plugins/Secrets</c>, which is exactly where an
    /// installation puts it.
    /// </summary>
    private static void InstallPluginFixture()
    {
        var source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginFixtures");

        Assert.True(Directory.Exists(source),
            "The BastionVault plugin fixture was not staged. See the StageBastionVaultPluginFixture "
            + "target in ServerServices.Tests.csproj.");

        var destination = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins",
            SecretVaultDefaults.PluginDirectory);

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    [Fact]
    public async Task TheBuiltPluginIsDiscoveredAndIdentifiesItself()
    {
        await _plugins.LoadPluginsAsync();

        var info = Assert.Single((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));

        Assert.Equal("1.0.0", info.Version);
        Assert.Contains("BastionVault", info.Description);

        // Discovered, but off: PluginIsEnabledAsync answers false until an administrator says
        // otherwise, so dropping a DLL into the directory does not by itself activate anything.
        Assert.False(info.IsEnabled);
    }

    [Fact]
    public async Task ItIsRecognisedAsASecretVaultPluginAcrossTheLoadContextBoundary()
    {
        await _plugins.LoadPluginsAsync();

        var plugin = await _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>(PluginName);

        // The assertion this whole file exists for. A false here means the host and the plugin are
        // looking at two different copies of Contracts.dll, and the symptom in production is a plugin
        // that is present in the directory and invisible to the feature.
        Assert.NotNull(plugin);
        Assert.Equal("bastionvault", plugin.VaultKind);
        Assert.False(plugin.RequiresMachineId);
    }

    [Fact]
    public async Task ItIsOfferedOnceItIsEnabled()
    {
        var settings = GetService<ISettingsService>();
        await settings.SetConfigurationKeyValueAsync("Plugin_" + PluginName + "_Enabled", "true");

        await _plugins.LoadPluginsAsync();

        var enabled = await _plugins.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>();

        Assert.Contains(enabled, p => p.PluginName == PluginName);
    }

    [Fact]
    public async Task ADisabledPluginIsLoadedButNotOffered()
    {
        var settings = GetService<ISettingsService>();
        await settings.SetConfigurationKeyValueAsync("Plugin_" + PluginName + "_Enabled", "false");

        await _plugins.LoadPluginsAsync();

        // Present, so the connection editor can say "installed but disabled" rather than
        // "not installed" — two states with entirely different remedies.
        Assert.True(await _plugins.PluginExistsAsync(PluginName));
        Assert.Empty(await _plugins.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>());
    }

    [Fact]
    public async Task AskingForADifferentPluginNameDoesNotReturnThisOne()
    {
        await _plugins.LoadPluginsAsync();

        // GetPluginByNameAsync matches the name, unlike GetPluginAsync, which returns the first
        // instance assignable to the requested capability from any loader. On an installation with two
        // vault plugins that difference decides which vault a connection talks to.
        Assert.Null(await _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>("SomeOtherPlugin"));
    }
}

/// <summary>
/// Serializes the test classes that manipulate the host's <c>Plugins</c> directory.
///
/// <see cref="PluginCapabilityDiscoveryTest"/> deletes it to prove the missing-directory guard;
/// <see cref="SecretVaultPluginLoadingTest"/> fills it with a real plugin. Run in parallel those two
/// are a coin flip, and the failure would look like a flaky loader rather than a test-isolation
/// problem.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PluginDirectoryCollection
{
    public const string Name = "PluginsDirectory";

    /// <summary>Removes the host's <c>Plugins</c> directory, so a test starts from a known state.</summary>
    public static void Clear()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
