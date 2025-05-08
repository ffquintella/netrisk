using Contracts;
using McMaster.NETCore.Plugins;
using Model.Plugins;
using Model.Services;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class PluginsService: ServiceBase, IPluginsService
{

    private List<string> _plugins = new List<string>();
    private List<string> _pluginsDirs = new List<string>();
    private List<PluginLoader> _pluginLoaders = new List<PluginLoader>();
    private bool _initialized = false;
    private ISettingsService SettingsService { get; }
    
    public PluginsService(ILogger logger, IDalService dalService, ISettingsService settingsService) : base(logger, dalService)
    {
        SettingsService = settingsService;
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
    

    public Task LoadPluginsAsync()
    {
        var pDlls = GetPluginsDlls();
        _pluginLoaders = new List<PluginLoader>();
        _pluginsDirs = new List<string>();
        _plugins = new List<string>();

        return Task.Run(() =>
        {
            foreach (var pDll in pDlls)
            {
                if (!pDll.Path.EndsWith("Plugin.dll")) continue;
                if (!File.Exists(pDll.Path)) continue;
                try
                {
                    var pluginLoader = PluginLoader.CreateFromAssemblyFile(pDll.Path, sharedTypes: new[] { typeof(INetriskPlugin), typeof(INetriskModelPlugin) });
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
        });

        
        //return Task.CompletedTask;
    }
}