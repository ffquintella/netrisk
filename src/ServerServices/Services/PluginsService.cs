using Contracts;
using Contracts.Secrets;
using System.IO.Compression;
using McMaster.NETCore.Plugins;
using Model.Plugins;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Plugins;
using ServerServices.Security;
using Tools.Security;

namespace ServerServices.Services;

public class PluginsService: ServiceBase, IPluginsService
{

    private List<string> _plugins = new List<string>();
    private List<string> _pluginsDirs = new List<string>();
    private List<LoadedPluginAssembly> _pluginLoaders = new List<LoadedPluginAssembly>();
    private bool _initialized = false;
    private ISettingsService SettingsService { get; }

    /// <summary>
    /// Whether an unsigned or untrusted plugin is refused rather than merely reported
    /// (security finding NR-2026-027). Off by default: turning it on in an upgrade would silently
    /// disable every plugin an installation already runs, and a security control that arrives as an
    /// outage is a control that gets turned back off.
    /// </summary>
    public const string RequireSignatureSetting = "plugins_require_signature";

    /// <summary>SHA-256 thumbprints of the publishers this installation trusts. Empty means any
    /// valid signature is accepted, which still proves the file was not swapped after signing.</summary>
    public const string TrustedPublishersSetting = "plugins_trusted_publishers";

    private readonly PluginSignatureVerifier _signatureVerifier;

    /// <summary>
    /// A loaded plugin assembly together with the directory it came from.
    ///
    /// The directory is carried rather than re-derived because it is the unit of installation and of
    /// removal: a plugin is a directory under <c>Plugins/</c>, and both "which installation is this
    /// row" and "what does deleting this plugin delete" are unanswerable from a loader alone.
    /// </summary>
    private sealed record LoadedPluginAssembly(PluginLoader Loader, string Directory);

    public PluginsService(ILogger logger, IDalService dalService, ISettingsService settingsService) : base(logger, dalService)
    {
        SettingsService = settingsService;
        _signatureVerifier = new PluginSignatureVerifier(logger);
    }

    /// <summary>
    /// The signature policy in force. Read once per load pass rather than per plugin, and any
    /// failure to read it falls back to "report but do not refuse" — a settings table that cannot be
    /// reached must not take the whole plugin surface down with it.
    /// </summary>
    private async Task<(bool Require, string[] Trusted)> ReadSignaturePolicyAsync()
    {
        try
        {
            var require = false;
            if (await SettingsService.ConfigurationKeyExistsAsync(RequireSignatureSetting))
            {
                var value = await SettingsService.GetConfigurationKeyValueAsync(RequireSignatureSetting);
                require = value.Trim().ToLowerInvariant() is "true" or "1" or "yes";
            }

            var trusted = Array.Empty<string>();
            if (await SettingsService.ConfigurationKeyExistsAsync(TrustedPublishersSetting))
                trusted = PluginSignatureVerifier.ParseThumbprints(
                    await SettingsService.GetConfigurationKeyValueAsync(TrustedPublishersSetting));

            return (require, trusted);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read the plugin signature policy, defaulting to report-only: {Message}",
                ex.Message);
            return (false, []);
        }
    }
    
    private List<PluginDll> GetPluginsDlls()
    {
        var dlls = new List<PluginDll>();
        
        var dirs = GetPluginsDirs();

        foreach (var dir in dirs)
        {
            var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);
        
            if (Directory.Exists(pluginPath))
            {
                // A directory whose plugin was uninstalled while its assembly was still locked by
                // this process is skipped and swept: the plugin has to disappear from the list on
                // the uninstall, and the files can only go once nothing holds them, which in
                // practice is the next start.
                if (File.Exists(Path.Combine(pluginPath, PluginPackageInstaller.UninstalledMarkerFile)))
                {
                    SweepUninstalled(pluginPath);

                    if (Directory.Exists(pluginPath))
                        Log.Information(
                            "Skipping plugin directory {Path}: it is marked uninstalled and its files " +
                            "are still in use. They will be removed on a later start.", pluginPath);

                    continue;
                }

                var dirPaths = Directory.GetFiles(pluginPath, "*Plugin.dll");

                foreach (var dirPath in dirPaths)
                {
                    var pdll = new PluginDll
                    {
                        Name = Path.GetFileNameWithoutExtension(dirPath),
                        Path = dirPath,
                        Type = dir
                    };
                    
                    dlls.Add(pdll);
                }
                
            }
            else
            {
                Log.Information("Plugins directory doesn't exist ... creating one");
                Directory.CreateDirectory(pluginPath);
            }
        }

        return dlls;
    }
    
    private string[] GetPluginsDirs()
    {
        var pluginPath = PluginsRoot;

        // A host with no Plugins directory has no plugins; it does not have a broken installation.
        // Without this guard Directory.GetDirectories throws, LoadPluginsAsync fails, and every
        // caller that asks "is a plugin available" gets an exception instead of "no" — which now
        // includes the secret-vault resolver on the credential read path of every integration.
        if (!Directory.Exists(pluginPath))
        {
            Log.Information("Plugins directory {Path} doesn't exist ... creating one", pluginPath);
            Directory.CreateDirectory(pluginPath);
            return [];
        }

        return Directory.GetDirectories(pluginPath);
    }

    public async Task<bool> PluginExistsAsync(string pluginName)
    {
        if(!IsInitialized()) await LoadPluginsAsync();
        
        if (_plugins.Contains(pluginName))
        {
            return true;
        }

        return false;
    }

    public async Task<bool> PluginIsEnabledAsync(string pluginName)
    {
        if(!IsInitialized()) await LoadPluginsAsync();
        
        var configured = await SettingsService.ConfigurationKeyExistsAsync("Plugin_" + pluginName + "_Enabled");

        if (configured)
        {
            var enabledVal = await SettingsService.GetConfigurationKeyValueAsync("Plugin_" + pluginName + "_Enabled");
            if (enabledVal == "true")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    public bool IsInitialized()
    {
        return _initialized;
    }
    

    public async Task LoadPluginsAsync()
    {
        var (requireSignature, trustedPublishers) = await ReadSignaturePolicyAsync();

        var pDlls = GetPluginsDlls();
        _pluginLoaders = new List<LoadedPluginAssembly>();
        _pluginsDirs = new List<string>();
        _plugins = new List<string>();


        foreach (var pDll in pDlls)
        {
            if (!pDll.Path.EndsWith("Plugin.dll")) continue;
            if (!File.Exists(pDll.Path)) continue;

            // Finding NR-2026-027. This does not confine the plugin — nothing in .NET can — but it
            // turns "any DLL in the directory" into "a DLL from a publisher this installation named",
            // and it puts the publisher in the log beside every load.
            var signature = _signatureVerifier.Verify(pDll.Path);
            var trusted = PluginSignatureVerifier.IsTrusted(signature, trustedPublishers);

            if (trusted)
                Log.Information("Plugin assembly {Path} is signed by {Publisher} ({Thumbprint})",
                    pDll.Path, signature.Publisher, signature.Thumbprint);
            else if (requireSignature)
            {
                Log.Error(
                    "REFUSING plugin assembly {Path}: {Detail} The '{Setting}' policy requires a " +
                    "signature from a trusted publisher before a plugin is loaded into the API process.",
                    pDll.Path, signature.Detail ?? "the signature is not from a trusted publisher.",
                    RequireSignatureSetting);
                continue;
            }
            else
                Log.Warning(
                    "Plugin assembly {Path} is loading unverified: {Detail} It will run with the API's " +
                    "full authority. Set '{Setting}' to true once your plugins are signed.",
                    pDll.Path, signature.Detail ?? "no trusted signature.", RequireSignatureSetting);

            try
            {
                // REMEMBER TO ADD THE PLUGINS INTERFACES HERE
                var pluginLoader = PluginLoader.CreateFromAssemblyFile(pDll.Path, sharedTypes: new[]
                {
                    typeof(INetriskPlugin), typeof(INetriskModelPlugin), typeof(INetriskFaceIDPlugin),
                    // Secret-vault capability. Listing it is documentation rather than strictly
                    // necessary — every one of these lives in Contracts.dll and sharing any type
                    // shares the whole assembly — but the next capability may not, and a plugin
                    // whose interface is loaded twice fails an IsAssignableFrom check with no
                    // message that says why.
                    typeof(INetriskSecretVaultPlugin), typeof(IPluginHttpClient)
                });
                _pluginLoaders.Add(new LoadedPluginAssembly(pluginLoader,
                    Path.GetDirectoryName(pDll.Path) ?? string.Empty));

                var pluginTypes = pluginLoader.LoadDefaultAssembly()
                    .GetTypes()
                    .Where(t => typeof(INetriskPlugin).IsAssignableFrom(t));

                foreach (var pluginType in pluginTypes)
                {
                    var plugin = (INetriskPlugin)Activator.CreateInstance(pluginType)! as INetriskPlugin;

                    // A name is recorded once even when two directories hold the same plugin: the
                    // list is asked "does this plugin exist", and answering it twice served no
                    // caller while making every duplicate installation look like two plugins.
                    if (!_plugins.Contains(plugin.PluginName)) _plugins.Add(plugin.PluginName);

                    Log.Information($"Plugin {plugin.PluginName} loaded");
                }  


                
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error loading plugin {pDll}");
            }
        }
    
        _initialized = true;
    }
    
    public async Task<ServiceInformation> GetInfoAsync()
    {
        return await Task.Run(() =>
        {
            var information = new ServiceInformation
            {
                IsServiceAvailable = true,
                ServiceName = "PluginsService",
                ServiceVersion = "1.0",
                ServiceDescription = "Plugins service for managing plugins",
                ServiceUrl = "/plugins",
                ServiceNeedsPlugin = false,
                ServicePluginInstalled = false
            };

            return information;
        });
        
    }

    public async Task SetPluginEnabledStatusAsync(string pluginName, bool enabled)
    {
        if (enabled)
        {
            await SettingsService.SetConfigurationKeyValueAsync("Plugin_" + pluginName + "_Enabled", "true");
        }
        else
        {
            await SettingsService.SetConfigurationKeyValueAsync("Plugin_" + pluginName + "_Enabled", "false");
        }
    }

    public async Task<List<PluginInfo>> GetPluginsAsync()
    {

        var pluginInfos = new List<PluginInfo>();
        
        if(!IsInitialized()) await LoadPluginsAsync();

        foreach (var loaded in _pluginLoaders)
        {
            var pluginTypes = loaded.Loader.LoadDefaultAssembly()
                .GetTypes()
                .Where(t => typeof(INetriskPlugin).IsAssignableFrom(t));

            foreach (var pluginType in pluginTypes)
            {
                var netriskPlugin = (INetriskPlugin)Activator.CreateInstance(pluginType)!;
                    

                var pluginInfo = new PluginInfo
                {
                    Name = netriskPlugin.PluginName,
                    Description = netriskPlugin.PluginDescription,
                    Version = netriskPlugin.PluginVersion,
                    IsEnabled = await PluginIsEnabledAsync(netriskPlugin.PluginName),
                    PackageName = new DirectoryInfo(loaded.Directory).Name
                };
                
                pluginInfos.Add(pluginInfo);
                
            }  
        }

        return PluginListing.CollapseVersions(pluginInfos);
    }

    public async Task<T> GetPluginAsync<T>(string pluginName) where T: INetriskPlugin
    {
        if(!IsInitialized()) await LoadPluginsAsync();
        
        if(!await PluginExistsAsync(pluginName)) throw new Exception($"Plugin {pluginName} not found");

        //if (typeof(T).Name != pluginName) throw new Exception($"Plugin Name must match the return type not found");

        foreach (var pluginLoader in _pluginLoaders.Select(l => l.Loader))
        {
            var pluginTypes = pluginLoader.LoadDefaultAssembly()
                .GetTypes()
                .Where(tp => typeof(T).IsAssignableFrom(tp));

            foreach (var pluginType in pluginTypes)
            {
                var netriskPlugin = (T)Activator.CreateInstance(pluginType)!;
                    
                return netriskPlugin;
                
            }  
        }
        
        throw new Exception($"Plugin {pluginName} not found");
        
    }

    public async Task<T?> GetPluginByNameAsync<T>(string pluginName) where T : INetriskPlugin
    {
        if (!IsInitialized()) await LoadPluginsAsync();

        // The name is matched, not assumed. GetPluginAsync<T> checks that *some* plugin with the
        // requested name exists and then returns the first instance assignable to T from any loader,
        // which on an installation with two plugins of the same capability silently returns the
        // wrong one. A vault connection names the plugin that services it precisely so that cannot
        // happen, so this overload has to honour it.
        //
        // Matching the name is not sufficient on its own, because the same name can be on disk
        // twice. Of those, the newest version serves: see PluginListing.PreferNewestPerName.
        var matches = Candidates<T>()
            .Where(c => string.Equals(c.Plugin.PluginName, pluginName, StringComparison.Ordinal))
            .ToList();

        return PluginListing
            .PreferNewestPerName(matches, c => c.Plugin.PluginName, c => c.Plugin.PluginVersion)
            .Select(c => c.Plugin)
            .FirstOrDefault();
    }

    /// <summary>
    /// Every instantiable plugin implementing <typeparamref name="T"/>, in loader order, with the
    /// directory each came from.
    /// </summary>
    private List<(T Plugin, string Directory)> Candidates<T>() where T : INetriskPlugin
    {
        var candidates = new List<(T, string)>();

        foreach (var loaded in _pluginLoaders)
        {
            foreach (var pluginType in LoadableTypes(loaded.Loader).Where(t => typeof(T).IsAssignableFrom(t)))
            {
                if (Activator.CreateInstance(pluginType) is not T candidate) continue;

                candidates.Add((candidate, loaded.Directory));
            }
        }

        return candidates;
    }

    public async Task<List<T>> GetEnabledPluginsAsync<T>() where T : INetriskPlugin
    {
        if (!IsInitialized()) await LoadPluginsAsync();

        // One entry per plugin name, newest version. A plugin installed twice used to be offered
        // twice here — two rows in the vault connection editor for one plugin, and no way for the
        // caller to tell which of them a later lookup would resolve to.
        var candidates = PluginListing.PreferNewestPerName(
            Candidates<T>(), c => c.Plugin.PluginName, c => c.Plugin.PluginVersion);

        var found = new List<T>();

        foreach (var candidate in candidates)
        {
            if (!await PluginIsEnabledAsync(candidate.Plugin.PluginName)) continue;

            found.Add(candidate.Plugin);
        }

        return found;
    }

    /// <summary>
    /// Overrides the plugins root. Set it to keep plugins outside the deployed application
    /// directory -- a mounted volume in a container, a path a test owns -- and leave it unset for
    /// the default, which is <c>Plugins</c> beside the host's binaries.
    /// </summary>
    public const string PluginsPathEnvironmentVariable = "NETRISK_PLUGINS_PATH";

    /// <summary>
    /// The host's plugins root — the directory whose subdirectories each hold one plugin.
    /// </summary>
    /// <remarks>
    /// Read on every access rather than cached, because the value it answers has to be able to
    /// change between load passes: that is what lets a test give each case its own root, and a
    /// loaded plugin assembly stays locked by this process for its lifetime, so reusing one root
    /// across cases cannot be cleaned up between them.
    /// </remarks>
    private static string PluginsRoot
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(PluginsPathEnvironmentVariable);

            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins")
                : configured;
        }
    }

    public async Task<PluginInstallResult> InstallPluginPackageAsync(Stream package, string fileName)
    {
        var fallbackName = PluginPackageInstaller.DerivePackageName(fileName);
        var packageName = fallbackName;

        if (packageName is null)
            return Failure(string.Empty,
                $"'{fileName}' does not give a usable plugin directory name. Rename the package using " +
                "letters, digits, dashes and underscores.");

        // The upload is buffered to disk before anything is inspected. ZipArchive needs a seekable
        // stream, and a request body is not one; buffering also puts a hard ceiling on the upload
        // before the archive gets a say in how big it claims to be.
        var tempFile = Path.Combine(Path.GetTempPath(), $"netrisk-plugin-{Guid.NewGuid():N}.zip");

        try
        {
            await using (var buffer = File.Create(tempFile))
            {
                var copied = await CopyBoundedAsync(package, buffer, PluginPackageInstaller.MaxPackageBytes);

                if (copied is null)
                    return Failure(packageName,
                        $"The package is larger than {PluginPackageInstaller.MaxPackageBytes / (1024 * 1024)} MB.");

                if (copied == 0)
                    return Failure(packageName, "The uploaded file is empty.");
            }

            using var archive = OpenArchive(tempFile, out var openError);

            if (archive is null)
                return Failure(packageName, openError!);

            var validation = PluginPackageInstaller.Validate(PluginPackageInstaller.Describe(archive));

            if (!validation.IsValid)
                return Failure(packageName, validation.Error!);

            // The directory is named after the plugin assembly, not the uploaded file: a package
            // named for its release (BastionVaultPlugin-1.2.1.zip) would otherwise install beside
            // the previous release rather than over it, and the loader would find both.
            packageName = PluginPackageInstaller.DeriveInstallDirectoryName(validation) ?? fallbackName!;

            var pluginsRoot = PluginsRoot;
            Directory.CreateDirectory(pluginsRoot);

            var targetDirectory = SafePathTool.CombineWithin(pluginsRoot, packageName);
            var replaced = Directory.Exists(targetDirectory);

            // Installations that predate the naming rule above already hold one directory per
            // release, so replacing the target alone would leave the older ones loading. They are
            // removed here rather than merely hidden: two copies of one plugin have two independent
            // enabled switches, and which of them a capability lookup resolves is not defined.
            var superseded = SupersededDirectories(pluginsRoot, packageName,
                PluginPackageInstaller.PluginAssemblyNames(validation.Files));

            // A replacement is staged, not overwritten in place: if extraction dies half-way the
            // installation would otherwise be left with a directory holding half of one version of
            // the plugin and half of another, which loads and misbehaves rather than failing.
            var backup = replaced
                ? Path.Combine(Path.GetTempPath(), $"netrisk-plugin-backup-{Guid.NewGuid():N}")
                : null;

            if (backup is not null) Directory.Move(targetDirectory, backup);

            List<string> written;

            try
            {
                written = PluginPackageInstaller.Extract(archive, validation, targetDirectory);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract plugin package {Package}", packageName);

                TryDelete(targetDirectory);
                if (backup is not null) Directory.Move(backup, targetDirectory);

                return Failure(packageName,
                    $"The package could not be unpacked: {ex.Message}" +
                    (backup is not null ? " The previous version was restored." : string.Empty));
            }

            if (backup is not null) TryDelete(backup);

            var removedDirectories = new List<string>();

            foreach (var directory in superseded)
            {
                TryDelete(directory);

                if (Directory.Exists(directory))
                {
                    MarkUninstalled(directory);
                    Log.Warning(
                        "Superseded plugin directory {Directory} could not be deleted; it is marked " +
                        "uninstalled and will be removed on a later start.", directory);
                }

                removedDirectories.Add(new DirectoryInfo(directory).Name);
            }

            await LoadPluginsAsync();

            // The plugins this package provides, not the ones that are new to the process. A new
            // version of an installed plugin adds no name, so a diff of the loaded set reported a
            // successful upgrade as "no new plugin was discovered" and told the operator to go and
            // read the log.
            var loaded = PluginNamesInDirectory(targetDirectory);

            Log.Information(
                "Plugin package {Package} installed into {Directory} ({Count} files); plugins now loaded: {Loaded}",
                packageName, targetDirectory, written.Count, string.Join(", ", loaded));

            return new PluginInstallResult
            {
                Success = true,
                PackageName = packageName,
                ReplacedExisting = replaced || removedDirectories.Count > 0,
                InstalledFiles = written,
                LoadedPlugins = loaded,
                RemovedDirectories = removedDirectories,
                Message = DescribeInstall(packageName, replaced, loaded, removedDirectories)
            };
        }
        catch (ArgumentException ex)
        {
            // SafePathTool refusing a segment. Reachable only if DerivePackageName and the validator
            // disagree with it, but this is the write path and it stays defended.
            Log.Error(ex, "Refused a plugin package path for {Package}", packageName);
            return Failure(packageName, "The package resolves to a path this server will not write.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected failure installing plugin package {Package}", packageName);
            return Failure(packageName, $"The package could not be installed: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempFile);
        }
    }

    /// <summary>
    /// The plugin names the loaded assemblies in <paramref name="directory"/> provide. Used after a
    /// reload to say what an installation actually produced.
    /// </summary>
    private List<string> PluginNamesInDirectory(string directory)
    {
        var names = new List<string>();

        foreach (var loaded in _pluginLoaders)
        {
            if (!SameDirectory(loaded.Directory, directory)) continue;

            foreach (var pluginType in LoadableTypes(loaded.Loader)
                         .Where(t => typeof(INetriskPlugin).IsAssignableFrom(t)))
            {
                if (Activator.CreateInstance(pluginType) is not INetriskPlugin plugin) continue;
                if (!names.Contains(plugin.PluginName)) names.Add(plugin.PluginName);
            }
        }

        return names;
    }

    private static bool SameDirectory(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What to tell the operator about a completed installation.
    /// </summary>
    private static string DescribeInstall(string packageName, bool replaced, List<string> loaded,
        List<string> removedDirectories)
    {
        if (loaded.Count == 0)
            return $"Installed {packageName}, but no plugin was discovered in it. Check the server log: " +
                   "the assembly may be refused by the signature policy or may not implement INetriskPlugin.";

        var what = string.Join(", ", loaded);

        var message = replaced || removedDirectories.Count > 0
            ? $"Updated {what} from {packageName}. Its enabled setting was kept."
            : $"Installed {what} from {packageName}. A new plugin is disabled until you switch it on.";

        if (removedDirectories.Count > 0)
            message += $" Replaced earlier installation(s): {string.Join(", ", removedDirectories)}.";

        return message;
    }

    /// <summary>
    /// The plugin directories under <paramref name="pluginsRoot"/>, other than
    /// <paramref name="targetDirectory"/>, that carry one of <paramref name="assemblyNames"/> and are
    /// therefore an older installation of the same plugin.
    /// </summary>
    private static List<string> SupersededDirectories(string pluginsRoot, string targetDirectory,
        IReadOnlyCollection<string> assemblyNames)
    {
        try
        {
            var installed = Directory.GetDirectories(pluginsRoot)
                .Select(d => (Directory: new DirectoryInfo(d).Name,
                    Assemblies: (IReadOnlyCollection<string>)Directory
                        .GetFiles(d, "*" + PluginPackageInstaller.PluginAssemblySuffix)
                        .Select(Path.GetFileName)
                        .Where(f => f is not null)
                        .Select(f => f!)
                        .ToList()))
                .ToList();

            return PluginPackageInstaller
                .FindSupersededDirectories(targetDirectory, assemblyNames, installed)
                .Select(d => Path.Combine(pluginsRoot, d))
                .ToList();
        }
        catch (Exception ex)
        {
            // Failing to enumerate the plugins root must not fail the install: the target directory
            // is still replaced correctly, and the worst case is a duplicate that was already there.
            Log.Warning("Could not look for superseded plugin directories in {Root}: {Message}",
                pluginsRoot, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Removes as much of an uninstalled plugin directory as the operating system allows, and the
    /// directory itself once nothing is left in it.
    /// </summary>
    /// <remarks>
    /// The marker is deleted last, and only together with the directory. A plain recursive delete
    /// will not do: it removes the files it can reach before it hits the locked assembly, and the
    /// marker is one of them -- so the pass after that saw a directory with a plugin assembly in it
    /// and no marker, and loaded the plugin the operator had deleted straight back.
    /// </remarks>
    private static void SweepUninstalled(string directory)
    {
        var marker = Path.Combine(directory, PluginPackageInstaller.UninstalledMarkerFile);

        foreach (var file in SafeEnumerate(directory, Directory.GetFiles))
        {
            if (string.Equals(file, marker, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Debug("Uninstalled plugin file {File} is still in use: {Message}", file, ex.Message);
            }
        }

        foreach (var child in SafeEnumerate(directory, Directory.GetDirectories))
            TryDelete(child);

        var remaining = SafeEnumerate(directory, Directory.GetFileSystemEntries)
            .Where(e => !string.Equals(e, marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count > 0) return;

        try
        {
            File.Delete(marker);
            Directory.Delete(directory);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not remove the uninstalled plugin directory {Directory}: {Message}",
                directory, ex.Message);
        }
    }

    private static string[] SafeEnumerate(string directory, Func<string, string[]> enumerate)
    {
        try
        {
            return enumerate(directory);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read {Directory}: {Message}", directory, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Marks a directory whose files could not be deleted, so the next load pass skips it and tries
    /// again. Best effort: a marker that cannot be written leaves the directory loading, which the
    /// caller reports.
    /// </summary>
    private static bool MarkUninstalled(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(directory, PluginPackageInstaller.UninstalledMarkerFile),
                $"Uninstalled at {DateTime.UtcNow:O}. NetRisk deletes this directory on a start when " +
                "nothing holds its files.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not mark {Directory} as uninstalled", directory);
            return false;
        }
    }

    public async Task<PluginUninstallResult> UninstallPluginAsync(string pluginName)
    {
        if (!IsInitialized()) await LoadPluginsAsync();

        var directories = DirectoriesProviding(pluginName);

        if (directories.Count == 0)
            return new PluginUninstallResult
            {
                Success = false,
                PluginName = pluginName,
                Message = $"Plugin {pluginName} is not installed."
            };

        // Switched off first, and before anything is deleted. A plugin whose files are still locked
        // stays loaded in this process until it restarts, so the setting is what actually stops it
        // being used in the meantime; doing it first also means a failed delete cannot leave an
        // enabled plugin the operator believes is gone.
        await SetPluginEnabledStatusAsync(pluginName, false);

        var pending = false;

        foreach (var directory in directories)
        {
            TryDelete(directory);

            if (!Directory.Exists(directory)) continue;

            if (!MarkUninstalled(directory))
                return new PluginUninstallResult
                {
                    Success = false,
                    PluginName = pluginName,
                    PackageName = new DirectoryInfo(directory).Name,
                    Message = $"Plugin {pluginName} was disabled, but {directory} could neither be " +
                              "deleted nor marked for removal. Check the server's permissions on its " +
                              "Plugins directory."
                };

            pending = true;
        }

        var packageNames = directories.Select(d => new DirectoryInfo(d).Name).ToList();

        await LoadPluginsAsync();

        Log.Information("Plugin {Name} uninstalled from {Directories} (removal pending: {Pending})",
            pluginName, string.Join(", ", packageNames), pending);

        return new PluginUninstallResult
        {
            Success = true,
            PluginName = pluginName,
            PackageName = string.Join(", ", packageNames),
            RemovalPending = pending,
            Message = pending
                ? $"Plugin {pluginName} was disabled and removed from the list. Its files are still in " +
                  "use by the server and will be deleted when it next restarts."
                : $"Plugin {pluginName} was removed."
        };
    }

    /// <summary>
    /// Every loaded directory whose assemblies provide <paramref name="pluginName"/> (plural,
    /// because a duplicate installation is exactly the state this has to be able to clean up).
    /// </summary>
    private List<string> DirectoriesProviding(string pluginName)
    {
        var directories = new List<string>();

        foreach (var loaded in _pluginLoaders)
        {
            if (string.IsNullOrEmpty(loaded.Directory)) continue;

            foreach (var pluginType in LoadableTypes(loaded.Loader)
                         .Where(t => typeof(INetriskPlugin).IsAssignableFrom(t)))
            {
                if (Activator.CreateInstance(pluginType) is not INetriskPlugin plugin) continue;
                if (!string.Equals(plugin.PluginName, pluginName, StringComparison.Ordinal)) continue;

                if (!directories.Contains(loaded.Directory, StringComparer.OrdinalIgnoreCase))
                    directories.Add(loaded.Directory);

                break;
            }
        }

        return directories;
    }

    private static PluginInstallResult Failure(string packageName, string message)
    {
        Log.Warning("Refused plugin package {Package}: {Message}", packageName, message);
        return new PluginInstallResult { Success = false, PackageName = packageName, Message = message };
    }

    /// <summary>
    /// Copies at most <paramref name="limit"/> bytes and returns null when the source has more.
    /// </summary>
    private static async Task<long?> CopyBoundedAsync(Stream source, Stream destination, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            total += read;
            if (total > limit) return null;

            await destination.WriteAsync(buffer.AsMemory(0, read));
        }

        return total;
    }

    private static ZipArchive? OpenArchive(string path, out string? error)
    {
        try
        {
            error = null;
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            error = "The uploaded file is not a valid zip archive.";
            return null;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not clean up {Directory}: {Message}", directory, ex.Message);
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not remove the temporary upload {File}: {Message}", file, ex.Message);
        }
    }

    /// <summary>
    /// The concrete, instantiable plugin types in a loaded assembly.
    ///
    /// Filtering out interfaces and abstract types matters here in a way it did not for the original
    /// callers: <c>typeof(T).IsAssignableFrom</c> is true for T itself, so without this an assembly
    /// that ships its own interface extending the capability makes Activator.CreateInstance throw.
    /// </summary>
    private static IEnumerable<Type> LoadableTypes(PluginLoader loader)
    {
        Type[] types;

        try
        {
            types = loader.LoadDefaultAssembly().GetTypes();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not enumerate the types of a loaded plugin assembly");
            return [];
        }

        return types.Where(t => t is { IsInterface: false, IsAbstract: false, IsClass: true });
    }
}