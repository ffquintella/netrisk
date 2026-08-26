using System;
using System.Collections.Generic;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

public class PackagingTemplateTest
{
    [Fact]
    public void EveryPlaceholderIsSubstituted()
    {
        var rendered = PackagingTemplate.Render(
            "<Identity Name=\"{{Name}}\" Version=\"{{Version}}\" />",
            new Dictionary<string, string> { ["Name"] = "NetRisk.Desktop", ["Version"] = "2.16.3.0" });

        Assert.Equal("<Identity Name=\"NetRisk.Desktop\" Version=\"2.16.3.0\" />", rendered);
    }

    [Fact]
    public void ARepeatedPlaceholderIsSubstitutedEveryTime()
    {
        var rendered = PackagingTemplate.Render("{{X}}-{{X}}-{{X}}",
            new Dictionary<string, string> { ["X"] = "1" });

        Assert.Equal("1-1-1", rendered);
    }

    [Fact]
    public void AnUnsuppliedPlaceholderIsAnErrorRatherThanALiteralInAShippedManifest()
    {
        // Shipping "{{Version}}" inside an AppxManifest only surfaces when a customer's
        // installer refuses to upgrade, so it has to fail during the build instead.
        var exception = Assert.Throws<TemplateRenderException>(() =>
            PackagingTemplate.Render("Version=\"{{Version}}\" Publisher=\"{{Publisher}}\"",
                new Dictionary<string, string> { ["Version"] = "2.16.3.0" }));

        Assert.Contains("{{Publisher}}", exception.Message);
        Assert.DoesNotContain("{{Version}}", exception.Message);
    }

    [Fact]
    public void TokensAreReportedInFirstSeenOrderWithoutDuplicates()
    {
        var tokens = PackagingTemplate.Tokens("{{B}} {{A}} {{B}} {{C}}");

        Assert.Equal(new[] { "B", "A", "C" }, tokens);
    }

    [Fact]
    public void TextWithoutPlaceholdersPassesThroughUnchanged() =>
        Assert.Equal("no tokens here",
            PackagingTemplate.Render("no tokens here", new Dictionary<string, string>()));

    [Fact]
    public void AnEmptyTemplateRendersEmpty() =>
        Assert.Equal(string.Empty, PackagingTemplate.Render(null, new Dictionary<string, string>()));

    [Fact]
    public void ValuesAreNotRescannedForPlaceholders()
    {
        // A value that happens to contain {{...}} must not send the renderer round again.
        var rendered = PackagingTemplate.Render("{{A}}",
            new Dictionary<string, string> { ["A"] = "{{B}}" });

        Assert.Equal("{{B}}", rendered);
    }
}

public class ArtifactNamesTest
{
    [Fact]
    public void TheWindowsSetupNameMatchesTheHistoricalInnoSetupOutput() =>
        Assert.Equal("NetRisk-Setup-2.16.3.exe", ArtifactNames.WindowsSetupExe("2.16.3"));

    [Fact]
    public void TheMacDmgNameMatchesTheHistoricalOutput() =>
        Assert.Equal("GUIClient-Mac-a64-2.16.3.dmg", ArtifactNames.MacDmg("a64", "2.16.3"));

    [Fact]
    public void ArtifactNamesAreDerivedFromTheProductVersion()
    {
        Assert.Equal("NetRisk-2.16.3-x64.msi", ArtifactNames.WindowsMsi("2.16.3"));
        Assert.Equal("NetRisk-2.16.3-x64.msix", ArtifactNames.WindowsMsix("2.16.3.0"));
        Assert.Equal("app.netrisk.NetRisk-2.16.3.flatpak", ArtifactNames.LinuxFlatpak("2.16.3"));
        Assert.Equal("netrisk_2.16.3_amd64.snap", ArtifactNames.LinuxSnap("2.16.3"));
    }

    [Fact]
    public void TheMsiAndMsixChecksumFilesDoNotCollide()
    {
        // Both artifacts share a base name; replacing the extension instead of appending
        // would have them overwrite each other's checksum.
        var msi = ArtifactNames.Checksum(ArtifactNames.WindowsMsi("2.16.3"));
        var msix = ArtifactNames.Checksum(ArtifactNames.WindowsMsix("2.16.3"));

        Assert.NotEqual(msi, msix);
        Assert.Equal("NetRisk-2.16.3-x64.msi.sha256", msi);
    }

    [Fact]
    public void AnEmptyArchitectureIsRejected() =>
        Assert.Throws<ArgumentException>(() => ArtifactNames.MacDmg("  ", "2.16.3"));

    [Fact]
    public void AnInvalidVersionIsRejectedRatherThanNamingAFileAfterIt() =>
        Assert.Throws<ArgumentException>(() => ArtifactNames.WindowsMsi("not-a-version"));
}
