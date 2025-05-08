namespace ServerServices.Interfaces;

public interface IPluginsService
{
    public Task LoadPluginsAsync();
    
    public Task<bool> PluginExistsAsync(string pluginName);
    
    public Task<bool> PluginIsEnabledAsync(string pluginName);
    
    public bool IsInitialized();
}