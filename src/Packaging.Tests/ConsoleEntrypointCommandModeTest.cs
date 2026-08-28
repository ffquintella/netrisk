using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// What the console container does with the command it is given, if any.
///
/// <c>entrypoint-console.sh</c> used to run <c>start_console_keepalive</c> — a bare
/// <c>tail -f /dev/null</c> — and then <c>exec "$@"</c>. The keepalive never returns, so the
/// <c>exec</c> was unreachable, and the script did not say which of the two it meant.
///
/// The deployment settles it. <c>Dockerfile-ConsoleClient</c> declares an <c>ENTRYPOINT</c> and no
/// <c>CMD</c>, and the generated host launcher on apldc1vds0044
/// (<c>/usr/local/bin/docker-run-netrisk-dsv_console-start.sh</c>) ends its <c>docker create</c> at
/// the image name with no command after it — so <c>"$@"</c> is empty in production and the container
/// is there to be driven with <c>docker exec … netrisk-console &lt;command&gt;</c>, as
/// docs/product-guides/installation.md instructs. (The dev script,
/// <c>build/Docker/DevDockerRun-ConsoleClient.sh</c>, replaces the entrypoint outright with
/// <c>--entrypoint /bin/bash</c>, so it exercises none of this.)
///
/// The keepalive is therefore the deployed path and is pinned as such below. The argument path is
/// pinned too, because leaving unreachable code that looks like it runs a command is how the next
/// reader concludes <c>docker run &lt;image&gt; netrisk-console database init</c> works — it hung
/// forever instead.
///
/// These tests run the real <c>_main</c> lifted out of the shipped script, with only its leaf
/// actions stubbed, rather than re-describing it.
/// </summary>
public class ConsoleEntrypointCommandModeTest
{
    private static string EntrypointPath =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", "entrypoint-console.sh");

    private static string DockerfilePath =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", "Dockerfile-ConsoleClient");

    /// <summary>
    /// The deployed path: no command, so the container stays up on the keepalive.
    /// </summary>
    [Fact]
    public void TestTheEntrypointKeepsTheContainerAliveWhenGivenNoCommand()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        Assert.Equal("KEEPALIVE", RunEntrypoint());
    }

    /// <summary>
    /// The path that used to be dead code: a command is given, so it runs — and the keepalive that
    /// used to swallow it does not.
    /// </summary>
    [Fact]
    public void TestTheEntrypointRunsTheCommandItIsGiven()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var output = RunEntrypoint("/bin/echo", "-n", "database init");

        Assert.Equal("database init", output);
        Assert.DoesNotContain("KEEPALIVE", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Arguments reach the command whole. <c>exec "$@"</c> unquoted would split
    /// <c>database init</c> into two, which is the kind of thing that only shows up in an operator's
    /// terminal at 2am.
    /// </summary>
    [Fact]
    public void TestTheEntrypointPassesArgumentsThroughUnsplit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the entrypoints are bash scripts");

        var output = RunEntrypoint("/bin/echo", "-n", "a b", "c;d", "e$f");

        Assert.Equal("a b c;d e$f", output);
    }

    /// <summary>
    /// The reason the keepalive branch is the deployed one: the image supplies no command of its
    /// own. A <c>CMD</c> added here would silently move production onto the exec branch.
    /// </summary>
    [Fact]
    public void TestTheConsoleImageDeclaresAnEntrypointAndNoCommand()
    {
        var directives = File.ReadAllLines(DockerfilePath)
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith('#'))
            .ToList();

        Assert.Contains(directives, line => line.StartsWith("ENTRYPOINT", StringComparison.Ordinal));
        Assert.DoesNotContain(directives, line => line.StartsWith("CMD", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the shipped <c>_main</c>. Everything it calls that needs a container — the Puppet run,
    /// the 0600 environment file, the blocking <c>tail</c> — is replaced by a stub redefined after
    /// the real definitions and before the call, so the control flow under test is the real one.
    /// </summary>
    private static string RunEntrypoint(params string[] arguments)
    {
        var body = File.ReadAllLines(EntrypointPath);

        var invocation = Array.FindIndex(body, l => l.Trim() == "_main \"$@\"");
        Assert.True(invocation >= 0, "entrypoint-console.sh no longer ends by invoking _main \"$@\"");

        var script = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sh");
        File.WriteAllText(script, string.Join("\n",
        [
            .. body[..invocation],
            "set_config(){ :; }",
            "config_netrisk(){ :; }",
            "load_netrisk_env(){ :; }",
            "start_console_keepalive(){ printf 'KEEPALIVE'; }",
            "_main \"$@\"",
        ]) + "\n");

        try
        {
            var startInfo = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            startInfo.ArgumentList.Add(script);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)!;

            // Read on background tasks: a regression that reinstates the unconditional keepalive
            // never closes stdout, and a synchronous ReadToEnd would hang the whole test run rather
            // than failing this one test.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("the entrypoint never exited \u2014 it is blocking where it should have run the command");
            }

            Assert.True(process.ExitCode == 0,
                $"the entrypoint exited {process.ExitCode}: {stderr.GetAwaiter().GetResult()}");

            return stdout.GetAwaiter().GetResult();
        }
        finally
        {
            File.Delete(script);
        }
    }
}
