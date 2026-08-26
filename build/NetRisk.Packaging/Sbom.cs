using System;
using System.Collections.Generic;
using System.Linq;

namespace NetRisk.Packaging;

/// <summary>
/// One shippable component and where its SBOM comes from.
/// </summary>
/// <param name="Name">
/// The component name that appears in the SBOM file name. Lower-case and hyphenated, because it ends
/// up in a URL on the download page.
/// </param>
/// <param name="ProjectName">The solution project CycloneDX walks to resolve dependencies.</param>
/// <param name="PublishSubdirectory">
/// The folder under <c>output/publish</c> the matching <c>Package*</c> target writes to. The SBOM is
/// written beside the artifact so that "the artifact and its SBOM" is a single directory to copy.
/// </param>
public sealed record SbomComponent(string Name, string ProjectName, string PublishSubdirectory);

/// <summary>
/// The naming and command-line rules for the CycloneDX software bill of materials that ships with
/// every artifact (Track 7 milestone 7.2.2).
///
/// Pure logic, kept out of the Nuke target for the same reason as the rest of
/// <c>NetRisk.Packaging</c>: an SBOM cannot be generated on a machine without the CycloneDX tool, so
/// the part that can be tested has to be the part that decides *what* to run.
///
/// Generated at build time from the resolved dependency graph rather than written by hand — that is
/// the whole value of an SBOM. A hand-maintained list records what somebody believed was shipping;
/// the resolved graph records what actually is, transitive packages and exact versions included.
/// </summary>
public static class Sbom
{
    /// <summary>
    /// The CycloneDX CLI, installed as a global dotnet tool. Reported rather than installed: the
    /// signing targets already take the position that a build must not silently install tooling into
    /// the developer's profile, and an SBOM generator is no different.
    /// </summary>
    public const string ToolCommand = "CycloneDX";

    /// <summary>The exact command an operator runs when the tool is missing.</summary>
    public const string ToolInstallCommand = "dotnet tool install --global CycloneDX";

    /// <summary>The CycloneDX specification version the manifests are emitted against.</summary>
    public const string SpecificationVersion = "1.6";

    /// <summary>
    /// Every component that ships an SBOM. Deliberately the same set as the <c>Package*</c> targets:
    /// an artifact without an SBOM is the case this milestone exists to remove.
    /// </summary>
    public static IReadOnlyList<SbomComponent> Components { get; } =
    [
        new("api", "API", "api"),
        new("website", "WebSite", "WebSite"),
        new("background-jobs", "BackgroundJobs", "backgroundjobs"),
        new("console-client", "ConsoleClient", "consoleClient"),
        new("gui-linux", "GUIClient", "GUIClient-Linux"),
        new("gui-windows", "GUIClient", "GUIClient-Windows-x64-Releases"),
        new("gui-mac", "GUIClient", "GUIClient-Mac"),
        new("gui-mac-arm64", "GUIClient", "GUIClient-MacA64")
    ];

    /// <summary>
    /// The SBOM file name for a component at a version, e.g.
    /// <c>netrisk-api-2.16.2.cdx.json</c>.
    /// </summary>
    public static string FileName(string component, string version)
    {
        if (string.IsNullOrWhiteSpace(component))
            throw new ArgumentException("The component name must not be empty.", nameof(component));

        return $"netrisk-{component.Trim()}-{PackageVersions.ToThreePart(version)}.cdx.json";
    }

    /// <summary>The companion checksum name, matching the convention the artifacts already use.</summary>
    public static string ChecksumFileName(string component, string version) =>
        ArtifactNames.Checksum(FileName(component, version));

    /// <summary>
    /// Looks up a component by name. Returns null rather than throwing so a caller can report an
    /// unknown name as a usage error with the valid list attached.
    /// </summary>
    public static SbomComponent? Find(string? component) =>
        Components.FirstOrDefault(c => string.Equals(c.Name, component?.Trim(),
            StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The CycloneDX CLI arguments for one component.
    /// </summary>
    /// <param name="projectPath">Absolute path to the <c>.csproj</c>.</param>
    /// <param name="outputDirectory">Directory the manifest is written to.</param>
    /// <param name="fileName">The manifest file name, from <see cref="FileName"/>.</param>
    /// <param name="version">The product version stamped into the manifest metadata.</param>
    /// <remarks>
    /// <c>--exclude-test-projects</c> is set because a test project's dependencies are not part of
    /// what ships, and an SBOM that lists xunit invites a consumer to raise a finding against a
    /// package that is not in the artifact. <c>--include-project-references</c> is set for the
    /// opposite reason: NetRisk's own projects reference each other heavily, and an SBOM that stopped
    /// at the entry project would omit most of the graph.
    /// </remarks>
    public static IReadOnlyList<string> BuildArguments(
        string projectPath, string outputDirectory, string fileName, string version)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("The project path must not be empty.", nameof(projectPath));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The output directory must not be empty.", nameof(outputDirectory));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("The file name must not be empty.", nameof(fileName));

        return
        [
            projectPath,
            "--output", outputDirectory,
            "--filename", fileName,
            "--json",
            "--set-name", "NetRisk",
            "--set-version", PackageVersions.ToThreePart(version),
            "--set-type", "application",
            "--exclude-test-projects",
            "--include-project-references"
        ];
    }
}
