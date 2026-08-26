using System;
using System.Collections.Generic;
using System.Globalization;

namespace NetRisk.Packaging;

/// <summary>
/// The exact placeholder values each installer template is rendered with. Kept here rather
/// than inline in the Nuke targets so that the tests render the real manifests with the real
/// values — a template that grows a placeholder nobody supplies fails in Packaging.Tests
/// instead of on a packaging runner.
/// </summary>
public static class PackagingTokens
{
    public static IReadOnlyDictionary<string, string> MsixManifest(string version, string? publisher) =>
        new Dictionary<string, string>
        {
            ["IdentityName"] = PackageIdentity.MsixIdentityName,
            ["Publisher"] = Publisher(publisher),
            ["Version"] = PackageVersions.ToMsixVersion(version),
            ["ProductName"] = PackageIdentity.ProductName,
            ["PublisherDisplayName"] = PackageIdentity.Publisher,
            ["Description"] = PackageIdentity.ShortDescription,
            ["ApplicationId"] = PackageIdentity.MsixApplicationId,
            ["ExecutableName"] = PackageIdentity.ExecutableName
        };

    public static IReadOnlyDictionary<string, string> AppInstaller(
        string version, string? publisher, string? baseUri) =>
        new Dictionary<string, string>
        {
            ["Version"] = PackageVersions.ToMsixVersion(version),
            ["BaseUri"] = BaseUri(baseUri),
            ["AppInstallerFileName"] = ArtifactNames.WindowsAppInstaller(),
            ["MsixFileName"] = ArtifactNames.WindowsMsix(version),
            ["IdentityName"] = PackageIdentity.MsixIdentityName,
            ["Publisher"] = Publisher(publisher)
        };

    public static IReadOnlyDictionary<string, string> AppStreamMetainfo(string version, DateTime releaseDate) =>
        new Dictionary<string, string>
        {
            ["AppId"] = PackageIdentity.LinuxAppId,
            ["ProductName"] = PackageIdentity.ProductName,
            ["Summary"] = PackageIdentity.ShortDescription,
            ["Description"] = PackageIdentity.LongDescription,
            ["PublisherDisplayName"] = PackageIdentity.Publisher,
            ["License"] = PackageIdentity.LicenseSpdx,
            ["Homepage"] = PackageIdentity.PublisherUrl,
            ["Version"] = PackageVersions.ToThreePart(version),
            ["ReleaseDate"] = releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

    public static IReadOnlyDictionary<string, string> FlatpakManifest() =>
        new Dictionary<string, string>
        {
            ["AppId"] = PackageIdentity.LinuxAppId,
            ["ExecutableName"] = PackageIdentity.ExecutableName
        };

    public static IReadOnlyDictionary<string, string> SnapcraftYaml(string version, string grade) =>
        new Dictionary<string, string>
        {
            ["SnapName"] = PackageIdentity.SnapName,
            ["AppId"] = PackageIdentity.LinuxAppId,
            ["ExecutableName"] = PackageIdentity.ExecutableName,
            ["Version"] = PackageVersions.ToSnapVersion(version),
            ["Summary"] = PackageIdentity.ShortDescription,
            ["Description"] = PackageIdentity.LongDescription,
            ["License"] = PackageIdentity.LicenseSpdx,
            ["Homepage"] = PackageIdentity.PublisherUrl,
            ["Grade"] = SnapGrades.Require(grade)
        };

    public static IReadOnlyDictionary<string, string> MacInfoPlist(string version, int copyrightYear) =>
        new Dictionary<string, string>
        {
            ["ProductName"] = PackageIdentity.ProductName,
            ["BundleIdentifier"] = PackageIdentity.MacBundleIdentifier,
            ["Version"] = PackageVersions.ToMacVersion(version),
            ["ExecutableName"] = PackageIdentity.ExecutableName,
            ["IconFileName"] = MacIconFileName,
            ["Copyright"] =
                $"© {copyrightYear.ToString(CultureInfo.InvariantCulture)} {PackageIdentity.Publisher}. " +
                "Licensed under the GNU GPL v3."
        };

    /// <summary>Name of the icon resource inside the macOS bundle.</summary>
    public const string MacIconFileName = "netrisk.icns";

    private static string Publisher(string? publisher) =>
        string.IsNullOrWhiteSpace(publisher) ? PackageIdentity.MsixDefaultPublisher : publisher.Trim();

    private static string BaseUri(string? baseUri) =>
        (string.IsNullOrWhiteSpace(baseUri) ? PackageIdentity.AppInstallerBaseUri : baseUri.Trim()).TrimEnd('/');
}

/// <summary>Snap channel grades. `devel` snaps cannot be released to the stable channel.</summary>
public static class SnapGrades
{
    public const string Stable = "stable";
    public const string Devel = "devel";

    /// <summary>Normalises a caller-supplied grade, falling back to the release-type default.</summary>
    public static string Resolve(string? requested, bool isReleaseBuild) =>
        string.IsNullOrWhiteSpace(requested)
            ? isReleaseBuild ? Stable : Devel
            : Require(requested);

    public static string Require(string? grade)
    {
        var normalized = (grade ?? string.Empty).Trim().ToLowerInvariant();

        return normalized is Stable or Devel
            ? normalized
            : throw new ArgumentException(
                $"Unknown snap grade '{grade}'. Valid values: {Stable}, {Devel}.", nameof(grade));
    }
}
