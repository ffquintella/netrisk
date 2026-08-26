using Contracts;
using McMaster.NETCore.Plugins;
using Model.Plugins;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security;

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
        var pluginsDirs = new List<string>();
        var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        
        var dirs = Directory.GetDirectories(pluginPath);
        
        return dirs;
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
                var pluginLoader = PluginLoader.CreateFromAssemblyFile(pDll.Path, sharedTypes: new[] { typeof(INetriskPlugin), typeof(INetriskModelPlugin), typeof(INetriskFaceIDPlugin)});
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
    
    
}