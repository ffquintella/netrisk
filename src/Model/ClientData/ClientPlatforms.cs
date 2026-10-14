namespace Model.ClientData;

/// <summary>
/// The platform names a desktop client reports to the API's update endpoints
/// (<c>/System/UpdateScript/{osFamily}</c> and <c>/System/ClientDownloadLocation/{osFamily}</c>),
/// and which of them the server still serves.
///
/// The one place both sides of that exchange read the names from, so the client, the update script
/// lookup and the installer lookup cannot disagree about what a platform is called or whether it is
/// supported.
/// </summary>
public static class ClientPlatforms
{
    public const string Windows = "windows";

    public const string Linux = "linux";

    /// <summary>
    /// The generic macOS name. It resolves to the Apple Silicon build, which since T341 is the only
    /// macOS build there is.
    /// </summary>
    public const string Mac = "mac";

    /// <summary>macOS on Apple Silicon (osx-arm64).</summary>
    public const string MacArm64 = "mac-a64";

    /// <summary>
    /// macOS on Intel (osx-x64). No longer built or served (T341), but still reported — truthfully —
    /// by every Intel client already deployed. It is named here so the server can recognise it and
    /// refuse it clearly, rather than treat it as an unknown platform or, worse, answer it with the
    /// Apple Silicon artifacts.
    /// </summary>
    public const string MacIntel = "mac-x64";

    /// <summary>The reason given to, and shown by, a client on a retired platform.</summary>
    public const string MacIntelUnsupportedMessage =
        "macOS on Intel (mac-x64) is no longer supported: NetRisk for macOS is built for Apple Silicon only.";

    /// <summary>The reason given for any other name the server does not know.</summary>
    public const string UnknownPlatformMessage = "OS Family not supported";

    private static readonly string[] Supported = [Windows, Linux, Mac, MacArm64];

    /// <summary>Lower-cased and trimmed; null reads as empty.</summary>
    public static string Normalize(string? osFamily) => (osFamily ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>True for a platform the server still has an update script and an installer for.</summary>
    public static bool IsSupported(string? osFamily) => Supported.Contains(Normalize(osFamily));

    /// <summary>True for a platform NetRisk once shipped for and no longer does.</summary>
    public static bool IsRetired(string? osFamily) => Normalize(osFamily) == MacIntel;
}
