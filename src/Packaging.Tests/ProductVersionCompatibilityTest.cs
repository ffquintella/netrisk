using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The product version lives in src/Directory.Build.props and is bumped by `./build.sh
/// BumpPatch`. Each installer format constrains it differently, and every one of those
/// constraints fails late — an MSI whose ProductVersion Windows truncates simply stops
/// upgrading. This test moves the failure to the bump.
/// </summary>
public class ProductVersionCompatibilityTest
{
    private static string CurrentVersion()
    {
        var props = Path.Combine(RepositoryPaths.RepositoryRoot, "src", "Directory.Build.props");
        var document = XDocument.Load(props);

        var version = document.Descendants("Version").FirstOrDefault()?.Value
                      ?? document.Descendants("AssemblyVersion").FirstOrDefault()?.Value;

        Assert.False(string.IsNullOrWhiteSpace(version),
            $"No <Version> found in {props}; the packaging targets read the product version from there.");

        return version!;
    }

    [Fact]
    public void TheDeclaredVersionIsParsable() =>
        Assert.False(string.IsNullOrWhiteSpace(PackageVersions.Normalize(CurrentVersion())));

    [Fact]
    public void TheDeclaredVersionFitsEveryInstallerFormat()
    {
        var version = CurrentVersion();

        // Any of these throwing means the next release cannot be packaged.
        Assert.Equal(3, PackageVersions.ToMsiProductVersion(version).Split('.').Length);
        Assert.Equal(4, PackageVersions.ToMsixVersion(version).Split('.').Length);
        Assert.Equal(3, PackageVersions.ToSnapVersion(version).Split('.').Length);
        Assert.Equal(3, PackageVersions.ToMacVersion(version).Split('.').Length);
    }

    [Fact]
    public void EveryArtifactNameCanBeDerivedFromTheDeclaredVersion()
    {
        var version = CurrentVersion();

        Assert.EndsWith(".exe", ArtifactNames.WindowsSetupExe(version));
        Assert.EndsWith(".msi", ArtifactNames.WindowsMsi(version));
        Assert.EndsWith(".msix", ArtifactNames.WindowsMsix(version));
        Assert.EndsWith(".dmg", ArtifactNames.MacDmg("a64", version));
        Assert.EndsWith(".pkg", ArtifactNames.MacPkg("x64", version));
        Assert.EndsWith(".flatpak", ArtifactNames.LinuxFlatpak(version));
        Assert.EndsWith(".snap", ArtifactNames.LinuxSnap(version));
    }

    [Fact]
    public void TheVersionPropsAndTheAssemblyVersionAgree()
    {
        var props = Path.Combine(RepositoryPaths.RepositoryRoot, "src", "Directory.Build.props");
        var document = XDocument.Load(props);

        var values = new[] { "Version", "AssemblyVersion", "FileVersion" }
            .Select(name => document.Descendants(name).FirstOrDefault()?.Value)
            .Where(value => value is not null)
            .Select(value => PackageVersions.Normalize(value))
            .Distinct()
            .ToList();

        Assert.Single(values);
    }
}
