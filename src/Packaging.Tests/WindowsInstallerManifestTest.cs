using System;
using System.Linq;
using System.Xml.Linq;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The MSI and MSIX cannot be built on a non-Windows host (WiX and makeappx are Windows-only),
/// so the authoring itself is what gets verified here: it is well-formed, it carries the
/// stable identity every upgrade depends on, and it requests no capability beyond what the
/// client uses.
/// </summary>
public class WixAuthoringTest
{
    private static readonly XNamespace Wxs = "http://wixtoolset.org/schemas/v4/wxs";

    private static XDocument Load() => XDocument.Parse(RepositoryPaths.Read("windows", "msi", "NetRisk.wxs"));

    [Fact]
    public void TheAuthoringIsWellFormedXml() => Assert.NotNull(Load().Root);

    [Fact]
    public void TheUpgradeCodeIsTheDeclaredOneBecauseUpgradesDependOnIt()
    {
        // A changed UpgradeCode turns version N+1 into a side-by-side install instead of an
        // upgrade, leaving two NetRisks on the machine.
        var package = Load().Root!.Element(Wxs + "Package")!;

        Assert.Equal(PackageIdentity.MsiUpgradeCode, (string)package.Attribute("UpgradeCode")!);
    }

    [Fact]
    public void ThePackageInstallsPerMachineSoOneInstallCoversEveryUser()
    {
        var package = Load().Root!.Element(Wxs + "Package")!;

        Assert.Equal("perMachine", (string)package.Attribute("Scope")!);
        Assert.Equal(PackageIdentity.ProductName, (string)package.Attribute("Name")!);
        Assert.Equal(PackageIdentity.Publisher, (string)package.Attribute("Manufacturer")!);
    }

    [Fact]
    public void MajorUpgradesAreWiredAndDowngradesAreRefusedWithAMessage()
    {
        var majorUpgrade = Load().Descendants(Wxs + "MajorUpgrade").Single();

        Assert.Equal("yes", (string)majorUpgrade.Attribute("AllowSameVersionUpgrades")!);
        Assert.False(string.IsNullOrWhiteSpace((string?)majorUpgrade.Attribute("DowngradeErrorMessage")));
    }

    [Theory]
    // The enterprise contract: both are settable on the msiexec command line, and both must be
    // Secure so they survive a silent (/qn) install.
    [InlineData("SERVERURL")]
    [InlineData("INSTALLDESKTOPSHORTCUT")]
    public void ThePublicPropertiesAreDeclaredSecure(string property)
    {
        var declaration = Load().Descendants(Wxs + "Property")
            .Single(p => (string?)p.Attribute("Id") == property);

        Assert.Equal("yes", (string)declaration.Attribute("Secure")!);
    }

    [Fact]
    public void TheInstallDirectoryIsOverridableThroughInstallFolder() =>
        Assert.Contains(Load().Descendants(Wxs + "Directory"),
            d => (string?)d.Attribute("Id") == "INSTALLFOLDER");

    [Fact]
    public void TheServerUrlIsWrittenToTheIniOverlayTheClientReads()
    {
        // netrisk.ini is the file GUIClient layers on top of appsettings.json; the section and
        // key have to match the Server:Url configuration path.
        var ini = Load().Descendants(Wxs + "IniFile").Single();

        Assert.Equal("netrisk.ini", (string)ini.Attribute("Name")!);
        Assert.Equal("Server", (string)ini.Attribute("Section")!);
        Assert.Equal("Url", (string)ini.Attribute("Key")!);
        Assert.Equal("[SERVERURL]", (string)ini.Attribute("Value")!);
    }

    [Fact]
    public void TheIniOverlayIsRemovedOnUninstallSoNoConfigurationIsLeftBehind() =>
        Assert.Contains(Load().Descendants(Wxs + "RemoveFile"),
            r => (string?)r.Attribute("Name") == "netrisk.ini" && (string?)r.Attribute("On") == "uninstall");

    [Fact]
    public void TheShortcutsPointAtTheClientExecutableWithTheInstallFolderAsWorkingDirectory()
    {
        // The Release configuration resolves appsettings.json relative to the working
        // directory, so a shortcut without WorkingDirectory would start an unconfigured app.
        var shortcuts = Load().Descendants(Wxs + "Shortcut").ToList();

        Assert.NotEmpty(shortcuts);
        foreach (var shortcut in shortcuts)
        {
            Assert.Equal($"[INSTALLFOLDER]{PackageIdentity.ExecutableName}.exe", (string)shortcut.Attribute("Target")!);
            Assert.Equal("INSTALLFOLDER", (string)shortcut.Attribute("WorkingDirectory")!);
        }
    }

    [Fact]
    public void ThePayloadIsHarvestedFromThePublishDirectoryRatherThanListedByHand()
    {
        // Listing files by hand is how a new dependency silently stops shipping.
        var files = Load().Descendants(Wxs + "Files").Single();

        Assert.Equal("$(PublishDir)\\**", (string)files.Attribute("Include")!);
    }

    [Fact]
    public void NoWixExtensionIsRequired()
    {
        // Extensions have to be installed into the WiX cache on the runner before a build; the
        // authoring deliberately avoids them so `wix build` works with a plain tool restore.
        var document = Load();
        var namespaces = document.Root!.Attributes()
            .Where(a => a.IsNamespaceDeclaration)
            .Select(a => a.Value)
            .ToList();

        Assert.Equal(new[] { Wxs.NamespaceName }, namespaces);
    }
}

public class MsixManifestTest
{
    private static readonly XNamespace Foundation =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    private static readonly XNamespace RestrictedCapabilities =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static XDocument Render(string version = "2.16.3", string? publisher = null) =>
        XDocument.Parse(PackagingTemplate.Render(
            RepositoryPaths.Read("windows", "msix", "AppxManifest.xml.template"),
            PackagingTokens.MsixManifest(version, publisher)));

    [Fact]
    public void TheRenderedManifestIsWellFormedXml() => Assert.NotNull(Render().Root);

    [Fact]
    public void TheIdentityCarriesTheFourFieldMsixVersion()
    {
        var identity = Render("2.16.3").Root!.Element(Foundation + "Identity")!;

        Assert.Equal(PackageIdentity.MsixIdentityName, (string)identity.Attribute("Name")!);
        Assert.Equal("2.16.3.0", (string)identity.Attribute("Version")!);
        Assert.Equal("x64", (string)identity.Attribute("ProcessorArchitecture")!);
    }

    [Fact]
    public void ThePublisherDefaultsToTheDeclaredSubjectAndIsOverridable()
    {
        Assert.Equal(PackageIdentity.MsixDefaultPublisher,
            (string)Render().Root!.Element(Foundation + "Identity")!.Attribute("Publisher")!);

        Assert.Equal("CN=Acme Ltd, O=Acme Ltd, C=GB",
            (string)Render(publisher: "CN=Acme Ltd, O=Acme Ltd, C=GB").Root!
                .Element(Foundation + "Identity")!.Attribute("Publisher")!);
    }

    [Fact]
    public void TheApplicationLaunchesTheClientExecutableAsAFullTrustDesktopApp()
    {
        var application = Render().Descendants(Foundation + "Application").Single();

        Assert.Equal(PackageIdentity.MsixApplicationId, (string)application.Attribute("Id")!);
        Assert.Equal($"{PackageIdentity.ExecutableName}.exe", (string)application.Attribute("Executable")!);
        Assert.Equal("Windows.FullTrustApplication", (string)application.Attribute("EntryPoint")!);
    }

    [Fact]
    public void OnlyTheCapabilitiesTheClientNeedsAreRequested()
    {
        // runFullTrust is unavoidable for a packaged Win32 app; internetClient is the client
        // half of networking. Anything else here would be a privilege the app does not use.
        var capabilities = Render().Descendants()
            .Where(e => e.Name.LocalName == "Capability")
            .Select(e => (string)e.Attribute("Name")!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "internetClient", "runFullTrust" }, capabilities);
    }

    [Fact]
    public void TheRestrictedCapabilityUsesTheRestrictedNamespace() =>
        Assert.Contains(Render().Descendants(RestrictedCapabilities + "Capability"),
            e => (string?)e.Attribute("Name") == "runFullTrust");

    [Fact]
    public void EveryTileAssetTheManifestReferencesExistsOnDisk()
    {
        var referenced = Render().Descendants()
            .SelectMany(e => e.Attributes())
            .Select(a => a.Value)
            .Where(value => value.StartsWith("Assets\\", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.NotEmpty(referenced);

        foreach (var asset in referenced)
            Assert.True(RepositoryPaths.Exists("windows", "msix", asset.Replace('\\', '/')),
                $"MSIX asset '{asset}' is referenced by the manifest but missing from build/installers/windows/msix.");
    }

    [Fact]
    public void ARejectedVersionFailsBeforeAManifestIsWritten() =>
        Assert.Throws<ArgumentException>(() => Render("2.16.3.9"));
}

public class AppInstallerTest
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/appinstaller/2018";

    private static XDocument Render(string version = "2.16.3", string? baseUri = null) =>
        XDocument.Parse(PackagingTemplate.Render(
            RepositoryPaths.Read("windows", "msix", "NetRisk.appinstaller.template"),
            PackagingTokens.AppInstaller(version, null, baseUri)));

    [Fact]
    public void TheAppInstallerAndItsMainPackageAgreeOnIdentityAndVersion()
    {
        // Windows rejects an update whose declared identity does not match the MSIX it points at.
        var root = Render("2.16.3").Root!;
        var main = root.Element(Ns + "MainPackage")!;

        Assert.Equal("2.16.3.0", (string)root.Attribute("Version")!);
        Assert.Equal("2.16.3.0", (string)main.Attribute("Version")!);
        Assert.Equal(PackageIdentity.MsixIdentityName, (string)main.Attribute("Name")!);
        Assert.Equal(PackageIdentity.MsixDefaultPublisher, (string)main.Attribute("Publisher")!);
    }

    [Fact]
    public void BothUrisAreAbsoluteAndPointAtTheRealArtifactNames()
    {
        var root = Render("2.16.3", "https://downloads.example.com/windows/").Root!;

        var selfUri = (string)root.Attribute("Uri")!;
        var packageUri = (string)root.Element(Ns + "MainPackage")!.Attribute("Uri")!;

        Assert.Equal("https://downloads.example.com/windows/NetRisk.appinstaller", selfUri);
        Assert.Equal("https://downloads.example.com/windows/NetRisk-2.16.3-x64.msix", packageUri);
        Assert.True(Uri.TryCreate(selfUri, UriKind.Absolute, out _));
        Assert.True(Uri.TryCreate(packageUri, UriKind.Absolute, out _));
    }

    [Fact]
    public void UpdateCheckingIsConfiguredSoInstallsSelfUpdate()
    {
        var onLaunch = Render().Descendants(Ns + "OnLaunch").Single();

        Assert.True(int.Parse((string)onLaunch.Attribute("HoursBetweenUpdateChecks")!) > 0);
    }
}
