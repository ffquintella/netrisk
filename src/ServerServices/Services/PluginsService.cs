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
    
    private List<string> GetPlugins()
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


    public Task LoadPlugins()
    {
        _plugins = GetPlugins();
        _initialized = true;
        
        return Task.CompletedTask;
    }
}