namespace NetRisk.Packaging;

/// <summary>
/// The single declaration of every installer-visible identifier. Each packaging target reads
/// these constants instead of restating them, so an identifier can never drift between the
/// Inno Setup script, the MSI, the MSIX manifest, the macOS bundle and the Linux packages.
///
/// The GUID/identity values below are part of the product's public identity: changing one
/// turns an upgrade into a side-by-side install (Windows) or orphans the previous package
/// (MSIX/Flatpak/Snap). Treat them as append-only.
/// </summary>
public static class PackageIdentity
{
    /// <summary>Product name shown by every installer UI.</summary>
    public const string ProductName = "NetRisk";

    /// <summary>Name of the GUI client executable inside the published output.</summary>
    public const string ExecutableName = "GUIClient";

    /// <summary>Publisher / manufacturer display name.</summary>
    public const string Publisher = "NetRisk";

    public const string PublisherUrl = "https://www.netrisk.app/";

    public const string SupportUrl = "https://www.netrisk.app/";

    public const string ShortDescription = "Risk, vulnerability and incident management";

    public const string LongDescription =
        "NetRisk is a cross-platform risk, vulnerability and incident management client. " +
        "It connects to a NetRisk server to manage risks, vulnerabilities, incidents, " +
        "assessments and reports.";

    /// <summary>
    /// Inno Setup AppId of the historical Windows installer. Kept verbatim: it is how the
    /// existing installed base is recognised, so it must never change.
    /// </summary>
    public const string InnoSetupAppId = "6D5567D6-4CB9-4060-9BFC-6E3113DD362B";

    /// <summary>
    /// MSI UpgradeCode. Every NetRisk MSI ever published must carry this value, otherwise
    /// version N+1 installs alongside N instead of replacing it.
    /// </summary>
    public const string MsiUpgradeCode = "3F0B22CE-5E1B-4C2E-9A54-08C67F0D9F41";

    /// <summary>MSIX package identity Name. Must match the Store registration once published.</summary>
    public const string MsixIdentityName = "NetRisk.NetRiskDesktop";

    /// <summary>
    /// Default MSIX Publisher. This must be the *exact* subject of the signing certificate,
    /// so real releases override it with --msix-publisher.
    /// </summary>
    public const string MsixDefaultPublisher = "CN=NetRisk";

    /// <summary>MSIX Application Id (manifest-local, not a GUID).</summary>
    public const string MsixApplicationId = "NetRiskDesktop";

    /// <summary>macOS bundle identifier — also the pkgbuild/codesign identifier.</summary>
    public const string MacBundleIdentifier = "com.netrisk.client";

    /// <summary>
    /// Reverse-DNS application id used by Flatpak, the AppStream metainfo file and the
    /// freedesktop .desktop entry. netrisk.app -> app.netrisk.NetRisk.
    /// </summary>
    public const string LinuxAppId = "app.netrisk.NetRisk";

    /// <summary>Snap Store package name (lowercase, no dots).</summary>
    public const string SnapName = "netrisk";

    /// <summary>Base URI where the .appinstaller and its MSIX are published for auto-update.</summary>
    public const string AppInstallerBaseUri = "https://www.netrisk.app/downloads/windows";

    /// <summary>SPDX license identifier, used by AppStream metadata.</summary>
    public const string LicenseSpdx = "GPL-3.0-only";
}
