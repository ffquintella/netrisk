using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NetRisk.Packaging;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Serilog;

/// <summary>
/// Milestone 7.2.2 — a CycloneDX software bill of materials beside every artifact.
///
/// The SBOM is generated from the *resolved* dependency graph at package time, which is the whole
/// point: a hand-maintained list records what somebody believed was shipping, while the resolved
/// graph records what is, transitive packages and exact versions included. A consumer can then feed
/// it to Dependency-Track and be told about a new CVE in a released version without NetRisk having to
/// re-scan anything.
///
/// The target follows the same two rules as the signing partial, for the same reasons:
/// <list type="number">
/// <item>
/// A missing tool is not a build failure. If the CycloneDX CLI is not installed the target warns
/// once, names the install command, and the artifact is still produced. Only <c>--require-sbom</c>
/// turns the gap into an error — a release pipeline sets it; a developer building locally does not.
/// </item>
/// <item>
/// The build reports the install command rather than running it. Silently installing global tooling
/// into somebody's profile is not a thing a build should do.
/// </item>
/// </list>
///
/// It is wired with <c>TriggeredBy</c> rather than by editing each <c>Package*</c> target, so adding
/// a component means adding one entry to <see cref="NetRisk.Packaging.Sbom.Components"/> and nothing else.
/// </summary>
partial class Build
{
    [Parameter("Fail the build if an SBOM cannot be generated instead of producing an artifact without one")]
    readonly bool RequireSbom;

    [Parameter("Generate an SBOM for one component only (see Sbom.Components); default is every component that was published")]
    readonly string SbomComponentName;

    /// <summary>
    /// Emits <c>netrisk-&lt;component&gt;-&lt;version&gt;.cdx.json</c> (plus its <c>.sha256</c>) into
    /// each published component's directory.
    ///
    /// Only components whose publish directory exists are processed, so a run of
    /// <c>PackageApi</c> alone produces exactly one SBOM rather than failing on the seven
    /// directories it did not create.
    /// </summary>
    Target GenerateSbom => _ => _
        .Description("Generate a CycloneDX SBOM for each published component (Track 7 milestone 7.2.2)")
        .TriggeredBy(PackageApi, PackageWebSite, PackageBackgroundJobs, PackageConsoleClient,
            PackageWindowsGUI, PackageLinuxGUI, PackageMacGUI, PackageMacA64GUI)
        .Executes(() =>
        {
            var requested = ResolveSbomComponents();

            if (requested.Count == 0)
            {
                Log.Information("No published component directories found; nothing to generate an SBOM for");
                return;
            }

            var tool = ResolveCycloneDxTool();

            if (tool == null)
            {
                var message =
                    $"The CycloneDX CLI was not found, so no SBOM was generated. Install it with: {NetRisk.Packaging.Sbom.ToolInstallCommand}";

                if (RequireSbom) throw new Exception(message + " (--require-sbom was passed)");

                Log.Warning(message);
                return;
            }

            var generated = new List<string>();

            foreach (var component in requested)
            {
                var project = Solution.GetProject(component.ProjectName);

                if (project == null)
                {
                    var message = $"Project '{component.ProjectName}' is not in the solution, so no SBOM "
                                  + $"could be generated for component '{component.Name}'";

                    if (RequireSbom) throw new Exception(message);

                    Log.Warning(message);
                    continue;
                }

                var outputDirectory = PublishDirectory / component.PublishSubdirectory;
                var fileName = Sbom.FileName(component.Name, VersionClean);

                var arguments = Sbom.BuildArguments(
                    project.Path.ToString(), outputDirectory.ToString(), fileName, VersionClean);

                try
                {
                    tool(QuoteArguments(arguments), workingDirectory: RootDirectory);
                }
                catch (Exception exception)
                {
                    var message = $"CycloneDX failed for component '{component.Name}': {exception.Message}";

                    if (RequireSbom) throw new Exception(message, exception);

                    Log.Warning(message);
                    continue;
                }

                var manifest = outputDirectory / fileName;

                if (!File.Exists(manifest))
                {
                    var message = $"CycloneDX reported success but {manifest} was not written";

                    if (RequireSbom) throw new Exception(message);

                    Log.Warning(message);
                    continue;
                }

                // The same checksum companion the artifacts carry, so an SBOM published on the
                // download page can be verified the same way the installer is.
                WriteSbomChecksum(manifest);

                generated.Add(component.Name);
                Log.Information("SBOM written: {Manifest}", manifest);
            }

            if (generated.Count > 0)
                Log.Information("Generated {Count} SBOM manifest(s): {Components}",
                    generated.Count, string.Join(", ", generated));
        });

    /// <summary>
    /// Joins the argument list into the single string the <see cref="Tool"/> delegate takes, quoting
    /// anything containing a space. Necessary because project and output paths routinely sit under a
    /// directory with a space in it, and an unquoted one silently becomes two arguments.
    /// </summary>
    private static string QuoteArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(a =>
            a.Contains(' ', StringComparison.Ordinal) ? "\"" + a + "\"" : a));

    /// <summary>
    /// The components to process: the one named by <c>--sbom-component-name</c>, or every component
    /// whose publish directory exists.
    /// </summary>
    private IReadOnlyList<SbomComponent> ResolveSbomComponents()
    {
        if (!string.IsNullOrWhiteSpace(SbomComponentName))
        {
            var named = Sbom.Find(SbomComponentName);

            if (named == null)
                throw new Exception(
                    $"Unknown SBOM component '{SbomComponentName}'. Valid names: "
                    + string.Join(", ", Sbom.Components.Select(c => c.Name)));

            return [named];
        }

        return Sbom.Components
            .Where(c => Directory.Exists(PublishDirectory / c.PublishSubdirectory))
            .ToList();
    }

    /// <summary>
    /// Resolves the CycloneDX CLI, returning null when it is not installed.
    ///
    /// <see cref="ToolResolver.GetPathTool"/> looks on PATH, which is where a global dotnet
    /// tool ends up. Wrapped in a try/catch because a resolver that throws on "not found" would make
    /// "no SBOM tool" indistinguishable from "the build is broken".
    /// </summary>
    private static Tool? ResolveCycloneDxTool()
    {
        try
        {
            return ToolResolver.GetPathTool(Sbom.ToolCommand);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteSbomChecksum(AbsolutePath manifest)
    {
        using var stream = File.OpenRead(manifest);
        var digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var checksumFile = manifest.Parent / ArtifactNames.Checksum(manifest.Name);
        File.WriteAllText(checksumFile, $"{digest}  {manifest.Name}{Environment.NewLine}");
    }
}
