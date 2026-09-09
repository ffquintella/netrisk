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

    /// <summary>
    /// The plugin whose <c>PluginName</c> is <paramref name="pluginName"/> and which implements
    /// <typeparamref name="T"/>, or null when no plugin matches both.
    ///
    /// Differs from <see cref="GetPluginAsync{T}"/> in two ways that matter to a caller holding a
    /// stored plugin name: the name is actually matched against the instance rather than merely
    /// checked for existence, and a miss is a null rather than an exception — "the vault plugin this
    /// connection needs is not installed" is a state to report to an operator, not a fault.
    /// </summary>
    public Task<T?> GetPluginByNameAsync<T>(string pluginName) where T : INetriskPlugin;

    /// <summary>
    /// Every loaded plugin implementing <typeparamref name="T"/> that is switched on. Used to answer
    /// "which vaults can this installation talk to" for the connection editor.
    /// </summary>
    public Task<List<T>> GetEnabledPluginsAsync<T>() where T : INetriskPlugin;
}