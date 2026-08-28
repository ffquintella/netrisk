using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The container images provision themselves with the <b>OpenVox</b> agent, not Perforce's
/// <c>puppet-agent</c>. The base image (<c>ffquintella/docker-puppet</c>) installs
/// <c>openvox-agent</c> from <c>yum.voxpupuli.org</c> on Rocky Linux 9.
///
/// OpenVox is a drop-in fork: it keeps the <c>puppet</c> command, <c>/opt/puppetlabs/bin/puppet</c>
/// and the whole <c>/opt/puppetlabs</c> layout, so the entrypoints' <c>puppet apply</c> and every
/// manifest under <c>build/puppet</c> are unaffected — and so an image that silently went back to
/// <c>puppet-agent</c> would look exactly the same from this repository. That is what the
/// <c>rpm -q</c> guard in each Dockerfile is for, and what this test keeps in place: the packaging
/// runners are Linux, this test runs anywhere, and neither the guard nor the base tag is something a
/// reader can verify by inspection.
/// </summary>
public class ConfigurationManagementAgentTest
{
    private static readonly string[] Dockerfiles =
    [
        "Dockerfile-API",
        "Dockerfile-BackgroundJobs",
        "Dockerfile-ConsoleClient",
        "Dockerfile-WebSite",
    ];

    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        foreach (var file in Dockerfiles) data.Add(file);
        return data;
    }

    private static string Read(string dockerfile)
    {
        var path = Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", dockerfile);
        Assert.True(File.Exists(path), $"{dockerfile} is missing");
        return File.ReadAllText(path);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void TestEveryImageAssertsTheOpenVoxAgentIsInstalled(string dockerfile)
    {
        var content = Read(dockerfile);

        Assert.Contains("rpm -q openvox-agent", content, StringComparison.Ordinal);

        // And that the Perforce package is not merely absent from the Dockerfile but rejected by it.
        Assert.Contains("rpm -q puppet-agent", content, StringComparison.Ordinal);
        Assert.Contains("exit 1", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing installs a configuration management agent inside these images — the base image owns
    /// that, and a second install would be the way puppet-agent came back in.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public void TestNoImageInstallsAnAgentOfItsOwn(string dockerfile)
    {
        var offenders = Read(dockerfile)
            .Split('\n')
            .Where(l => l.Contains("dnf ", StringComparison.Ordinal) ||
                        l.Contains("yum ", StringComparison.Ordinal))
            .Where(l => l.Contains("puppet", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("openvox", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// OpenVox ships the agent at the puppet paths, so the entrypoints keep calling
    /// <c>/opt/puppetlabs/bin/puppet apply</c>. Renaming that to something OpenVox-flavoured would
    /// break every image — there is no <c>openvox</c> binary in the package.
    /// </summary>
    [Theory]
    [InlineData("entrypoint-api.sh")]
    [InlineData("entrypoint-backgroundjobs.sh")]
    [InlineData("entrypoint-console.sh")]
    [InlineData("entrypoint-website.sh")]
    public void TestEveryEntrypointAppliesItsCatalogThroughThePuppetLabsPath(string script)
    {
        var content = File.ReadAllText(
            Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", script));

        Assert.Contains("/opt/puppetlabs/bin/puppet apply", content, StringComparison.Ordinal);
    }
}
