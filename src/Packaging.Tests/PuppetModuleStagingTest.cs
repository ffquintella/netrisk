using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The Puppet module reaches the container images through <c>workdir/puppet-modules</c>, and the
/// manifests baked into an image are the ones its entrypoint applies at start-up. The four Docker
/// packaging targets used to stage that tree only
/// <c>if (!Directory.Exists(BuildWorkDirectory / "puppet-modules"))</c>, so a workdir left over from
/// an earlier build kept its old copy and an edit to the module silently never shipped — including a
/// fix to a manifest that was breaking a host.
///
/// Nuke's build logic is not reachable from a test (it is the Nuke project, not
/// <c>NetRisk.Packaging</c>), so this asserts on the source. That is weaker than executing it and
/// strong enough for the one thing that went wrong: the staging must be unconditional.
/// </summary>
public class PuppetModuleStagingTest
{
    private static string[] BuildScript =>
        File.ReadAllLines(Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Build.cs"));

    private const string Copy = @"(PuppetDirectory / ""modules"").Copy(BuildWorkDirectory / ""puppet-modules"")";
    private const string Delete = @"(BuildWorkDirectory / ""puppet-modules"").DeleteDirectory()";

    [Fact]
    public void TestEveryDockerTargetRestagesThePuppetModuleUnconditionally()
    {
        var lines = BuildScript;

        var copies = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains(Copy, StringComparison.Ordinal))
            .Where(i => !lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();

        // One per Docker packaging target: API, BackgroundJobs, WebSite, ConsoleClient.
        Assert.Equal(4, copies.Count);

        Assert.All(copies, i =>
        {
            var previous = lines[i - 1];

            Assert.Contains(Delete, previous, StringComparison.Ordinal);
            Assert.DoesNotContain("//", previous, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The guard itself, named, so reintroducing it fails with the reason attached rather than as a
    /// count that no longer matches.
    /// </summary>
    [Fact]
    public void TestThePuppetStagingIsNotSkippedWhenTheWorkdirAlreadyHasACopy()
    {
        var offenders = BuildScript
            .Where(l => l.Contains("Directory.Exists(BuildWorkDirectory / \"puppet-modules\")", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }
}
