using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The 2.17.0 homolog outage, as a test.
///
/// Security finding NR-2026-025 moved the database credential into <c>/netrisk/netrisk.env</c>, and
/// the Docker entrypoints read it back with <c>. /netrisk/netrisk.env</c>. That file is a literal
/// KEY=VALUE environment file, not a shell script, and the value it carries is a connection string
/// full of <c>;</c> — a command separator. So the shell set
/// <c>Database__ConnectionString=server=10.60.65.119</c> and ran <c>port=4306</c>, <c>uid=…</c> and
/// the rest as unrelated assignments. MySqlConnector fell back to its default port 3306, the API's
/// database self test timed out after 15 s, and every netrisk host on apldc1vds0044 restarted in a
/// loop (220 <c>Connect Timeout expired</c> in 24 h, zero successful connections).
///
/// Nothing caught it: <see cref="DeploymentSecretPlacementTest"/> asserted the template emits the
/// key, never that the <em>value</em> survives being read back. This test closes that gap by
/// rendering the real template and running the real entrypoint loader over the result.
/// </summary>
public class DeploymentEnvironmentFileLoaderTest
{
    /// <summary>The path baked into the loader, and the one this test rewrites to a temporary file.</summary>
    private const string DeployedEnvFile = "/netrisk/netrisk.env";

    private static readonly string[] Entrypoints =
    [
        "entrypoint-api.sh",
        "entrypoint-backgroundjobs.sh",
        "entrypoint-console.sh",
        "entrypoint-website.sh",
    ];

    /// <summary>
    /// Every character a shell would act on if the value were ever parsed rather than read: a
    /// separator, an expansion, a substitution, both quotes, a space, a pipe and a redirect.
    /// </summary>
    private const string HostilePassword = "a;b$c`d\"e'f g|h&i>j\\k";

    private static string ConnectionString(string password) =>
        $"server=10.60.65.119;port=4306;uid=netrisk;pwd={password};database=netrisk;ConvertZeroDateTime=True";

    /// <summary>
    /// The fix. Every character of the connection string reaches the environment, including a
    /// password made entirely of shell metacharacters.
    /// </summary>
    [Fact]
    public void TestTheEntrypointLoaderPreservesTheWholeConnectionString()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var envFile = WriteRenderedEnvironmentFile(HostilePassword);

        try
        {
            var output = RunBash(
                LoaderFrom("entrypoint-api.sh", envFile),
                "load_netrisk_env",
                "printf '%s' \"$Database__ConnectionString\"");

            Assert.Equal(ConnectionString(HostilePassword), output);
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    /// <summary>
    /// The pre-fix behaviour, pinned so nobody reintroduces it as a "simplification". This is the
    /// assertion that fails on the 2.17.0 entrypoints — there, the loader under test *was* the
    /// <c>.</c> below.
    /// </summary>
    [Fact]
    public void TestSourcingTheEnvironmentFileTruncatesTheConnectionString()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        // A benign password, so the only thing the shell reacts to is the `;` separators — which is
        // exactly the production case.
        var envFile = WriteRenderedEnvironmentFile("s3cr3t");

        try
        {
            var output = RunBash(
                "set -a",
                ". '" + envFile + "'",
                "set +a",
                "printf '%s|%s' \"$Database__ConnectionString\" \"$port\"");

            // The observed symptom, exactly: the server survives, the port becomes a stray variable
            // of its own, and MySqlConnector is left to default to 3306.
            Assert.Equal("server=10.60.65.119|4306", output);
            Assert.NotEqual(ConnectionString("s3cr3t") + "|4306", output);
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    /// <summary>
    /// Four entrypoints, one loader. They are copies of each other by design, and a fix applied to
    /// one of them is worth nothing on the other three.
    /// </summary>
    [Fact]
    public void TestEveryEntrypointCarriesTheSameLoader()
    {
        var loaders = Entrypoints
            .Select(script => new { script, body = LoaderFrom(script, DeployedEnvFile) })
            .ToList();

        Assert.All(loaders, loader =>
            Assert.Equal(loaders[0].body, loader.body));
    }

    /// <summary>
    /// The loader is only correct if nothing hands the value to the shell first. Any surviving
    /// <c>.</c>/<c>source</c> of the environment file puts the outage straight back.
    /// </summary>
    [Theory]
    [InlineData("entrypoint-api.sh")]
    [InlineData("entrypoint-backgroundjobs.sh")]
    [InlineData("entrypoint-console.sh")]
    [InlineData("entrypoint-website.sh")]
    public void TestNoEntrypointShellParsesTheEnvironmentFile(string script)
    {
        var content = File.ReadAllText(EntrypointPath(script));

        Assert.DoesNotContain(". " + DeployedEnvFile, content, StringComparison.Ordinal);
        Assert.DoesNotContain("source " + DeployedEnvFile, content, StringComparison.Ordinal);

        // `set -a` existed only to turn the sourced assignments into exported variables; the loader
        // exports each one itself.
        Assert.DoesNotContain("set -a", content, StringComparison.Ordinal);
    }

    private static string EntrypointPath(string script) =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", script);

    /// <summary>
    /// Lifts <c>load_netrisk_env</c> out of a real entrypoint so the test runs the shipped code
    /// rather than a copy of it, repointing the hardcoded path at <paramref name="envFile"/>.
    /// </summary>
    private static string LoaderFrom(string script, string envFile)
    {
        var lines = File.ReadAllLines(EntrypointPath(script));

        var start = Array.FindIndex(lines, l => l.StartsWith("load_netrisk_env()", StringComparison.Ordinal));
        Assert.True(start >= 0, $"{script} no longer defines load_netrisk_env");

        var end = Array.FindIndex(lines, start, l => l == "}");
        Assert.True(end > start, $"{script}'s load_netrisk_env has no closing brace on its own line");

        var body = string.Join("\n", lines[start..(end + 1)]);

        Assert.Contains(DeployedEnvFile, body);
        return body.Replace(DeployedEnvFile, envFile, StringComparison.Ordinal);
    }

    private static string WriteRenderedEnvironmentFile(string password)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".env");
        File.WriteAllText(path, RenderEnvironmentTemplate(password));
        return path;
    }

    /// <summary>
    /// Just enough EPP for this one template: drop the parameter block, substitute
    /// <c>&lt;%= $name %&gt;</c>. The test asserts nothing template-shaped survives, so a template
    /// that grows a conditional or an iteration fails here rather than being half-rendered.
    /// </summary>
    private static string RenderEnvironmentTemplate(string password)
    {
        var values = new Dictionary<string, string>
        {
            ["db_server"] = "10.60.65.119",
            ["db_port"] = "4306",
            ["db_user"] = "netrisk",
            ["db_password"] = password,
            ["db_schema"] = "netrisk",
        };

        var template = File.ReadAllText(Path.Combine(
            RepositoryPaths.RepositoryRoot, "build", "puppet", "modules", "netrisk",
            "templates", "env", "netrisk.env.epp"));

        var body = template[(template.IndexOf("-%>", StringComparison.Ordinal) + 3)..]
            .TrimStart('\r', '\n');

        var rendered = Regex.Replace(body, @"<%=\s*\$(\w+)\s*%>", match =>
        {
            var name = match.Groups[1].Value;
            Assert.True(values.ContainsKey(name),
                $"netrisk.env.epp interpolates ${name}, which this test does not supply");
            return values[name];
        });

        Assert.DoesNotContain("<%", rendered, StringComparison.Ordinal);
        return rendered;
    }

    private static string RunBash(params string[] lines)
    {
        var script = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sh");
        File.WriteAllText(script, string.Join("\n", lines) + "\n");

        try
        {
            using var process = Process.Start(new ProcessStartInfo("/bin/bash")
            {
                ArgumentList = { script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return stdout;
        }
        finally
        {
            File.Delete(script);
        }
    }
}
