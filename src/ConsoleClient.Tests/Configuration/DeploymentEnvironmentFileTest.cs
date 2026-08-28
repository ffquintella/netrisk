#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConsoleClient.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Model.Exceptions;
using MySqlConnector;
using ServerServices.Services;
using Xunit;

namespace ConsoleClient.Tests.Configuration;

/// <summary>
/// The deployment env file as a configuration source.
///
/// The bug: <c>netrisk-console database update</c> on a deployed host reported
/// "Unable to connect to any of the specified MySQL hosts" against localhost, while
/// <c>/netrisk/netrisk.env</c> held a correct connection string the whole time. The credential moved
/// there for NR-2026-025, the entrypoint exports it into PID 1, and the console container is a
/// keepalive — so operator commands arrive by <c>docker exec</c>, which inherits none of it. The
/// host-side <c>/usr/local/bin/netrisk-console</c> that operators actually type is not shipped from
/// this repository, is three years older than the change, and runs <c>/netrisk/ConsoleClient</c>
/// directly — past the in-image launcher that 2.17.3 added to re-read the file.
///
/// So the binary reads the file itself. Every assertion here is about doing that with the shell
/// loader's exact rules, because the value is a connection string full of shell metacharacters and
/// parsing it as shell is what caused the 2.17.0 restart loop.
/// </summary>
public class DeploymentEnvironmentFileTest : IDisposable
{
    /// <summary>The value Puppet renders on the affected host, shape for shape.</summary>
    private const string ConnectionString =
        "server=10.60.65.119;port=4306;uid=netrisk;pwd=s3cr3t;database=netrisk;ConvertZeroDateTime=True";

    /// <summary>
    /// A value made of every character a shell would act on — the same one
    /// <c>Packaging.Tests/ConsoleLauncherTest</c> pushes through the bash loader. Used only where the
    /// assertion is byte preservation; it is not a well-formed connection string, because an
    /// unquoted <c>;</c> in a password is not one, and MySqlConnector rejects it downstream.
    /// </summary>
    private const string ShellHostileValue = "a;b$c`d\"e'f g|h&i>j";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "netrisk-envfile-" + Guid.NewGuid().ToString("N"));

    private string WriteEnvFile(string contents)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "netrisk.env");
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DeploymentEnvironmentFile.PathOverrideVariable, null);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The regression. A Puppet-rendered file, an environment that carries nothing — which is what a
    /// <c>docker exec</c> gives you — and the connection string still resolves, character for
    /// character. On the pre-fix configuration this threw
    /// <see cref="DatabaseConnectionStringMissingException"/>; see
    /// <see cref="TestTheSettingIsUnresolvableWithoutTheFileSource"/> for that half of the proof.
    /// </summary>
    [Fact]
    public void TestTheConnectionStringResolvesFromTheFileWithNothingInTheEnvironment()
    {
        var path = WriteEnvFile(
            "# NetRisk service environment -- security finding NR-2026-025.\n" +
            "#\n" +
            "# Managed by Puppet with mode 0600 owned by the service account.\n" +
            "Database__ConnectionString=" + ConnectionString);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(DeploymentEnvironmentFile.Read(path))
            .Build();

        Assert.Equal(ConnectionString, configuration[DatabaseConnectionStringResolver.SettingKey]);

        // The failure this replaces was an empty connection string read as server=localhost;port=3306.
        var resolved = new MySqlConnectionStringBuilder(
            DatabaseConnectionStringResolver.Resolve(configuration));

        Assert.Equal("10.60.65.119", resolved.Server);
        Assert.Equal(4306u, resolved.Port);
        Assert.Equal("netrisk", resolved.Database);
        Assert.Equal("netrisk", resolved.UserID);
    }

    /// <summary>
    /// The other half: without the file source the setting is absent, and the resolver says so. This
    /// is the state every deployed host was in — appsettings.json carries a comment where the
    /// connection string used to be, and the exec'd process's environment carries only the
    /// image's FACTER_* variables.
    /// </summary>
    [Fact]
    public void TestTheSettingIsUnresolvableWithoutTheFileSource()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Comment"] = "the connection string comes from the environment",
                ["FACTER_SERVER_HTTPS_PORT"] = "5443",
            })
            .Build();

        Assert.Throws<DatabaseConnectionStringMissingException>(
            () => DatabaseConnectionStringResolver.Resolve(configuration));
    }

    /// <summary>
    /// <c>Database__ConnectionString</c> in the file has to bind to <c>Database:ConnectionString</c>
    /// in configuration, the way the environment provider maps it — otherwise the file would need a
    /// second spelling and the entrypoint export and this reader would disagree.
    /// </summary>
    [Fact]
    public void TestTheDoubleUnderscoreBecomesTheConfigurationSeparator()
    {
        var entries = DeploymentEnvironmentFile.Parse(["Database__ConnectionString=x", "A__B__C=y"]);

        Assert.Contains(entries, e => e.Key == "Database:ConnectionString" && e.Value == "x");
        Assert.Contains(entries, e => e.Key == "A:B:C" && e.Value == "y");
    }

    /// <summary>
    /// The value is the raw remainder of the line. No unquoting, no unescaping, no trimming, and no
    /// treating <c>;</c> as anything: that is the whole reason the shell loader exports
    /// <c>${line#*=}</c> instead of sourcing the file.
    /// </summary>
    [Theory]
    [InlineData("K=" + ConnectionString, ConnectionString)]
    [InlineData("K=" + ShellHostileValue, ShellHostileValue)]
    [InlineData("K=\"quoted\"", "\"quoted\"")]
    [InlineData("K='single'", "'single'")]
    [InlineData("K=  padded  ", "  padded  ")]
    [InlineData("K=$(id)", "$(id)")]
    [InlineData("K=a=b=c", "a=b=c")]
    [InlineData("K=value # not a comment", "value # not a comment")]
    [InlineData("K=", "")]
    public void TestTheValueIsTheRestOfTheLineVerbatim(string line, string expected)
    {
        var entries = DeploymentEnvironmentFile.Parse([line]);

        Assert.Equal(expected, Assert.Single(entries).Value);
    }

    /// <summary>Only a line that starts with <c>#</c> is a comment, and only a line with an <c>=</c> is a setting.</summary>
    [Theory]
    [InlineData("# Database__ConnectionString=commented-out")]
    [InlineData("#Database__ConnectionString=commented-out")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_AN_ASSIGNMENT")]
    public void TestNonSettingLinesAreSkipped(string line)
    {
        Assert.Empty(DeploymentEnvironmentFile.Parse([line]));
    }

    /// <summary>
    /// The shell loader refuses any key that is not a plain identifier, because it feeds the name to
    /// <c>export</c>. This reader refuses the same ones, so a file that yields nothing in the
    /// entrypoint cannot quietly yield something here.
    /// </summary>
    [Theory]
    [InlineData("1KEY=value")]
    [InlineData("KEY WITH SPACE=value")]
    [InlineData("KEY-WITH-DASH=value")]
    [InlineData("KEY.WITH.DOT=value")]
    [InlineData("=value")]
    [InlineData("export KEY=value")]
    public void TestKeysThatAreNotIdentifiersAreSkipped(string line)
    {
        Assert.Empty(DeploymentEnvironmentFile.Parse([line]));
    }

    /// <summary>A repeated key takes the last value, because a repeated <c>export</c> overwrites.</summary>
    [Fact]
    public void TestTheLastAssignmentWins()
    {
        var entries = DeploymentEnvironmentFile.Parse(
            ["Database__ConnectionString=first", "Database__ConnectionString=second"]);

        Assert.Equal("second", Assert.Single(entries).Value);
    }

    /// <summary>
    /// The template's last line has no trailing newline. The shell loop keeps it
    /// (<c>|| [ -n "$line" ]</c>) and so must this.
    /// </summary>
    [Fact]
    public void TestTheFinalLineIsReadWithoutATrailingNewline()
    {
        var path = WriteEnvFile("# comment\nDatabase__ConnectionString=" + ConnectionString);

        Assert.Equal(ConnectionString,
            Assert.Single(DeploymentEnvironmentFile.Read(path)).Value);
    }

    /// <summary>
    /// No file is the normal case everywhere but a container, and an unreadable one is the normal
    /// case for a user who is not the service account — the file is mode 0600. Neither may throw:
    /// the resolver's message about the missing setting is a better diagnosis than a stack trace
    /// about a path.
    /// </summary>
    [Fact]
    public void TestAnAbsentFileYieldsNothingAndDoesNotThrow()
    {
        Assert.Empty(DeploymentEnvironmentFile.Read(
            Path.Combine(_directory, "does-not-exist", "netrisk.env")));
    }

    /// <summary>The default path is the one Puppet writes and every entrypoint reads.</summary>
    [Fact]
    public void TestTheDefaultPathIsTheDeployedOne()
    {
        Environment.SetEnvironmentVariable(DeploymentEnvironmentFile.PathOverrideVariable, null);

        Assert.Equal("/netrisk/netrisk.env", DeploymentEnvironmentFile.DefaultPath);
        Assert.Equal("/netrisk/netrisk.env", DeploymentEnvironmentFile.ResolvePath());
    }

    /// <summary><see cref="DeploymentEnvironmentFile.PathOverrideVariable"/> redirects the read.</summary>
    [Fact]
    public void TestThePathCanBeOverriddenForANonContainerRun()
    {
        var path = WriteEnvFile("Database__ConnectionString=" + ConnectionString);
        Environment.SetEnvironmentVariable(DeploymentEnvironmentFile.PathOverrideVariable, path);

        Assert.Equal(path, DeploymentEnvironmentFile.ResolvePath());
        Assert.Equal(ConnectionString, Assert.Single(DeploymentEnvironmentFile.Read()).Value);
    }

    /// <summary>
    /// Precedence, which is the one thing the ordering in <c>Program.CreateHostBuilder</c> decides:
    /// the file beats appsettings.json — the deployed one carries a comment where the credential used
    /// to be — and loses to the real process environment, so
    /// <c>docker exec -e Database__ConnectionString=...</c> still overrides it.
    /// </summary>
    [Fact]
    public void TestTheFileBeatsAppsettingsAndLosesToTheEnvironment()
    {
        var path = WriteEnvFile("Database__ConnectionString=from-the-env-file");

        var withoutEnvironment = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnectionStringResolver.SettingKey] = "from-appsettings",
            })
            .AddInMemoryCollection(DeploymentEnvironmentFile.Read(path))
            .Build();

        Assert.Equal("from-the-env-file",
            withoutEnvironment[DatabaseConnectionStringResolver.SettingKey]);

        var withEnvironment = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnectionStringResolver.SettingKey] = "from-appsettings",
            })
            .AddInMemoryCollection(DeploymentEnvironmentFile.Read(path))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseConnectionStringResolver.SettingKey] = "from-docker-exec-dash-e",
            })
            .Build();

        Assert.Equal("from-docker-exec-dash-e",
            withEnvironment[DatabaseConnectionStringResolver.SettingKey]);
    }

    /// <summary>
    /// The guard that matters most: the host builder the shipped binary uses actually registers this
    /// source, and registers it in the right place. Every other test here would still pass if the one
    /// line in <c>Program.CreateHostBuilder</c> were deleted, and the deployed hosts would be right
    /// back where they started.
    /// </summary>
    [Fact]
    public void TestTheConsoleHostBuilderReadsTheDeploymentEnvironmentFile()
    {
        var path = WriteEnvFile("Database__ConnectionString=" + ConnectionString);
        Environment.SetEnvironmentVariable(DeploymentEnvironmentFile.PathOverrideVariable, path);

        using var host = Program.CreateHostBuilder([]).Build();
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(ConnectionString, configuration[DatabaseConnectionStringResolver.SettingKey]);
    }

    /// <summary>
    /// And it loses to the real process environment, so an operator who passes the credential
    /// explicitly — <c>docker exec -e Database__ConnectionString=...</c> — still overrides the file.
    /// </summary>
    [Fact]
    public void TestTheProcessEnvironmentStillOverridesTheFileInTheConsoleHostBuilder()
    {
        var path = WriteEnvFile("Database__ConnectionString=" + ConnectionString);
        Environment.SetEnvironmentVariable(DeploymentEnvironmentFile.PathOverrideVariable, path);
        Environment.SetEnvironmentVariable(
            DatabaseConnectionStringResolver.EnvironmentVariableKey, "server=explicit");

        try
        {
            using var host = Program.CreateHostBuilder([]).Build();
            var configuration = host.Services.GetRequiredService<IConfiguration>();

            Assert.Equal("server=explicit", configuration[DatabaseConnectionStringResolver.SettingKey]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                DatabaseConnectionStringResolver.EnvironmentVariableKey, null);
        }
    }

    /// <summary>
    /// The reader and the four entrypoints plus the in-image launcher must agree, so this replays
    /// the rules against the shipped shell loader's own source: whatever key filter and value slice
    /// that script uses, it is the one asserted above.
    /// </summary>
    [Fact]
    public void TestTheRulesMatchTheShippedShellLoader()
    {
        var loader = File.ReadAllText(Path.Combine(
            RepoLayout.RepositoryRoot.FullName, "build", "Docker", "netrisk-console.sh"));

        Assert.Contains("^[A-Za-z_][A-Za-z0-9_]*$", loader, StringComparison.Ordinal);
        Assert.Contains("export \"$key=${line#*=}\"", loader, StringComparison.Ordinal);
        Assert.Contains("key=${line%%=*}", loader, StringComparison.Ordinal);
        Assert.Contains("if [[ $line == \\#* || $line != *=* ]]; then continue; fi",
            loader, StringComparison.Ordinal);
    }
}
