using System;
using System.Collections.Generic;
using System.Linq;

namespace GUIClient;

/// <summary>Format of a configuration file the client layers into its configuration root.</summary>
public enum ClientConfigurationFormat
{
    Json,
    Ini
}

/// <summary>One configuration file, in the order it is layered.</summary>
/// <param name="FileName">File name, resolved relative to the client's base directory.</param>
/// <param name="Format">How to parse it.</param>
/// <param name="Optional">Whether start-up survives its absence.</param>
public sealed record ClientConfigurationSource(string FileName, ClientConfigurationFormat Format, bool Optional);

/// <summary>
/// The ordered configuration sources the desktop client reads at start-up. Later entries win.
///
/// The last layer is <see cref="OverlayIniFileName"/>: an optional INI file that an
/// administrator can drop next to the executable to pre-seed settings — above all the server
/// URL — without editing the shipped appsettings.json. It is what the Windows MSI writes from
/// its <c>SERVERURL</c> property, and it is equally usable by the Flatpak, Snap, .deb-style and
/// macOS deployments. The INI configuration provider maps <c>[Server] Url=…</c> to the
/// <c>Server:Url</c> key, so the file needs no NetRisk-specific parsing.
///
/// <see cref="ForEnvironment"/> adds one more optional layer on top, <c>netrisk.&lt;environment&gt;.ini</c>,
/// so a single installation started with <c>--environment=homolog</c> can be pointed at a different
/// server than the one started without it.
/// </summary>
public static class ClientConfigurationSources
{
    /// <summary>
    /// The directory every source's <see cref="ClientConfigurationSource.FileName"/> is resolved
    /// against: the one holding the client's own assemblies — where appsettings.json is published
    /// and where the installers write netrisk.ini. Never the current directory: Finder starts a
    /// macOS app bundle with it set to <c>/</c>, so a cwd-relative lookup aborts the client at
    /// start-up looking for <c>/appsettings.json</c>.
    /// </summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>Name of the optional administrator overlay file.</summary>
    public const string OverlayIniFileName = "netrisk.ini";

    private static readonly ClientConfigurationSource Overlay =
        new(OverlayIniFileName, ClientConfigurationFormat.Ini, Optional: true);

    /// <summary>Sources used by a Release build.</summary>
    public static IReadOnlyList<ClientConfigurationSource> Release { get; } = new[]
    {
        new ClientConfigurationSource("appsettings.json", ClientConfigurationFormat.Json, Optional: false),
        Overlay
    };

    /// <summary>Sources used by a Debug build, which also layers user secrets on top.</summary>
    public static IReadOnlyList<ClientConfigurationSource> Development { get; } = new[]
    {
        new ClientConfigurationSource("appsettings.development.json", ClientConfigurationFormat.Json, Optional: false),
        Overlay
    };

    /// <summary>File name of the overlay read only when the client runs as <paramref name="environment"/>.</summary>
    public static string EnvironmentOverlayIniFileName(string environment) => $"netrisk.{environment}.ini";

    /// <summary>
    /// Whether <paramref name="environment"/> may name an overlay file. It comes straight from the
    /// command line, so anything other than letters, digits, '-' and '_' — a path separator above
    /// all — gets no overlay rather than a file name outside the install directory.
    /// </summary>
    public static bool IsOverlayEnvironmentName(string? environment) =>
        !string.IsNullOrEmpty(environment) &&
        environment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// <paramref name="sources"/> plus the optional per-environment overlay as the last layer.
    /// </summary>
    public static IReadOnlyList<ClientConfigurationSource> ForEnvironment(
        IReadOnlyList<ClientConfigurationSource> sources, string environment)
    {
        if (!IsOverlayEnvironmentName(environment)) return sources;

        return sources
            .Append(new ClientConfigurationSource(
                EnvironmentOverlayIniFileName(environment), ClientConfigurationFormat.Ini, Optional: true))
            .ToArray();
    }
}
