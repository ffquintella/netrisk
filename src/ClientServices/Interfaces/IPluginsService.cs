using Model.Plugins;

namespace ClientServices.Interfaces;

public interface IPluginsService
{
    public Task<List<PluginInfo>> GetPluginsAsync();
    
    public Task SetPluginEnabledAsync(string pluginName, bool enabled);
    
    public Task RequestPluginsReloadAsync();

    /// <summary>
    /// Uploads a plugin package (.zip) for the server to unpack into its plugins directory.
    /// </summary>
    /// <param name="filePath">The local archive to send.</param>
    /// <returns>
    /// The server's verdict. A refused package comes back with <c>Success == false</c> and the
    /// reason in <c>Message</c> — the caller shows it rather than translating it, because the
    /// reasons are specific ("no *Plugin.dll at the top level") and a generic failure message would
    /// leave the operator with nothing to fix.
    /// </returns>
    public Task<PluginInstallResult> UploadPluginAsync(string filePath);
}