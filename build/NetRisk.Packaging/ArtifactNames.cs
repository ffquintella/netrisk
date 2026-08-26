using System;

namespace NetRisk.Packaging;

/// <summary>
/// The file names of every published artifact, in one place. Download pages, checksum files
/// and the release notes all reference these, so they are derived rather than retyped.
/// </summary>
public static class ArtifactNames
{
    public static string WindowsSetupExe(string version) => $"NetRisk-Setup-{Three(version)}.exe";

    public static string WindowsMsi(string version) => $"NetRisk-{Three(version)}-x64.msi";

    public static string WindowsMsix(string version) => $"NetRisk-{Three(version)}-x64.msix";

    public static string WindowsAppInstaller() => "NetRisk.appinstaller";

    public static string MacDmg(string architecture, string version) =>
        $"GUIClient-Mac-{Require(architecture, nameof(architecture))}-{Three(version)}.dmg";

    public static string MacPkg(string architecture, string version) =>
        $"GUIClient-Mac-{Require(architecture, nameof(architecture))}-{Three(version)}.pkg";

    public static string LinuxFlatpak(string version) => $"{PackageIdentity.LinuxAppId}-{Three(version)}.flatpak";

    public static string LinuxSnap(string version) => $"{PackageIdentity.SnapName}_{Three(version)}_amd64.snap";

    /// <summary>Companion checksum file for any artifact.</summary>
    public static string Checksum(string artifactFileName) =>
        Require(artifactFileName, nameof(artifactFileName)) + ".sha256";

    private static string Three(string version) => PackageVersions.ToThreePart(version);

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} must not be empty.", parameterName)
            : value.Trim();
}
