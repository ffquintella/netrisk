using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Secrets;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using ServerServices.Plugins;
using ServerServices.Services;
using ServerServices.Tests.Secrets;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Plugins;

/// <summary>
/// Installing a new version of an installed plugin, and removing one, against the real
/// <see cref="PluginsService"/> and a real plugin on disk.
///
/// <para>The reported defect: uploading BastionVaultPlugin 1.2.1 over 1.2.0 left both installed, so
/// administration listed the plugin twice at two versions with two independent enabled switches.
/// The cause was that the install directory was named after the uploaded file, and release packages
/// are named for their version. The two assertions that matter here are that a second install
/// replaces the first rather than joining it, and that a directory left over from the old naming is
/// swept when the plugin is installed again.</para>
///
/// <para>Everything is asserted through the service rather than the filesystem where possible,
/// because the filesystem answer differs by platform: a loaded plugin assembly stays locked in this
/// process on Windows, so a removal there disables the plugin and marks the directory for deletion
/// on the next start instead of deleting it now. Both outcomes have to leave the plugin off the
/// list, and that is what is checked.</para>
/// </summary>
[Collection(PluginDirectoryCollection.Name)]
[TestSubject(typeof(PluginsService))]
public class PluginInstallationLifecycleTest : InMemoryServiceTestBase, IDisposable
{
    private const string PluginName = "FixtureVaultPlugin";
    private const string AssemblyName = "FixtureVaultPlugin.dll";

    /// <summary>The version the built fixture reports from its <c>PluginVersion</c> property.</summary>
    private const string FixtureVersion = "1.0.0";

    private readonly IPluginsService _plugins;

    /// <summary>
    /// A plugins root this test case owns, pointed at through
    /// <see cref="PluginsService.PluginsPathEnvironmentVariable"/>.
    /// </summary>
    /// <remarks>
    /// Not the host's own <c>Plugins</c> directory, and not shared between cases, because a plugin
    /// assembly this process has loaded stays locked for the rest of the process's life on Windows:
    /// a shared root cannot be cleared between cases, and a case that began by clearing it would
    /// fail on the leftovers of the one before. The variable is process-global, which is what the
    /// collection attribute is for.
    /// </remarks>
    private readonly string _pluginsRoot = Path.Combine(Path.GetTempPath(),
        "nr-plugins-" + Guid.NewGuid().ToString("N"));

    private readonly string _fixtureSource =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginFixtures");

    public PluginInstallationLifecycleTest()
    {
        _plugins = GetService<IPluginsService>();

        Assert.True(Directory.Exists(_fixtureSource),
            "The vault plugin fixture was not staged. See the StageVaultPluginFixture target in "
            + "ServerServices.Tests.csproj.");

        Directory.CreateDirectory(_pluginsRoot);
        Environment.SetEnvironmentVariable(PluginsService.PluginsPathEnvironmentVariable, _pluginsRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PluginsService.PluginsPathEnvironmentVariable, null);

        try
        {
            if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
        }
        catch (Exception)
        {
            // A plugin this process loaded holds its own file open until the process ends, so the
            // temp root often cannot be removed here. Leaking it is not a test failure.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Copies the built fixture plugin into <c>Plugins/&lt;directory&gt;</c>.</summary>
    private void InstallByHand(string directory)
    {
        var destination = Path.Combine(_pluginsRoot, directory);
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(_fixtureSource))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    /// <summary>A package of the fixture plugin, as an operator's release zip would look.</summary>
    private string BuildPackage(string fileName, string? rootFolder = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nr-pkg-{Guid.NewGuid():N}-{fileName}");

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var file in Directory.GetFiles(_fixtureSource))
        {
            var entryName = rootFolder is null
                ? Path.GetFileName(file)
                : rootFolder + "/" + Path.GetFileName(file);

            archive.CreateEntryFromFile(file, entryName);
        }

        return path;
    }

    private static void Discard(string file)
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

    private static bool StillHoldsThePlugin(string directory) =>
        Directory.Exists(directory) &&
        !File.Exists(Path.Combine(directory, PluginPackageInstaller.UninstalledMarkerFile));


    /// <summary>
    /// A package of the fixture plugin that reports <paramref name="version"/> instead of
    /// <see cref="FixtureVersion"/>.
    /// </summary>
    /// <remarks>
    /// The version is patched into the built assembly's bytes rather than built from a second
    /// source project. <c>PluginVersion</c> is a string literal, literals live in the metadata's
    /// user-string heap as UTF-16, and a replacement of the same length leaves every offset around
    /// it untouched — so the result is the one thing this test needs and cannot otherwise get
    /// cheaply: two packages of one plugin that differ only in the version they report.
    /// </remarks>
    private string BuildPackageReporting(string version)
    {
        Assert.Equal(FixtureVersion.Length, version.Length);

        var path = Path.Combine(Path.GetTempPath(), $"nr-pkg-{Guid.NewGuid():N}-{version}.zip");

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var file in Directory.GetFiles(_fixtureSource))
        {
            var bytes = File.ReadAllBytes(file);

            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                bytes = ReportVersion(bytes, version);

            var entry = archive.CreateEntry(Path.GetFileName(file));
            using var target = entry.Open();
            target.Write(bytes);
        }

        return path;
    }

    /// <summary>Rewrites every UTF-16 <see cref="FixtureVersion"/> literal in an assembly image.</summary>
    private static byte[] ReportVersion(byte[] assembly, string version)
    {
        var from = Encoding.Unicode.GetBytes(FixtureVersion);
        var to = Encoding.Unicode.GetBytes(version);

        var patched = 0;

        for (var i = 0; i <= assembly.Length - from.Length; i++)
        {
            var match = true;

            for (var j = 0; j < from.Length && match; j++)
                match = assembly[i + j] == from[j];

            if (!match) continue;

            Array.Copy(to, 0, assembly, i, to.Length);
            patched++;
        }

        Assert.True(patched > 0,
            $"The fixture assembly carries no '{FixtureVersion}' literal to patch. Has "
            + "FixtureVaultPlugin.PluginVersion changed?");

        return assembly;
    }

    /// <summary>
    /// The reported defect, end to end: uploading a new version of an installed plugin and being
    /// told it was updated, while the list goes on showing the version before it.
    /// </summary>
    /// <remarks>
    /// <para>The cause was not the installation — the new assembly really was extracted — but the
    /// reload. CoreCLR caches a loaded assembly image by file path, so pointing a fresh
    /// <c>AssemblyLoadContext</c> at a path this process has already loaded returns the assembly
    /// already loaded there, whatever the file now holds. Installing over the previous directory
    /// therefore could not take effect before a restart, and the operator's evidence was a version
    /// column that disagreed with the success message.</para>
    ///
    /// <para>This fails on the pre-fix code, which installed every version of a plugin into one
    /// directory named after its assembly: the second assertion reads the old version.</para>
    /// </remarks>
    [Fact]
    public async Task InstallingANewVersionListsTheNewVersionWithoutARestart()
    {
        var first = BuildPackageReporting(FixtureVersion);
        var second = BuildPackageReporting("1.9.9");

        try
        {
            await using (var stream = File.OpenRead(first))
                Assert.True((await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin.zip"))
                    .Success);

            Assert.Equal(FixtureVersion, (await _plugins.GetPluginsAsync())
                .Single(p => p.Name == PluginName).Version);

            await using (var stream = File.OpenRead(second))
            {
                var result = await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin.zip");

                Assert.True(result.Success, result.Message);
                Assert.True(result.ReplacedExisting);
            }

            var row = Assert.Single((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));

            Assert.Equal("1.9.9", row.Version);
        }
        finally
        {
            Discard(first);
            Discard(second);
        }
    }

    /// <summary>
    /// The regression for the screenshot: one plugin, two directories, one row.
    /// </summary>
    [Fact]
    public async Task ADuplicateInstallationIsListedOnceAtItsNewestVersion()
    {
        InstallByHand("FixtureVaultPlugin-1.0.0");
        InstallByHand("FixtureVaultPlugin-1.0.1");

        await _plugins.LoadPluginsAsync();

        var rows = (await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName).ToList();

        var row = Assert.Single(rows);
        Assert.NotEmpty(row.PackageName);
    }

    /// <summary>
    /// A package named for its release installs under the plugin's own name plus an install stamp,
    /// whatever the uploaded file was called.
    /// </summary>
    [Fact]
    public async Task AReleasePackageInstallsUnderThePluginName()
    {
        var package = BuildPackage("FixtureVaultPlugin-1.0.0.zip", "FixtureVaultPlugin-1.0.0");

        try
        {
            await using var stream = File.OpenRead(package);
            var result = await _plugins.InstallPluginPackageAsync(stream, Path.GetFileName(package));

            Assert.True(result.Success, result.Message);
            Assert.StartsWith(PluginName + PluginPackageInstaller.InstallStampSeparator,
                result.PackageName);
            Assert.Contains(PluginName, result.LoadedPlugins);
            Assert.True(File.Exists(Path.Combine(_pluginsRoot, result.PackageName, AssemblyName)));
        }
        finally
        {
            Discard(package);
        }
    }

    /// <summary>
    /// Installing a second release replaces the first: one row, and no second directory.
    /// </summary>
    [Fact]
    public async Task InstallingASecondReleaseReplacesTheFirst()
    {
        var first = BuildPackage("FixtureVaultPlugin-1.0.0.zip");
        var second = BuildPackage("FixtureVaultPlugin-1.0.1.zip");

        try
        {
            string firstDirectory;

            await using (var stream = File.OpenRead(first))
            {
                var result = await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin-1.0.0.zip");

                Assert.True(result.Success, result.Message);
                firstDirectory = result.PackageName;
            }

            await using (var stream = File.OpenRead(second))
            {
                var result = await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin-1.0.1.zip");

                Assert.True(result.Success, result.Message);
                Assert.True(result.ReplacedExisting);

                // A directory this process has already loaded from would serve the old assembly
                // however the new one is written, so the replacement gets its own.
                Assert.NotEqual(firstDirectory, result.PackageName);
                Assert.Contains(firstDirectory, result.RemovedDirectories);
            }

            Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, firstDirectory)));
            Assert.Single((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
        }
        finally
        {
            Discard(first);
            Discard(second);
        }
    }

    /// <summary>
    /// The upgrade path for an installation that already accumulated one directory per release:
    /// installing the plugin again removes the older directories rather than leaving them loading.
    /// </summary>
    [Fact]
    public async Task InstallingClearsDirectoriesLeftByTheOldNaming()
    {
        InstallByHand("FixtureVaultPlugin-1.0.0");
        InstallByHand("FixtureVaultPlugin-1.0.1");

        await _plugins.LoadPluginsAsync();

        var package = BuildPackage("FixtureVaultPlugin-1.0.2.zip");

        try
        {
            await using var stream = File.OpenRead(package);
            var result = await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin-1.0.2.zip");

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.RemovedDirectories.Count);
            Assert.Contains("FixtureVaultPlugin-1.0.0", result.RemovedDirectories);
            Assert.Contains("FixtureVaultPlugin-1.0.1", result.RemovedDirectories);

            // Deleted outright, or marked and skipped when this process still holds the assembly.
            Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, "FixtureVaultPlugin-1.0.0")));
            Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, "FixtureVaultPlugin-1.0.1")));

            Assert.Single((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
        }
        finally
        {
            Discard(package);
        }
    }

    /// <summary>
    /// A plugin installed twice is offered once.
    /// </summary>
    /// <remarks>
    /// Before this, every capability lookup walked the loaders and took what it found: the vault
    /// connection editor listed the same plugin twice, and — worse, because it is silent — the copy
    /// that actually served a credential was whichever directory the filesystem enumerated first.
    /// Both rows share one <c>Plugin_&lt;name&gt;_Enabled</c> setting, so the administration screen
    /// could show the newer copy enabled while the older one did the work.
    /// </remarks>
    [Fact]
    public async Task ADuplicateInstallationIsOfferedOnceWhenEnabled()
    {
        InstallByHand("FixtureVaultPlugin-1.0.0");
        InstallByHand("FixtureVaultPlugin-1.0.1");

        var settings = GetService<ISettingsService>();
        await settings.SetConfigurationKeyValueAsync("Plugin_" + PluginName + "_Enabled", "true");

        await _plugins.LoadPluginsAsync();

        var enabled = await _plugins.GetEnabledPluginsAsync<INetriskSecretVaultPlugin>();

        Assert.Single(enabled.Where(p => p.PluginName == PluginName));
    }

    /// <summary>
    /// Looking a duplicated plugin up by name returns exactly one instance, and never fails.
    /// </summary>
    [Fact]
    public async Task ADuplicatedPluginStillResolvesByName()
    {
        InstallByHand("FixtureVaultPlugin-1.0.0");
        InstallByHand("FixtureVaultPlugin-1.0.1");

        await _plugins.LoadPluginsAsync();

        var plugin = await _plugins.GetPluginByNameAsync<INetriskSecretVaultPlugin>(PluginName);

        Assert.NotNull(plugin);
        Assert.Equal(PluginName, plugin.PluginName);
    }

    [Fact]
    public async Task UninstallingRemovesThePluginFromTheList()
    {
        InstallByHand("Secrets");

        await _plugins.LoadPluginsAsync();
        Assert.True(await _plugins.PluginExistsAsync(PluginName));

        var result = await _plugins.UninstallPluginAsync(PluginName);

        Assert.True(result.Success, result.Message);
        Assert.Equal(PluginName, result.PluginName);
        Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, "Secrets")));
        Assert.False(await _plugins.PluginExistsAsync(PluginName));
        Assert.Empty((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
    }

    /// <summary>
    /// The plugin is switched off as part of the removal. Without this, reinstalling it -- or, on
    /// Windows, the rest of the process's lifetime, where the assembly stays loaded -- would find it
    /// enabled again with nobody having enabled it.
    /// </summary>
    [Fact]
    public async Task UninstallingDisablesThePlugin()
    {
        InstallByHand("Secrets");

        var settings = GetService<ISettingsService>();
        await settings.SetConfigurationKeyValueAsync("Plugin_" + PluginName + "_Enabled", "true");

        await _plugins.LoadPluginsAsync();
        Assert.True(await _plugins.PluginIsEnabledAsync(PluginName));

        Assert.True((await _plugins.UninstallPluginAsync(PluginName)).Success);

        Assert.False(await _plugins.PluginIsEnabledAsync(PluginName));
    }

    /// <summary>
    /// A duplicate installation is removed in full: deleting the plugin has to clear every
    /// directory providing it, or the row comes straight back on the next reload.
    /// </summary>
    [Fact]
    public async Task UninstallingRemovesEveryDirectoryProvidingThePlugin()
    {
        InstallByHand("FixtureVaultPlugin-1.0.0");
        InstallByHand("FixtureVaultPlugin-1.0.1");

        await _plugins.LoadPluginsAsync();

        var result = await _plugins.UninstallPluginAsync(PluginName);

        Assert.True(result.Success, result.Message);
        Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, "FixtureVaultPlugin-1.0.0")));
        Assert.False(StillHoldsThePlugin(Path.Combine(_pluginsRoot, "FixtureVaultPlugin-1.0.1")));
        Assert.Empty((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
    }

    /// <summary>
    /// A removed plugin stays removed across reloads.
    /// </summary>
    /// <remarks>
    /// The regression for a mistake in the first version of this feature: the load pass swept an
    /// uninstalled directory with a recursive delete, which removed the marker it had just read and
    /// left the locked assembly behind, so the pass after that found a plugin directory with no
    /// marker and loaded the deleted plugin again.
    /// </remarks>
    [Fact]
    public async Task AnUninstalledPluginDoesNotComeBackOnTheNextReload()
    {
        InstallByHand("Secrets");

        await _plugins.LoadPluginsAsync();
        Assert.True((await _plugins.UninstallPluginAsync(PluginName)).Success);

        await _plugins.LoadPluginsAsync();
        await _plugins.LoadPluginsAsync();

        Assert.False(await _plugins.PluginExistsAsync(PluginName));
        Assert.Empty((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
    }

    [Fact]
    public async Task UninstallingAPluginThatIsNotInstalledIsRefusedWithAReason()
    {
        await _plugins.LoadPluginsAsync();

        var result = await _plugins.UninstallPluginAsync("NoSuchPlugin");

        Assert.False(result.Success);
        Assert.Contains("not installed", result.Message);
    }

    /// <summary>
    /// The previous version of a plugin is staged inside the plugins root — a rename cannot cross a
    /// filesystem, and on a Linux host the temp directory is a different one — so the staging
    /// directory sits where the loader looks. It must not be loaded: it holds a plugin assembly, and
    /// loading it would put the same plugin on the list twice.
    /// </summary>
    [Fact]
    public async Task AStagingDirectoryIsNotLoadedAsAPlugin()
    {
        InstallByHand(PluginPackageInstaller.StagingDirectoryPrefix + "deadbeef");

        await _plugins.LoadPluginsAsync();

        Assert.Empty((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
        Assert.False(await _plugins.PluginExistsAsync(PluginName));
    }

    /// <summary>
    /// A staging directory left behind by a host that died mid-install is cleared by the next
    /// install, rather than accumulating one per attempt.
    /// </summary>
    [Fact]
    public async Task InstallingClearsAStagingDirectoryLeftByAnUnfinishedInstall()
    {
        InstallByHand(PluginName);
        InstallByHand(PluginPackageInstaller.StagingDirectoryPrefix + "deadbeef");

        await _plugins.LoadPluginsAsync();

        var package = BuildPackage("FixtureVaultPlugin-1.0.1.zip");

        try
        {
            await using var stream = File.OpenRead(package);
            var result = await _plugins.InstallPluginPackageAsync(stream, "FixtureVaultPlugin-1.0.1.zip");

            Assert.True(result.Success, result.Message);

            var left = Directory.GetDirectories(_pluginsRoot);

            Assert.DoesNotContain(left, PluginPackageInstaller.IsStagingDirectory);
            Assert.Equal(Path.Combine(_pluginsRoot, result.PackageName), Assert.Single(left));
            Assert.Single((await _plugins.GetPluginsAsync()).Where(p => p.Name == PluginName));
        }
        finally
        {
            Discard(package);
        }
    }
}
