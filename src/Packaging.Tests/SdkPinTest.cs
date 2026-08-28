using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// <c>global.json</c> does not merely express a floor. Two things install the exact string in
/// <c>sdk.version</c>:
///
///  * <c>build.sh</c> / <c>build.cmd</c>, the Nuke bootstrappers, read it and pass it to
///    <c>dotnet-install</c> as <c>--version</c> on a machine with no SDK; and
///  * <c>actions/setup-dotnet</c> with <c>global-json-file</c>, which every dotnet job in
///    <c>.github/workflows/security.yml</c> uses.
///
/// So a version that is not a real, published SDK is not a soft failure — it is a 404 from the
/// download CDN and a dead job. The repository shipped <c>"10.0.0"</c>, which is a *runtime*
/// version and has never existed as an SDK, and every CI run since the workflow was added failed
/// on it. <c>rollForward</c> hides this locally, because a developer who already has an SDK never
/// runs the install path.
/// </summary>
public class SdkPinTest
{
    private static JsonElement GlobalJson() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "global.json")))
            .RootElement;

    private static string PinnedVersion() =>
        GlobalJson().GetProperty("sdk").GetProperty("version").GetString()!;

    /// <summary>
    /// .NET SDK versions are <c>major.minor.feature-band + patch</c>, and the band always starts at
    /// 100 — 10.0.100, 10.0.203, 10.0.302. A three-part version whose last part is below 100 is a
    /// runtime version that no SDK archive is published under.
    /// </summary>
    [Fact]
    public void TheSdkVersionIsAPublishedSdkVersionAndNotARuntimeVersion()
    {
        var version = PinnedVersion();
        var match = Regex.Match(version, @"^(\d+)\.(\d+)\.(\d+)(?:-[A-Za-z0-9.]+)?$");

        Assert.True(match.Success, $"global.json sdk.version '{version}' is not a version number.");

        var patch = int.Parse(match.Groups[3].Value);

        Assert.True(
            patch >= 100,
            $"global.json pins SDK '{version}'. SDK feature bands start at .100, so dotnet-install " +
            "and actions/setup-dotnet will both 404 on this and every build that has no SDK yet " +
            "will fail.");
    }

    /// <summary>
    /// The pin has to be for the major the projects target, or the bootstrapper installs an SDK
    /// that cannot build them.
    /// </summary>
    [Fact]
    public void TheSdkMajorMatchesTheTargetFramework()
    {
        var major = PinnedVersion().Split('.')[0];
        var directoryBuildProps =
            File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "src", "Directory.Build.props"));
        var solutionProject =
            File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "src", "Tools", "Tools.csproj"));

        Assert.True(
            directoryBuildProps.Contains($"net{major}.0", StringComparison.Ordinal) ||
            solutionProject.Contains($"net{major}.0", StringComparison.Ordinal),
            $"global.json pins SDK major {major}, which no project targets.");
    }

    /// <summary>
    /// The bootstrapper reads <c>sdk.version</c> by name. If it is ever renamed or nested
    /// differently, <c>build.sh</c> silently installs "latest" instead of the pin.
    /// </summary>
    [Fact]
    public void TheBootstrapperStillReadsTheVersionItIsPinnedBy()
    {
        var bootstrapper = File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "build.sh"));

        Assert.Contains("FirstJsonValue \"version\"", bootstrapper, StringComparison.Ordinal);
        Assert.Contains("--version \"$DOTNET_VERSION\"", bootstrapper, StringComparison.Ordinal);
    }
}
