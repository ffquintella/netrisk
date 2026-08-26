using System;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Every installer format narrows the product version differently and fails late when the
/// rules are broken (a truncated MSI ProductVersion silently stops upgrading, a non-zero MSIX
/// revision is rejected at submission). These tests pin the rules.
/// </summary>
public class PackageVersionsTest
{
    [Theory]
    [InlineData("2.16.3", "2.16.3")]
    [InlineData("  2.16.3  ", "2.16.3")]
    [InlineData("Releases/2.16.3", "2.16.3")]
    [InlineData("v2.16.3", "2.16.3")]
    [InlineData("2.16.3.0", "2.16.3.0")]
    [InlineData("2.16.3-beta.1", "2.16.3")]
    [InlineData("2.16.3+build77", "2.16.3")]
    [InlineData("02.016.3", "2.16.3")]
    public void NormalizeAcceptsTheFormsTheBuildPassesAround(string raw, string expected) =>
        Assert.Equal(expected, PackageVersions.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2.x.3")]
    [InlineData("2..3")]
    [InlineData("1.2.3.4.5")]
    public void NormalizeRejectsAnythingItCannotTurnIntoAVersion(string? raw) =>
        Assert.Throws<ArgumentException>(() => PackageVersions.Normalize(raw));

    [Theory]
    [InlineData("2.16.3", "2.16.3")]
    [InlineData("2.16.3.0", "2.16.3")]
    [InlineData("2.16", "2.16.0")]
    [InlineData("3", "3.0.0")]
    public void ThreePartPadsAndDropsInsignificantComponents(string raw, string expected) =>
        Assert.Equal(expected, PackageVersions.ToThreePart(raw));

    [Fact]
    public void ThreePartRefusesToDropASignificantFourthComponent()
    {
        var exception = Assert.Throws<ArgumentException>(() => PackageVersions.ToThreePart("2.16.3.4"));
        Assert.Contains("cannot be narrowed", exception.Message);
    }

    [Theory]
    [InlineData("2.16.3", "2.16.3.0")]
    [InlineData("2.16", "2.16.0.0")]
    public void MsixVersionAlwaysHasFourFieldsWithAZeroRevision(string raw, string expected) =>
        Assert.Equal(expected, PackageVersions.ToMsixVersion(raw));

    [Fact]
    public void MsixVersionRejectsAMajorOfZeroBecauseTheStoreDoes()
    {
        var exception = Assert.Throws<ArgumentException>(() => PackageVersions.ToMsixVersion("0.9.1"));
        Assert.Contains("major version of 0", exception.Message);
    }

    [Fact]
    public void MsixVersionRejectsANonZeroRevisionBecauseItIsStoreReserved()
    {
        var exception = Assert.Throws<ArgumentException>(() => PackageVersions.ToMsixVersion("2.16.3.7"));
        Assert.Contains("revision", exception.Message);
    }

    [Fact]
    public void MsixVersionRejectsAComponentAboveUInt16()
    {
        var exception = Assert.Throws<ArgumentException>(() => PackageVersions.ToMsixVersion("2.16.70000"));
        Assert.Contains("65535", exception.Message);
    }

    [Fact]
    public void MsiProductVersionKeepsThreeFields() =>
        Assert.Equal("2.16.3", PackageVersions.ToMsiProductVersion("2.16.3.0"));

    [Theory]
    // Windows Installer only compares major.minor.build and caps them at 255.255.65535; a
    // larger value is silently truncated, which breaks major upgrades.
    [InlineData("256.0.0", "major")]
    [InlineData("2.256.0", "minor")]
    [InlineData("2.16.65536", "build")]
    public void MsiProductVersionRejectsValuesWindowsInstallerWouldTruncate(string raw, string field)
    {
        var exception = Assert.Throws<ArgumentException>(() => PackageVersions.ToMsiProductVersion(raw));
        Assert.Contains(field, exception.Message);
    }

    [Fact]
    public void SnapAndMacVersionsUseThePlainThreePartForm()
    {
        Assert.Equal("2.16.3", PackageVersions.ToSnapVersion("2.16.3.0"));
        Assert.Equal("2.16.3", PackageVersions.ToMacVersion("Releases/2.16.3"));
    }
}
