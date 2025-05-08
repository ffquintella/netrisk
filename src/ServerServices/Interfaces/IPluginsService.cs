namespace ServerServices.Interfaces;

public interface IPluginsService
{
    public Task LoadPluginsAsync();
    
    public bool PluginExists(string pluginName);
    
    public Task<bool> PluginIsEnabledAsync(string pluginName);
    
    public bool IsInitialized();
}