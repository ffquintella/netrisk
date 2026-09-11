using Model.Plugins;
using Serilog;

namespace ServerServices.Plugins;

/// <summary>
/// How the installed plugins are presented to the administration screen.
/// </summary>
public static class PluginListing
{
    /// <summary>
    /// One row per plugin name, keeping the highest version.
    /// </summary>
    /// <remarks>
    /// <para>A safety net rather than the fix: a duplicate installation is now removed when the
    /// plugin is installed (see <see cref="PluginPackageInstaller.DeriveInstallDirectoryName"/> and
    /// <see cref="PluginPackageInstaller.FindSupersededDirectories"/>). It stays because a plugin
    /// directory can also arrive by hand -- scp, an image layer, a half-finished upgrade -- and one
    /// plugin listed twice with two independent enabled switches, as BastionVaultPlugin 1.2.0 and
    /// 1.2.1 were, is worse than listing the newer of the two.</para>
    ///
    /// <para>Grouping is by <c>PluginName</c> and ordinal, because that name is the key everything
    /// else uses: the <c>Plugin_&lt;name&gt;_Enabled</c> setting, the capability lookups, and the
    /// plugin name a vault connection stores. Two rows sharing it are two rows sharing one switch,
    /// whatever directories they came from.</para>
    /// </remarks>
    public static List<PluginInfo> CollapseVersions(List<PluginInfo> plugins)
    {
        var collapsed = new List<PluginInfo>();

        foreach (var group in plugins.GroupBy(p => p.Name, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderByDescending(p => p.Version, Comparer<string>.Create(PluginVersionOrder.Compare))
                .ToList();

            if (ordered.Count > 1)
                Log.Warning(
                    "Plugin {Name} is installed {Count} times ({Directories}); listing {Version} from " +
                    "{Directory}. Reinstalling or deleting the plugin clears the duplicates.",
                    group.Key, ordered.Count, string.Join(", ", ordered.Select(p => p.PackageName)),
                    ordered[0].Version, ordered[0].PackageName);

            collapsed.Add(ordered[0]);
        }

        return collapsed;
    }
}
