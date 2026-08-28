using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Regression tests for the symptom, not just the guard: with <c>Database:ConnectionString</c> unset,
/// <c>DatabaseService</c> used to hand <c>""</c> to MySqlConnector, which reads it as
/// <c>server=localhost;port=3306</c>. On a machine with a local MariaDB, <c>database status</c>
/// therefore connected to the wrong server and printed "Schema does not exist"; elsewhere it printed
/// "Offline". Both send the operator to look at the database server instead of at the configuration.
///
/// These tests deliberately assert on observable behaviour (status, message text, whether the call
/// throws) rather than on the guard's type, so they fail against the pre-fix code.
/// </summary>
public class MissingConnectionStringDiagnosisTest
{
    private const string SettingName = "Database:ConnectionString";

    private static DatabaseService BuildWithNoConnectionString()
    {
        // An empty configuration, exactly like a `docker exec` that did not inherit the env file's
        // exports: the key resolves to null.
        var configuration = new ConfigurationBuilder().Build();

        return new DatabaseService(
            configuration,
            Substitute.For<ILogger>(),
            Substitute.For<IConfigurationsService>(),
            Substitute.For<IDalService>());
    }

    [Fact]
    public void Status_WithNoConnectionString_NamesTheSettingInsteadOfDialingLocalhost()
    {
        var status = BuildWithNoConnectionString().Status();

        Assert.Equal("Misconfigured", status.Status);
        Assert.Contains(SettingName, status.Message);
        Assert.Contains("Database__ConnectionString", status.Message);

        // The old outputs, which are what this fix exists to stop producing.
        Assert.DoesNotContain("Schema does not exist", status.Message);
        Assert.NotEqual("Offline", status.Status);
    }

    [Fact]
    public void Init_WithNoConnectionString_ReportsTheSettingRatherThanAnOfflineDatabase()
    {
        var result = BuildWithNoConnectionString().Init(initialVersion: 1, targetVersion: 2);

        Assert.Equal("Error", result.Status);
        Assert.Contains(SettingName, result.Message);
        Assert.DoesNotContain("Database is offline", result.Message);
    }

    [Fact]
    public void Update_WithNoConnectionString_ReportsTheSettingRatherThanAnOfflineDatabase()
    {
        var result = BuildWithNoConnectionString().Update(initialVersion: 1, targetVersion: 2);

        Assert.Equal("Error", result.Status);
        Assert.Contains(SettingName, result.Message);
        Assert.DoesNotContain("Database is offline", result.Message);
    }

    [Fact]
    public void Backup_WithNoConnectionString_FailsLoudlyInsteadOfSwallowingTheError()
    {
        // Backup's catch-all logs and returns, so the resolution has to happen outside it: a backup
        // that quietly did nothing is worse than one that fails.
        var destination = Path.Combine(Path.GetTempPath(), "nr-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            var exception = Record.Exception(() => BuildWithNoConnectionString().Backup(destination));

            Assert.NotNull(exception);
            Assert.Contains(SettingName, exception!.Message);
            Assert.Empty(Directory.GetFiles(destination));
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public void Restore_WithNoConnectionString_FailsLoudlyInsteadOfSwallowingTheError()
    {
        var source = Path.Combine(Path.GetTempPath(), "nr-restore-" + Guid.NewGuid().ToString("N") + ".sql");
        File.WriteAllText(source, "-- empty backup");
        try
        {
            var exception = Record.Exception(() => BuildWithNoConnectionString().Restore(source));

            Assert.NotNull(exception);
            Assert.Contains(SettingName, exception!.Message);
        }
        finally
        {
            File.Delete(source);
        }
    }
}
