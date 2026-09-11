namespace Model.Plugins;

/// <summary>
/// The outcome of removing an installed plugin (administration → plugins → delete).
///
/// A refusal is a <em>result</em> with <see cref="Success"/> false and a reason, for the same reason
/// <see cref="PluginInstallResult"/> is: every failure an operator can act on — the plugin is not
/// installed, its directory is not writable — is something to read, not an exception to translate.
/// </summary>
public class PluginUninstallResult
{
    public bool Success { get; set; }

    /// <summary>The plugin that was removed, as it was named in the list.</summary>
    public string PluginName { get; set; } = string.Empty;

    /// <summary>The directory under <c>Plugins/</c> that held it, when one was found.</summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the files are still on disk and will be removed on the next start.
    ///
    /// A loaded plugin assembly is locked by the host process on Windows, and nothing in .NET can
    /// reliably unload it while instances may still be referenced. When the directory cannot be
    /// deleted the plugin is switched off and marked, which takes it out of the list immediately;
    /// the files go on the next restart. <see cref="Success"/> is still true — the plugin is gone
    /// as far as the product is concerned — and the message says so.
    /// </summary>
    public bool RemovalPending { get; set; }

    /// <summary>Why it failed, or what happened when it succeeded.</summary>
    public string Message { get; set; } = string.Empty;
}
