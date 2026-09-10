namespace Model.Plugins;

/// <summary>
/// The outcome of installing a plugin package (a .zip) through the administration screen.
///
/// A rejected package is a <em>result</em> with <see cref="Success"/> false and a reason, not an
/// exception: every rejection here is something the operator chose (wrong archive, no plugin
/// assembly inside, a name that is not a safe directory), and an operator needs to read why.
/// </summary>
public class PluginInstallResult
{
    public bool Success { get; set; }

    /// <summary>The directory the package was installed into, under <c>Plugins/</c>.</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Why it failed, or what was installed when it succeeded.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Paths, relative to the package directory, that were written.</summary>
    public List<string> InstalledFiles { get; set; } = new();

    /// <summary>The plugin names the host discovered after reloading. Empty when the reload found none.</summary>
    public List<string> LoadedPlugins { get; set; } = new();

    /// <summary>Whether the package replaced an installation of the same name.</summary>
    public bool ReplacedExisting { get; set; }
}
