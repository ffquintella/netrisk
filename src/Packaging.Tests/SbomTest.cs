using System;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Track 7 milestone 7.2.2 — the CycloneDX SBOM that ships beside every artifact.
///
/// The generator itself cannot run here (it is a global dotnet tool that may not be installed), so
/// what is tested is what the build *decides*: the file names published alongside the installers, the
/// component inventory, and the command line handed to the tool. A mistake in any of those produces
/// an artifact with no SBOM or an SBOM describing the wrong thing, and neither shows up until a
/// release.
/// </summary>
[TestSubject(typeof(Sbom))]
public class SbomTest
{
    [Fact]
    public void TheFileNameFollowsTheDocumentedPattern() =>
        Assert.Equal("netrisk-api-2.16.2.cdx.json", Sbom.FileName("api", "2.16.2"));

    /// <summary>
    /// Version normalisation is shared with the installers, so a four-part or tag-shaped version has
    /// to reduce the same way here as it does for the MSI.
    /// </summary>
    [Theory]
    [InlineData("2.16.2.0")]
    [InlineData("Releases/2.16.2")]
    [InlineData("v2.16.2")]
    [InlineData("2.16.2-rc.1")]
    public void TheVersionIsNormalisedTheSameWayTheInstallersNormaliseIt(string version) =>
        Assert.Equal("netrisk-website-2.16.2.cdx.json", Sbom.FileName("website", version));

    [Fact]
    public void TheChecksumCompanionMatchesTheArtifactConvention()
    {
        Assert.Equal("netrisk-api-2.16.2.cdx.json.sha256", Sbom.ChecksumFileName("api", "2.16.2"));
        Assert.Equal(ArtifactNames.Checksum(Sbom.FileName("api", "2.16.2")),
            Sbom.ChecksumFileName("api", "2.16.2"));
    }

    [Fact]
    public void AnEmptyComponentNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sbom.FileName("", "2.16.2"));
        Assert.Throws<ArgumentException>(() => Sbom.FileName("   ", "2.16.2"));
    }

    [Fact]
    public void AnEmptyVersionIsRejected() =>
        Assert.Throws<ArgumentException>(() => Sbom.FileName("api", ""));

    // ---- The component inventory ----

    /// <summary>
    /// Every component names a project that is actually in the solution. A typo here would make the
    /// build warn and carry on, shipping an artifact with no SBOM — the exact silent-gap failure this
    /// milestone is about.
    /// </summary>
    [Fact]
    public void EveryComponentNamesARealProject()
    {
        var solution = File.ReadAllText(SolutionFile());

        Assert.All(Sbom.Components, component =>
            Assert.Contains($"\"{component.ProjectName}\"", solution, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryComponentNameIsUniqueAndUrlSafe()
    {
        Assert.Equal(Sbom.Components.Count,
            Sbom.Components.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(Sbom.Components, component =>
            Assert.All(component.Name, c => Assert.True(
                char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-',
                $"'{component.Name}' is not lower-case letters, digits and hyphens")));
    }

    [Fact]
    public void EveryComponentNamesAPublishSubdirectory() =>
        Assert.All(Sbom.Components, component => Assert.False(string.IsNullOrWhiteSpace(component.PublishSubdirectory)));

    /// <summary>
    /// The set has to cover every shippable component; an artifact without an SBOM is what this
    /// milestone removes. Pinned as a count plus explicit membership so that adding a ninth artifact
    /// without an SBOM entry fails here.
    /// </summary>
    [Fact]
    public void TheInventoryCoversEveryShippedComponent()
    {
        var names = Sbom.Components.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "api", "background-jobs", "console-client", "gui-linux", "gui-mac", "gui-mac-arm64",
                "gui-windows", "website"
            },
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ComponentsAreFoundByNameCaseInsensitivelyAndUnknownNamesReturnNull()
    {
        Assert.NotNull(Sbom.Find("api"));
        Assert.NotNull(Sbom.Find("API"));
        Assert.NotNull(Sbom.Find("  gui-windows  "));
        Assert.Null(Sbom.Find("not-a-component"));
        Assert.Null(Sbom.Find(null));
    }

    // ---- The command line ----

    [Fact]
    public void TheArgumentsNameTheProjectOutputAndFile()
    {
        var arguments = Sbom.BuildArguments("/repo/src/API/API.csproj", "/out/api",
            "netrisk-api-2.16.2.cdx.json", "2.16.2");

        Assert.Equal("/repo/src/API/API.csproj", arguments[0]);
        AssertOption(arguments, "--output", "/out/api");
        AssertOption(arguments, "--filename", "netrisk-api-2.16.2.cdx.json");
        AssertOption(arguments, "--set-version", "2.16.2");
        AssertOption(arguments, "--set-name", "NetRisk");
    }

    /// <summary>
    /// JSON, not XML: the whole reason to emit an SBOM is that something else consumes it, and every
    /// tool in the ecosystem takes CycloneDX JSON.
    /// </summary>
    [Fact]
    public void TheManifestIsEmittedAsJson() =>
        Assert.Contains("--json", Sbom.BuildArguments("p.csproj", "o", "f.json", "1.0.0"));

    /// <summary>
    /// Test dependencies are not in the artifact, so listing them would invite a consumer to raise a
    /// finding against a package that does not ship.
    /// </summary>
    [Fact]
    public void TestProjectsAreExcluded() =>
        Assert.Contains("--exclude-test-projects", Sbom.BuildArguments("p.csproj", "o", "f.json", "1.0.0"));

    /// <summary>
    /// And the converse: NetRisk's projects reference each other heavily, so an SBOM that stopped at
    /// the entry project would omit most of the graph.
    /// </summary>
    [Fact]
    public void ProjectReferencesAreIncluded() =>
        Assert.Contains("--include-project-references", Sbom.BuildArguments("p.csproj", "o", "f.json", "1.0.0"));

    [Fact]
    public void TheArgumentsNormaliseTheVersion() =>
        AssertOption(Sbom.BuildArguments("p.csproj", "o", "f.json", "Releases/2.16.2.0"),
            "--set-version", "2.16.2");

    [Theory]
    [InlineData("", "o", "f.json")]
    [InlineData("p.csproj", "", "f.json")]
    [InlineData("p.csproj", "o", "")]
    public void MissingArgumentsAreRejected(string project, string output, string fileName) =>
        Assert.Throws<ArgumentException>(() => Sbom.BuildArguments(project, output, fileName, "1.0.0"));

    /// <summary>
    /// The build reports this command rather than running it, so it has to be the command that
    /// actually works — an operator will paste it verbatim.
    /// </summary>
    [Fact]
    public void TheInstallCommandIsTheGlobalToolInstall()
    {
        Assert.Equal("dotnet tool install --global CycloneDX", Sbom.ToolInstallCommand);
        Assert.Contains(Sbom.ToolCommand, Sbom.ToolInstallCommand, StringComparison.Ordinal);
    }

    private static void AssertOption(System.Collections.Generic.IReadOnlyList<string> arguments,
        string option, string expected)
    {
        var index = arguments.ToList().IndexOf(option);

        Assert.True(index >= 0, $"{option} is missing");
        Assert.True(index + 1 < arguments.Count, $"{option} has no value");
        Assert.Equal(expected, arguments[index + 1]);
    }

    /// <summary>
    /// Walks up to the repository root, the same way <c>RepositoryPaths</c> does for the installer
    /// templates, so the test does not depend on the working directory the runner chose.
    /// </summary>
    private static string SolutionFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "netrisk.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "netrisk.sln");
    }
}
