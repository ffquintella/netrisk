using System.Collections.Generic;

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
/// </summary>
public static class ClientConfigurationSources
{
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
}
