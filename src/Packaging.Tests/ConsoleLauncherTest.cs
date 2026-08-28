using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The <c>netrisk-console</c> wrapper the deployed console container installs on PATH.
///
/// The bug it exists for: the console container holds itself open with <c>tail -f /dev/null</c>, so
/// every operator command arrives as
/// <c>docker exec netrisk-&lt;env&gt;_console netrisk-console &lt;command&gt;</c>. A <c>docker exec</c>
/// builds its environment from the image configuration, inheriting nothing that
/// <c>entrypoint-console.sh</c> exported into PID 1 — and the database credential lives *only* in
/// that export, because security finding NR-2026-025 took it out of <c>appsettings.json</c> and the
/// Puppet-rendered <c>/netrisk/appsettings.json</c> now carries a comment where the connection string
/// used to be.
///
/// So <c>Configuration["Database:ConnectionString"]</c> resolved to null, MySqlConnector defaulted to
/// <c>localhost:3306</c>, and every <c>netrisk-console database …</c> command on a deployed host
/// failed with <c>Unable to connect to any of the specified MySQL hosts</c> — pointing at a database
/// server nobody had configured rather than at the setting that was absent. The database runs in its
/// own container, so nothing answers on localhost and the connect is refused immediately, which also
/// made it look nothing like the connect-timeout signature of a genuinely unreachable host.
///
/// These tests run the shipped script rather than a copy, with only the two absolute paths rewritten.
/// </summary>
public class ConsoleLauncherTest
{
    private const string LauncherScript = "netrisk-console.sh";

    /// <summary>The install path in the image, and the name operators type.</summary>
    private const string InstalledPath = "/usr/local/bin/netrisk-console";

    private static string LauncherPath =>
        Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Docker", LauncherScript);

    private static string Launcher => File.ReadAllText(LauncherPath);

    private const string ConnectionString =
        "server=10.60.65.119;port=4306;uid=netrisk;pwd=a;b$c`d\"e'f g|h&i>j;database=netrisk";

    [Fact]
    public void TestTheLauncherExists()
    {
        Assert.True(File.Exists(LauncherPath), $"build/Docker/{LauncherScript} is missing");
        Assert.StartsWith("#!/bin/bash", Launcher, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point: the wrapper puts the credential in the environment of the process it execs,
    /// with every character intact — including a password made of shell metacharacters, since the
    /// value is read from a file the shell must never parse.
    /// </summary>
    [Fact]
    public void TestTheLauncherExportsTheConnectionStringToTheProcessItRuns()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (stdout, _, exitCode) = RunLauncher(ConnectionString, "database", "status");

        Assert.Equal(0, exitCode);
        Assert.Contains("connection-string=" + ConnectionString, stdout);
    }

    /// <summary>
    /// Arguments have to survive the wrapper untouched, or `database update` becomes a different
    /// command than the operator typed.
    /// </summary>
    [Fact]
    public void TestTheLauncherForwardsItsArgumentsUnchanged()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (stdout, _, _) = RunLauncher(ConnectionString, "database", "upgrade-schema", "--phase", "6b");

        Assert.Contains("args=[database|upgrade-schema|--phase|6b]", stdout);
    }

    /// <summary>
    /// <c>Host.CreateDefaultBuilder</c> resolves <c>appsettings.json</c> against the working
    /// directory and the console registers it with <c>optional: false</c>, so the binary has to run
    /// from <c>/netrisk</c>. Running it from anywhere else is a start-up crash, not a fallback.
    /// </summary>
    [Fact]
    public void TestTheLauncherRunsTheBinaryFromTheInstallDirectory()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (stdout, _, _) = RunLauncher(ConnectionString, "database", "status");

        Assert.Contains("cwd-has-appsettings=yes", stdout);
    }

    /// <summary>
    /// A missing credential has to say so. Silence is what turned the original failure into a hunt
    /// for an unreachable database server, and the wrapper is the last place that can still name the
    /// variable before the .NET side reduces it to "cannot connect".
    /// </summary>
    [Fact]
    public void TestTheLauncherWarnsWhenTheCredentialIsAbsent()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (stdout, stderr, exitCode) = RunLauncher(connectionString: null, "database", "status");

        // Still runs — plenty of subcommands need no database, and the wrapper does not decide which.
        Assert.Equal(0, exitCode);
        Assert.Contains("Database__ConnectionString", stderr);
        Assert.Contains("connection-string=", stdout);
    }

    /// <summary>
    /// A missing <c>netrisk.env</c> must not take the command down with <c>set -e</c>: the loader
    /// returns 0 when the file is unreadable, and the wrapper has to keep going and warn.
    /// </summary>
    [Fact]
    public void TestTheLauncherSurvivesAMissingEnvironmentFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (_, stderr, exitCode) = RunLauncher(connectionString: null, writeEnvFile: false, "database", "status");

        Assert.Equal(0, exitCode);
        Assert.Contains("Database__ConnectionString", stderr);
    }

    /// <summary>
    /// The exit code is the command's, not the wrapper's. An operator scripting against this — or
    /// Puppet, or a CI job — needs a failed upgrade to fail.
    /// </summary>
    [Fact]
    public void TestTheLauncherPropagatesTheExitCode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the launcher is a bash script");

        var (_, _, exitCode) = RunLauncher(ConnectionString, "fail");

        Assert.Equal(23, exitCode);
    }

    /// <summary>
    /// <c>exec</c> rather than a plain call, so the wrapper does not sit in the process tree holding
    /// a signal handler between <c>docker exec</c> and the command it ran.
    /// </summary>
    [Fact]
    public void TestTheLauncherExecsRatherThanForks()
    {
        Assert.Matches(new Regex(@"^exec \./ConsoleClient ""\$@""$", RegexOptions.Multiline), Launcher);
    }

    /// <summary>
    /// The image has to install it as the name operators type, executable, with CRLF stripped for the
    /// same reason the entrypoint does — a CR after the shebang makes the interpreter unfindable.
    /// </summary>
    [Fact]
    public void TestTheDockerfileInstallsTheLauncherOnPath()
    {
        var dockerfile = File.ReadAllText(Path.Combine(
            RepositoryPaths.RepositoryRoot, "build", "Docker", "Dockerfile-ConsoleClient"));

        Assert.Contains($"COPY {LauncherScript} {InstalledPath}", dockerfile);
        Assert.Contains($"chmod 755 {InstalledPath}", dockerfile);
        Assert.Contains($"sed -i 's/\\r$//' {InstalledPath}", dockerfile);
    }

    /// <summary>
    /// And the Nuke target has to stage it next to the Dockerfile, or the <c>COPY</c> above fails the
    /// image build.
    /// </summary>
    [Fact]
    public void TestTheBuildStagesTheLauncherForTheImage()
    {
        var build = File.ReadAllText(Path.Combine(RepositoryPaths.RepositoryRoot, "build", "Build.cs"));

        Assert.Contains($"\"{LauncherScript}\"", build);
        Assert.Contains($"BuildWorkDirectory / \"{LauncherScript}\"", build);
    }

    /// <summary>
    /// Runs the real wrapper against a temporary <c>/netrisk</c>: a rendered environment file and a
    /// stand-in <c>ConsoleClient</c> that reports what it received. Only the two absolute paths are
    /// rewritten — the loader, the guard and the exec are the shipped lines.
    /// </summary>
    private static (string Stdout, string Stderr, int ExitCode) RunLauncher(
        string? connectionString, params string[] args) =>
        RunLauncher(connectionString, writeEnvFile: true, args);

    private static (string Stdout, string Stderr, int ExitCode) RunLauncher(
        string? connectionString, bool writeEnvFile, params string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        try
        {
            var envFile = Path.Combine(root, "netrisk.env");

            if (writeEnvFile)
                File.WriteAllText(
                    envFile,
                    "# NetRisk service environment\n"
                    + (connectionString is null
                        ? ""
                        : $"Database__ConnectionString={connectionString}\n"));

            // Present so the launcher's working directory is observable, exactly as the real
            // /netrisk/appsettings.json is the file the cwd exists for.
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{}");

            var stub = Path.Combine(root, "ConsoleClient");
            File.WriteAllText(stub,
                "#!/bin/bash\n"
                + "first=\"$1\"\n"
                + "printf 'connection-string=%s\\n' \"$Database__ConnectionString\"\n"
                + "printf 'args=['\n"
                + "printf '%s' \"$1\"; shift; for a in \"$@\"; do printf '|%s' \"$a\"; done\n"
                + "printf ']\\n'\n"
                + "[ -f ./appsettings.json ] && echo 'cwd-has-appsettings=yes'\n"
                + "[ \"$first\" = 'fail' ] && exit 23\n"
                + "exit 0\n");
            MakeExecutable(stub);

            var script = Path.Combine(root, "netrisk-console");
            File.WriteAllText(script, Launcher
                .Replace("/netrisk/netrisk.env", envFile, StringComparison.Ordinal)
                .Replace("cd /netrisk", "cd " + root, StringComparison.Ordinal));
            MakeExecutable(script);

            var startInfo = new ProcessStartInfo("/bin/bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            startInfo.ArgumentList.Add(script);
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);

            using var process = Process.Start(startInfo)!;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (stdout, stderr, process.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
