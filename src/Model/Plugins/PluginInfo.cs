namespace Model.Plugins;

public class PluginInfo
{
    public string Name { get; set; } = String.Empty;
    public string Description { get; set; } = String.Empty;
    public string Version { get; set; } = String.Empty;
    
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// The directory under the host's <c>Plugins/</c> that this plugin was loaded from.
    ///
    /// Carried to the client because it is the only thing that distinguishes two installations of
    /// the same plugin, and because removing one is removing a directory rather than a name.
    /// </summary>
    public string PackageName { get; set; } = String.Empty;

}