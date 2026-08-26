using System;
using System.Linq;
using System.Xml.Linq;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The macOS side is the one path that can be exercised end to end on a developer Mac, but the
/// two files that decide whether Gatekeeper accepts the result — Info.plist and the hardened
/// runtime entitlements — are worth pinning here too.
/// </summary>
public class MacBundleManifestTest
{
    private static XDocument RenderInfoPlist(string version = "2.16.3") =>
        XDocument.Parse(PackagingTemplate.Render(
            RepositoryPaths.Read("macos", "Info.plist.template"),
            PackagingTokens.MacInfoPlist(version, 2026)));

    /// <summary>Reads a plist dict as key/value pairs; values are either text or a bare true/false tag.</summary>
    private static string ValueFor(XDocument plist, string key)
    {
        var dict = plist.Root!.Element("dict")!;
        var nodes = dict.Elements().ToList();

        for (var i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i].Name == "key" && nodes[i].Value == key)
                return nodes[i + 1].Name == "true" || nodes[i + 1].Name == "false"
                    ? nodes[i + 1].Name.LocalName
                    : nodes[i + 1].Value;
        }

        throw new Xunit.Sdk.XunitException($"Info.plist has no '{key}' key.");
    }

    [Fact]
    public void TheRenderedPlistIsWellFormedXml() => Assert.NotNull(RenderInfoPlist().Root);

    [Fact]
    public void TheBundleIdentityMatchesTheOneCodesignAndPkgbuildUse()
    {
        // pkgbuild --identifier and the notarization submission both key off this value.
        var plist = RenderInfoPlist();

        Assert.Equal(PackageIdentity.MacBundleIdentifier, ValueFor(plist, "CFBundleIdentifier"));
        Assert.Equal(PackageIdentity.ProductName, ValueFor(plist, "CFBundleName"));
        Assert.Equal(PackageIdentity.ExecutableName, ValueFor(plist, "CFBundleExecutable"));
    }

    [Fact]
    public void BothVersionKeysCarryTheThreePartProductVersion()
    {
        var plist = RenderInfoPlist("2.16.3.0");

        Assert.Equal("2.16.3", ValueFor(plist, "CFBundleVersion"));
        Assert.Equal("2.16.3", ValueFor(plist, "CFBundleShortVersionString"));
    }

    [Fact]
    public void TheIconResourceNameMatchesWhatTheBuildCopiesIntoTheBundle() =>
        Assert.Equal(PackagingTokens.MacIconFileName, ValueFor(RenderInfoPlist(), "CFBundleIconFile"));

    [Fact]
    public void TheCameraPurposeStringIsPresentBecauseTheHardenedRuntimeDemandsIt()
    {
        // Without a purpose string macOS kills the process the moment FaceID touches the
        // camera, instead of prompting — a crash that only reproduces on a signed build.
        var description = ValueFor(RenderInfoPlist(), "NSCameraUsageDescription");

        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    [Fact]
    public void TheBundleDeclaresAMinimumSystemVersionAndRetinaSupport()
    {
        var plist = RenderInfoPlist();

        Assert.Equal("11.0", ValueFor(plist, "LSMinimumSystemVersion"));
        Assert.Equal("true", ValueFor(plist, "NSHighResolutionCapable"));
    }

    [Fact]
    public void TheCopyrightNamesTheYearAndTheLicence() =>
        Assert.Equal("© 2026 NetRisk. Licensed under the GNU GPL v3.",
            ValueFor(RenderInfoPlist(), "NSHumanReadableCopyright"));
}

public class MacEntitlementsTest
{
    private static XDocument Load() =>
        XDocument.Parse(RepositoryPaths.Read("macos", "entitlements.plist"));

    private static string[] Keys() =>
        Load().Root!.Element("dict")!.Elements("key").Select(k => k.Value).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheEntitlementsFileIsWellFormedXml() => Assert.NotNull(Load().Root);

    [Fact]
    public void TheClrEntitlementsAreGrantedBecauseCoreClrJitsManagedCode()
    {
        var keys = Keys();

        Assert.Contains("com.apple.security.cs.allow-jit", keys);
        Assert.Contains("com.apple.security.cs.allow-unsigned-executable-memory", keys);
    }

    [Fact]
    public void TheCameraEntitlementIsGrantedForFaceId() =>
        Assert.Contains("com.apple.security.device.camera", Keys());

    [Theory]
    // Each of these switches off a hardened-runtime protection. The build signs every nested
    // Mach-O with its own Developer ID, so none of them is needed; granting one would weaken
    // the runtime for no benefit.
    [InlineData("com.apple.security.cs.disable-library-validation")]
    [InlineData("com.apple.security.cs.disable-executable-page-protection")]
    [InlineData("com.apple.security.cs.allow-dyld-environment-variables")]
    [InlineData("com.apple.security.get-task-allow")]
    public void NoHardenedRuntimeProtectionIsDisabled(string weakening) =>
        Assert.DoesNotContain(weakening, Keys());

    [Fact]
    public void TheEntitlementSetIsExactlyWhatIsDocumented()
    {
        // A new entitlement should be a deliberate, reviewed decision, so the whole set is
        // pinned rather than only its forbidden members.
        Assert.Equal(
            new[]
            {
                "com.apple.security.cs.allow-jit",
                "com.apple.security.cs.allow-unsigned-executable-memory",
                "com.apple.security.device.camera"
            },
            Keys());
    }
}

public class PackageIdentityTest
{
    [Fact]
    public void TheHistoricalInnoSetupAppIdIsUnchanged() =>
        // Changing it orphans every existing Windows install.
        Assert.Equal("6D5567D6-4CB9-4060-9BFC-6E3113DD362B", PackageIdentity.InnoSetupAppId);

    [Fact]
    public void TheMsiUpgradeCodeIsAParsableGuidDistinctFromTheInnoAppId()
    {
        Assert.True(Guid.TryParse(PackageIdentity.MsiUpgradeCode, out _));
        Assert.NotEqual(PackageIdentity.InnoSetupAppId, PackageIdentity.MsiUpgradeCode);
    }

    [Fact]
    public void TheLinuxAppIdIsReverseDnsAsFlatpakRequires()
    {
        var parts = PackageIdentity.LinuxAppId.Split('.');

        Assert.True(parts.Length >= 3, "A Flatpak app-id needs at least three dot-separated components.");
        Assert.All(parts, part => Assert.False(string.IsNullOrWhiteSpace(part)));
    }

    [Fact]
    public void TheSnapNameIsAValidStoreName()
    {
        // Snap names are lowercase alphanumerics and hyphens only.
        Assert.Matches("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", PackageIdentity.SnapName);
    }

    [Fact]
    public void TheMsixPublisherDefaultIsADistinguishedName() =>
        Assert.StartsWith("CN=", PackageIdentity.MsixDefaultPublisher);

    [Fact]
    public void ThePublishedUrlsAreAbsolute()
    {
        Assert.True(Uri.TryCreate(PackageIdentity.PublisherUrl, UriKind.Absolute, out _));
        Assert.True(Uri.TryCreate(PackageIdentity.SupportUrl, UriKind.Absolute, out _));
        Assert.True(Uri.TryCreate(PackageIdentity.AppInstallerBaseUri, UriKind.Absolute, out _));
    }
}
