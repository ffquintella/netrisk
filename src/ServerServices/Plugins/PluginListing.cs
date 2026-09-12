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

    /// <summary>
    /// One candidate per plugin name — the highest version — keeping the order the names were first
    /// seen in.
    /// </summary>
    /// <remarks>
    /// <para>This is what decides which copy of a duplicated plugin actually <em>serves</em> a
    /// request, and it is a different question from which copy is <em>listed</em>
    /// (<see cref="CollapseVersions"/>). Before it existed the answer was "whichever directory the
    /// filesystem enumerated first", which on a host that had BastionVaultPlugin 1.2.0 and 1.2.1
    /// side by side meant the older copy resolved every credential — while the administration screen
    /// showed 1.2.1 enabled. The operator-visible symptom was a vault test that reported a denied
    /// token without the vault's own explanation, because quoting that explanation is precisely what
    /// 1.2.1 added.</para>
    ///
    /// <para>Names are compared ordinally, like everywhere else the plugin name is a key: it is the
    /// same string that indexes <c>Plugin_&lt;name&gt;_Enabled</c> and that a vault connection
    /// stores.</para>
    /// </remarks>
    public static List<TCandidate> PreferNewestPerName<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, string> name,
        Func<TCandidate, string> version)
    {
        var best = new Dictionary<string, TCandidate>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var candidate in candidates)
        {
            var key = name(candidate);

            if (!best.TryGetValue(key, out var incumbent))
            {
                best[key] = candidate;
                order.Add(key);
                continue;
            }

            // Strictly greater: a tie keeps the incumbent, so the result does not depend on
            // enumeration order when two directories hold the same version.
            if (PluginVersionOrder.Compare(version(candidate), version(incumbent)) > 0)
                best[key] = candidate;
        }

        return order.Select(k => best[k]).ToList();
    }
}
