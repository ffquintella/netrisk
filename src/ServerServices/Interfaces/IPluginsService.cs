namespace ServerServices.Interfaces;

public interface IPluginsService
{
    public Task LoadPlugins();
    
    public bool PluginExists(string pluginName);
    
    public bool PluginIsEnabled(string pluginName);
    
    public bool IsInitialized();
}