using Model.Services;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class PluginsService: ServiceBase, IPluginsService
{

    private List<string> _plugins;
    private bool _initialized;
    
    public PluginsService(ILogger logger, IDalService dalService) : base(logger, dalService)
    {
        //_plugins = GetPlugins();
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

        return false;
    }

    public bool PluginIsEnabled(string pluginName)
    {
        if(!IsInitialized()) return false;

        return false;
    }

    public bool IsInitialized()
    {
        return _initialized;
    }
    

    public Task LoadPlugins()
    {
        var pDlls = GetPluginsDlls();
        
        
        _initialized = true;
        
        return Task.CompletedTask;
    }
}