using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using NetRisk.Packaging;
using Xunit;
using YamlDotNet.Serialization;

namespace Packaging.Tests;

/// <summary>
/// Flatpak and Snap can only be built on Linux, so what is verified here is the recipe: it
/// parses, it declares the identity the stores registered, and its sandbox grants are the
/// enumerated least-privilege set rather than a blanket "allow everything".
/// </summary>
public class FlatpakManifestTest
{
    private static Dictionary<object, object> Render()
    {
        var yaml = PackagingTemplate.Render(
            RepositoryPaths.Read("linux", "flatpak", $"{PackageIdentity.LinuxAppId}.yml.template"),
            PackagingTokens.FlatpakManifest());

        return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
    }

    private static List<string> FinishArgs(Dictionary<object, object> manifest) =>
        ((List<object>)manifest["finish-args"]).Select(a => (string)a).ToList();

    [Fact]
    public void TheManifestIsValidYaml() => Assert.NotEmpty(Render());

    [Fact]
    public void TheAppIdMatchesTheDeclaredReverseDnsIdentity() =>
        Assert.Equal(PackageIdentity.LinuxAppId, Render()["app-id"]);

    [Fact]
    public void ItBuildsOnTheFreedesktopRuntimeWithAPinnedVersion()
    {
        var manifest = Render();

        Assert.Equal("org.freedesktop.Platform", manifest["runtime"]);
        Assert.Equal("org.freedesktop.Sdk", manifest["sdk"]);
        Assert.False(string.IsNullOrWhiteSpace((string)manifest["runtime-version"]));
    }

    [Fact]
    public void ItLaunchesThroughTheSharedLauncherScript() =>
        Assert.Equal("netrisk", Render()["command"]);

    [Theory]
    // The deliberate least-privilege set: rendering, GPU, and network for the REST client.
    [InlineData("--socket=wayland")]
    [InlineData("--socket=fallback-x11")]
    [InlineData("--share=ipc")]
    [InlineData("--device=dri")]
    [InlineData("--share=network")]
    public void TheSandboxGrantsWhatADesktopClientActuallyNeeds(string expected) =>
        Assert.Contains(expected, FinishArgs(Render()));

    [Theory]
    // Each of these would hand the sandbox back its whole reason for existing. --device=all in
    // particular is what a camera-capable build is tempted to add; the FaceID feature is left
    // opt-in per machine instead.
    [InlineData("--device=all")]
    [InlineData("--filesystem=home")]
    [InlineData("--filesystem=host")]
    [InlineData("--socket=session-bus")]
    [InlineData("--socket=system-bus")]
    [InlineData("--share=all")]
    public void TheSandboxDoesNotGrantBlanketAccess(string forbidden) =>
        Assert.DoesNotContain(forbidden, FinishArgs(Render()));

    [Fact]
    public void FileDialogsRelyOnThePortalRatherThanADirectHomeGrant()
    {
        // Avalonia uses the XDG desktop portal inside a sandbox, so no filesystem grant beyond
        // a predictable download location is needed.
        var filesystemGrants = FinishArgs(Render())
            .Where(arg => arg.StartsWith("--filesystem=", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(new[] { "--filesystem=xdg-download" }, filesystemGrants);
    }

    [Fact]
    public void ThePayloadIsStagedRatherThanCompiledInsideTheSandbox()
    {
        var module = ((List<object>)Render()["modules"]).Cast<Dictionary<object, object>>().Single();

        Assert.Equal("simple", module["buildsystem"]);
        Assert.Equal("netrisk", module["name"]);

        var commands = ((List<object>)module["build-commands"]).Select(c => (string)c).ToList();
        Assert.Contains(commands, c => c.Contains("cp -a publish/.", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Contains($"chmod +x /app/lib/netrisk/{PackageIdentity.ExecutableName}", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryFileTheBuildCommandsInstallIsStagedByTheBuild()
    {
        // These names have to match what Build.Installers.cs stages, or flatpak-builder fails
        // on a Linux runner with a missing-file error nobody can reproduce locally.
        var module = ((List<object>)Render()["modules"]).Cast<Dictionary<object, object>>().Single();
        var commands = string.Join("\n", ((List<object>)module["build-commands"]).Select(c => (string)c));

        foreach (var staged in new[]
                 {
                     "netrisk.sh",
                     $"{PackageIdentity.LinuxAppId}.desktop",
                     $"{PackageIdentity.LinuxAppId}.metainfo.xml",
                     "icon-256.png",
                     "icon-512.png"
                 })
        {
            Assert.Contains(staged, commands, StringComparison.Ordinal);
        }
    }
}

public class SnapcraftRecipeTest
{
    private static Dictionary<object, object> Render(string version = "2.16.3", string grade = "stable")
    {
        var yaml = PackagingTemplate.Render(
            RepositoryPaths.Read("linux", "snap", "snapcraft.yaml.template"),
            PackagingTokens.SnapcraftYaml(version, grade));

        return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml);
    }

    [Fact]
    public void TheRecipeIsValidYaml() => Assert.NotEmpty(Render());

    [Fact]
    public void ItIsStrictlyConfinedOnASupportedBase()
    {
        var recipe = Render();

        Assert.Equal("strict", recipe["confinement"]);
        Assert.Equal("core24", recipe["base"]);
        Assert.Equal(PackageIdentity.SnapName, recipe["name"]);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("devel")]
    public void TheGradeIsCarriedThroughForTheChannelStrategy(string grade) =>
        Assert.Equal(grade, Render(grade: grade)["grade"]);

    [Fact]
    public void AnUnknownGradeIsRejectedBeforeARecipeIsWritten() =>
        Assert.Throws<ArgumentException>(() => Render(grade: "experimental"));

    [Fact]
    public void TheVersionIsTheThreePartProductVersion() =>
        Assert.Equal("2.16.3", Render("2.16.3.0")["version"]);

    [Fact]
    public void TheAppUsesTheGnomeExtensionAndTheSharedLauncher()
    {
        var app = ((Dictionary<object, object>)Render()["apps"])
            .Values.Cast<Dictionary<object, object>>().Single();

        Assert.Equal("bin/netrisk", app["command"]);
        Assert.Contains("gnome", ((List<object>)app["extensions"]).Select(e => (string)e));
        Assert.Equal(PackageIdentity.LinuxAppId, app["common-id"]);
    }

    [Fact]
    public void TheInterfacesAreTheEnumeratedDesktopClientSet()
    {
        var app = ((Dictionary<object, object>)Render()["apps"])
            .Values.Cast<Dictionary<object, object>>().Single();
        var plugs = ((List<object>)app["plugs"]).Select(p => (string)p).ToList();

        foreach (var expected in new[] { "desktop", "wayland", "x11", "opengl", "network", "camera" })
            Assert.Contains(expected, plugs);

        // A client never listens, so it must not ask to bind a port.
        Assert.DoesNotContain("network-bind", plugs);
        // Neither of these belongs in a strictly-confined desktop client.
        Assert.DoesNotContain("system-files", plugs);
        Assert.DoesNotContain("classic-support", plugs);
    }

    [Fact]
    public void TheGlobalizationDataIsKeptSoDatesAreLocalised()
    {
        var app = ((Dictionary<object, object>)Render()["apps"])
            .Values.Cast<Dictionary<object, object>>().Single();
        var environment = (Dictionary<object, object>)app["environment"];

        Assert.Equal("0", environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"]);
    }

    [Fact]
    public void ThePayloadIsDumpedFromTheStagedDirectory()
    {
        var part = ((Dictionary<object, object>)Render()["parts"])
            .Values.Cast<Dictionary<object, object>>().Single();

        Assert.Equal("dump", part["plugin"]);
        Assert.Equal("payload", part["source"]);

        var organized = (Dictionary<object, object>)part["organize"];
        Assert.Equal("lib/netrisk", organized["publish"]);
        Assert.Equal("bin/netrisk", organized["netrisk.sh"]);
    }
}

public class LinuxDesktopIntegrationTest
{
    [Fact]
    public void TheDesktopEntryLaunchesTheSharedLauncherAndUsesTheAppIdIcon()
    {
        var entry = RepositoryPaths.Read("linux", "shared", $"{PackageIdentity.LinuxAppId}.desktop")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains('='))
            .ToDictionary(line => line.Split('=', 2)[0], line => line.Split('=', 2)[1]);

        Assert.Equal("Application", entry["Type"]);
        Assert.Equal(PackageIdentity.ProductName, entry["Name"]);
        Assert.Equal("netrisk", entry["Exec"]);
        // The icon is installed as <app-id>.png into the hicolor theme by both recipes.
        Assert.Equal(PackageIdentity.LinuxAppId, entry["Icon"]);
        Assert.Equal("false", entry["Terminal"]);
    }

    [Fact]
    public void TheLauncherExecutesTheClientBinaryTheRecipesChmod()
    {
        // The recipes chmod +x <ExecutableName>; a launcher naming something else would ship a
        // package that cannot start.
        var launcher = RepositoryPaths.Read("linux", "shared", "netrisk.sh");

        Assert.Contains($"exec \"./{PackageIdentity.ExecutableName}\"", launcher);
    }

    [Fact]
    public void TheLauncherEntersTheInstallDirectoryBecauseConfigurationIsResolvedFromIt()
    {
        // GUIClient reads appsettings.json (and the netrisk.ini overlay) relative to the
        // working directory.
        var launcher = RepositoryPaths.Read("linux", "shared", "netrisk.sh");

        Assert.Contains("cd \"$INSTALL_DIR\"", launcher);
        Assert.Contains("NETRISK_INSTALL_DIR", launcher);
    }

    [Fact]
    public void TheAppStreamMetadataIsWellFormedAndCarriesTheReleaseBeingBuilt()
    {
        var rendered = PackagingTemplate.Render(
            RepositoryPaths.Read("linux", "shared", $"{PackageIdentity.LinuxAppId}.metainfo.xml.template"),
            PackagingTokens.AppStreamMetainfo("2.16.3", new DateTime(2026, 8, 26)));

        var document = XDocument.Parse(rendered);
        var component = document.Root!;

        Assert.Equal("desktop-application", (string)component.Attribute("type")!);
        Assert.Equal(PackageIdentity.LinuxAppId, component.Element("id")!.Value);
        Assert.Equal(PackageIdentity.LicenseSpdx, component.Element("project_license")!.Value);
        Assert.Equal($"{PackageIdentity.LinuxAppId}.desktop", component.Element("launchable")!.Value);

        var release = component.Element("releases")!.Elements("release").Single();
        Assert.Equal("2.16.3", (string)release.Attribute("version")!);
        Assert.Equal("2026-08-26", (string)release.Attribute("date")!);
    }

    [Fact]
    public void TheMetadataDeclaresAContentRatingBecauseFlathubRequiresOne() =>
        Assert.Contains("content_rating",
            RepositoryPaths.Read("linux", "shared", $"{PackageIdentity.LinuxAppId}.metainfo.xml.template"));
}
