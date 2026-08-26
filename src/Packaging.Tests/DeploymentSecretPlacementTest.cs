using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Where a deployed installation keeps its database credential (security finding NR-2026-025).
///
/// The Puppet module used to render <c>Database:ConnectionString</c>, password included, into
/// <c>appsettings.json</c> on every target host. Milestone 7.3.3 forbids that, and the reason is
/// mundane rather than exotic: a configuration file is the thing that gets copied into a support
/// ticket, pasted into a chat, and read by anything that can read the install directory.
///
/// Until NR-2026-033 was fixed there was no alternative — the hosts never read environment
/// variables at all — so this test is only meaningful now that they do. It asserts on the shipped
/// templates rather than on a rendered host, because Puppet cannot be run here and a template that
/// silently regains a <c>pwd=</c> is exactly the regression nobody notices.
/// </summary>
public class DeploymentSecretPlacementTest
{
    private static string PuppetRoot =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "puppet", "modules", "netrisk");

    private static readonly string[] AppSettingsTemplates =
    [
        Path.Combine("templates", "api", "appsettings.json.epp"),
        Path.Combine("templates", "backgroundJobs", "appsettings.json.epp"),
        Path.Combine("templates", "console", "appsettings.json.epp"),
        Path.Combine("templates", "website", "appsettings.json.epp"),
    ];

    private static readonly string[] Manifests =
    [
        Path.Combine("manifests", "api.pp"),
        Path.Combine("manifests", "backgroundjobs.pp"),
        Path.Combine("manifests", "console.pp"),
        Path.Combine("manifests", "website.pp"),
    ];

    private static string Read(string relative)
    {
        var path = Path.Combine(PuppetRoot, relative);
        Assert.True(File.Exists(path), $"{relative} is missing from the Puppet module");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TestNoAppSettingsTemplateRendersTheDatabaseCredential(int index)
    {
        var content = Read(AppSettingsTemplates[index]);

        Assert.DoesNotContain("pwd=", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString\"", content, StringComparison.OrdinalIgnoreCase);

        // Not even as a declared parameter. A dead `$db_password` in the template still means the
        // credential is handed to the renderer, and it is the first thing the next person editing
        // this file will reach for.
        Assert.DoesNotContain("db_password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_server", content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The replacement exists and uses .NET's configuration separator. A single underscore would set
    /// a variable nothing reads, which fails as "cannot connect to the database" on first boot.
    /// </summary>
    [Fact]
    public void TestTheEnvironmentTemplateSetsTheConnectionStringWithTheDotNetSeparator()
    {
        var content = Read(Path.Combine("templates", "env", "netrisk.env.epp"));

        Assert.Contains("Database__ConnectionString=", content);
        Assert.DoesNotContain("Database_ConnectionString=", content.Replace("Database__", "X"));
        Assert.Contains("pwd=<%= $db_password %>", content);
    }

    /// <summary>
    /// Mode 0600 owned by the service account, and <c>show_diff => false</c> — the Puppet run report
    /// is otherwise a second plaintext copy of the credential, published to whoever reads the report.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TestEveryManifestWritesTheEnvironmentFileRestrictedAndUndiffed(int index)
    {
        var content = Read(Manifests[index]);

        Assert.Contains("/netrisk/netrisk.env", content);
        Assert.Contains("netrisk/env/netrisk.env.epp", content);

        var block = content[content.IndexOf("'/netrisk/netrisk.env'", StringComparison.Ordinal)..];
        block = block[..block.IndexOf('}')];

        Assert.Contains("'0600'", block);
        Assert.Contains("show_diff => false", block);
        Assert.Contains("owner     => $user", block);
    }

    /// <summary>
    /// The container path. An environment file nothing sources is an outage rather than a hardening
    /// step, so every entrypoint has to read it — and the three that drop privileges have to read it
    /// <em>inside</em> the <c>sudo -u netrisk</c> shell, because sudo scrubs the environment by
    /// default and a variable exported by root would not survive the switch.
    /// </summary>
    [Theory]
    [InlineData("entrypoint-api.sh", true)]
    [InlineData("entrypoint-backgroundjobs.sh", true)]
    [InlineData("entrypoint-website.sh", true)]
    // The console container runs as root and keeps itself alive for `docker exec`, so there is no
    // privilege drop to be inside of; it sources the file in its own shell instead.
    [InlineData("entrypoint-console.sh", false)]
    public void TestEveryDockerEntrypointSourcesTheEnvironmentFile(string script, bool dropsPrivileges)
    {
        var path = Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", script);

        Assert.True(File.Exists(path), $"{script} is missing");

        var lines = File.ReadAllLines(path);

        var sourcingLines = lines
            .Where(l => l.Contains(". /netrisk/netrisk.env", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(sourcingLines);

        // `set -a` around the source is what turns the assignments into exported variables; without
        // it .NET's environment provider never sees them.
        Assert.Contains("set -a", File.ReadAllText(path));

        if (dropsPrivileges)
            Assert.All(sourcingLines, line =>
                Assert.Contains("sudo -u netrisk", line, StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing anywhere in the Puppet module carries a literal password. Every value is a parameter,
    /// which is what makes the module safe to keep in the repository at all.
    /// </summary>
    [Fact]
    public void TestNoPuppetFileCarriesALiteralCredential()
    {
        var offenders = Directory
            .EnumerateFiles(PuppetRoot, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pp") || f.EndsWith(".epp"))
            .Where(f =>
            {
                var content = File.ReadAllText(f);

                // A pwd= whose value is not a template variable is a hardcoded credential.
                var index = content.IndexOf("pwd=", StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;

                var after = content[(index + 4)..];
                return !after.StartsWith("<%=") && !after.StartsWith("$");
            })
            .Select(f => Path.GetRelativePath(PuppetRoot, f))
            .ToList();

        Assert.Empty(offenders);
    }
}
