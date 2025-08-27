using Contracts;
using Model.Plugins;
using Model.Services;

namespace ServerServices.Interfaces;

public interface IPluginsService
{
    
    /// <summary>
    /// Loads the plugins from the disk.
    /// </summary>
    /// <returns></returns>
    public Task LoadPluginsAsync();
    
    /// <summary>
    /// Checks if the plugin exists.
    /// </summary>
    /// <param name="pluginName"></param>
    /// <returns></returns>
    public Task<bool> PluginExistsAsync(string pluginName);
    
    /// <summary>
    /// Checks if the plugin is enabled.
    /// </summary>
    /// <param name="pluginName"></param>
    /// <returns></returns>
    public Task<bool> PluginIsEnabledAsync(string pluginName);
    
    /// <summary>
    /// Checks if the service is initialized.
    /// </summary>
    /// <returns></returns>
    public bool IsInitialized();
    
    /// <summary>
    /// Returns the information about the service.
    /// </summary>
    /// <returns></returns>
    public Task<ServiceInformation> GetInfoAsync();
    
    /// <summary>
    /// Sets the plugin enabled status.
    /// </summary>
    /// <param name="pluginName"></param>
    /// <param name="enabled"></param>
    /// <returns></returns>
    public Task SetPluginEnabledStatusAsync(string pluginName, bool enabled);
    
    /// <summary>
    /// Lists the plugins and gets the information about them.
    /// </summary>
    /// <returns></returns>
    public Task<List<PluginInfo>> GetPluginsAsync();
    
    /// <summary>
    /// Gets the plugin by name.
    /// </summary>
    /// <param name="pluginName"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public Task<T> GetPluginAsync<T>(string pluginName) where T : INetriskPlugin;
}