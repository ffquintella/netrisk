using Model.Plugins;

namespace ClientServices.Interfaces;

public interface IPluginsService
{
    public Task<List<PluginInfo>> GetPluginsAsync();
}