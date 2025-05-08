using Contracts;
using McMaster.NETCore.Plugins;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class PluginsService: ServiceBase, IPluginsService
{

    private List<string> _plugins;
    private List<PluginLoader> _pluginLoaders;
    private bool _initialized;
    private ISettingsService SettingsService { get; }
    
    public PluginsService(ILogger logger, IDalService dalService, ISettingsService settingsService) : base(logger, dalService)
    {
        SettingsService = settingsService;
    }
    
    private List<string> GetPluginsDlls()
    {
        var plugins = new List<string>();
        var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (Directory.Exists(pluginPath))
        {
            plugins.AddRange(Directory.GetFiles(pluginPath, "*.dll"));
        }
        else
        {
            Log.Information("Plugins directory doesn't exist ... creating one");
            Directory.CreateDirectory(pluginPath);
        }
        return plugins;
    }

    public bool PluginExists(string pluginName)
    {
        if(!IsInitialized()) return false;
        
        if (_plugins.Contains(pluginName))
        {
            return true;
        }

        return false;
    }

    public async Task<bool> PluginIsEnabledAsync(string pluginName)
    {
        if(!IsInitialized()) return false;
        
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
    

    public Task LoadPluginsAsync()
    {
        var pDlls = GetPluginsDlls();

        return Task.Run(() =>
        {
            foreach (var pDll in pDlls)
            {
                if (!pDll.EndsWith(".dll")) continue;
                if (!File.Exists(pDll)) continue;
                try
                {
                    var pluginLoader = PluginLoader.CreateFromAssemblyFile(pDll, sharedTypes: new[] { typeof(INetriskPlugin) });
                    _pluginLoaders.Add(pluginLoader);
                
                    if (pluginLoader.LoadDefaultAssembly()
                            .CreateInstance("Plugin") is INetriskPlugin plugin)
                    {
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
        });

        
        //return Task.CompletedTask;
    }
}