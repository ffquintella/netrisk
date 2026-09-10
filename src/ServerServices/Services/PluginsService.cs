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
    private List<PluginLoader> _pluginLoaders = new List<PluginLoader>();
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
        var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

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
        _pluginLoaders = new List<PluginLoader>();
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
                _pluginLoaders.Add(pluginLoader);

                var pluginTypes = pluginLoader.LoadDefaultAssembly()
                    .GetTypes()
                    .Where(t => typeof(INetriskPlugin).IsAssignableFrom(t));

                foreach (var pluginType in pluginTypes)
                {
                    var plugin = (INetriskPlugin)Activator.CreateInstance(pluginType)! as INetriskPlugin;
                
                    _plugins.Add(plugin.PluginName);
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

        foreach (var pluginLoader in _pluginLoaders)
        {
            var pluginTypes = pluginLoader.LoadDefaultAssembly()
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
                    IsEnabled = await PluginIsEnabledAsync(netriskPlugin.PluginName)
                };
                
                pluginInfos.Add(pluginInfo);
                
            }  
        }

        return pluginInfos;
    }

    public async Task<T> GetPluginAsync<T>(string pluginName) where T: INetriskPlugin
    {
        if(!IsInitialized()) await LoadPluginsAsync();
        
        if(!await PluginExistsAsync(pluginName)) throw new Exception($"Plugin {pluginName} not found");

        //if (typeof(T).Name != pluginName) throw new Exception($"Plugin Name must match the return type not found");

        foreach (var pluginLoader in _pluginLoaders)
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

        foreach (var pluginLoader in _pluginLoaders)
        {
            foreach (var pluginType in LoadableTypes(pluginLoader).Where(t => typeof(T).IsAssignableFrom(t)))
            {
                if (Activator.CreateInstance(pluginType) is not T candidate) continue;

                // The name is matched, not assumed. GetPluginAsync<T> checks that *some* plugin with
                // the requested name exists and then returns the first instance assignable to T from
                // any loader, which on an installation with two plugins of the same capability
                // silently returns the wrong one. A vault connection names the plugin that services
                // it precisely so that cannot happen, so this overload has to honour it.
                if (string.Equals(candidate.PluginName, pluginName, StringComparison.Ordinal))
                    return candidate;
            }
        }

        return default;
    }

    public async Task<List<T>> GetEnabledPluginsAsync<T>() where T : INetriskPlugin
    {
        if (!IsInitialized()) await LoadPluginsAsync();

        var found = new List<T>();

        foreach (var pluginLoader in _pluginLoaders)
        {
            foreach (var pluginType in LoadableTypes(pluginLoader).Where(t => typeof(T).IsAssignableFrom(t)))
            {
                if (Activator.CreateInstance(pluginType) is not T candidate) continue;
                if (!await PluginIsEnabledAsync(candidate.PluginName)) continue;

                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>
    /// The host's plugins root — the directory whose subdirectories each hold one plugin.
    /// </summary>
    private static string PluginsRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

    public async Task<PluginInstallResult> InstallPluginPackageAsync(Stream package, string fileName)
    {
        var packageName = PluginPackageInstaller.DerivePackageName(fileName);

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

            var pluginsRoot = PluginsRoot;
            Directory.CreateDirectory(pluginsRoot);

            var targetDirectory = SafePathTool.CombineWithin(pluginsRoot, packageName);
            var replaced = Directory.Exists(targetDirectory);

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

            var before = _plugins.ToHashSet(StringComparer.Ordinal);
            await LoadPluginsAsync();
            var loaded = _plugins.Where(p => !before.Contains(p)).ToList();

            Log.Information(
                "Plugin package {Package} installed into {Directory} ({Count} files); plugins now loaded: {Loaded}",
                packageName, targetDirectory, written.Count, string.Join(", ", loaded));

            return new PluginInstallResult
            {
                Success = true,
                PackageName = packageName,
                ReplacedExisting = replaced,
                InstalledFiles = written,
                LoadedPlugins = loaded,
                Message = loaded.Count > 0
                    ? $"Installed {packageName}. New plugins are disabled until you switch them on."
                    : $"Installed {packageName}, but no new plugin was discovered. Check the server log — " +
                      "the assembly may be refused by the signature policy or may not implement INetriskPlugin."
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