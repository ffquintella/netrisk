using Model.Plugins;

namespace ClientServices.Interfaces;

public interface IPluginsService
{
    public Task<List<PluginInfo>> GetPluginsAsync();
    
    public Task SetPluginEnabledAsync(string pluginName, bool enabled);
    
    public Task RequestPluginsReloadAsync();
}