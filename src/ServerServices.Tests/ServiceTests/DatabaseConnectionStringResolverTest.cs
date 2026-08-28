using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using MySqlConnector;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// A missing <c>Database:ConnectionString</c> must be diagnosed by name.
///
/// It used to reach <c>new MySqlConnection("")</c> at six call sites, and MySqlConnector reads an
/// empty connection string as <c>server=localhost;port=3306</c> — so the setting being unset showed
/// up as an unreachable MySQL host, or, on a machine running a local MariaDB, as a successful
/// connection to the wrong database. Neither mentions the setting.
/// <see cref="ServerServices.SchemaUpgrade.NumberedSqlConnectionString.Normalize"/> passes an empty
/// value through untouched precisely so that this guard, not MySqlConnector, produces the message.
/// </summary>
public class DatabaseConnectionStringResolverTest
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Require_MissingValue_ThrowsNamingTheSetting(string? configured)
    {
        var ex = Assert.Throws<DatabaseConnectionStringMissingException>(
            () => DatabaseConnectionStringResolver.Require(configured));

        Assert.Contains("Database:ConnectionString", ex.Message);
        Assert.Contains("Database__ConnectionString", ex.Message);
        // The fallback is the whole point: the message has to say that unset means localhost.
        Assert.Contains("localhost", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingConfiguration_ThrowsNamingTheSetting(string? configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = configured
            })
            .Build();

        var ex = Assert.Throws<DatabaseConnectionStringMissingException>(
            () => DatabaseConnectionStringResolver.Resolve(configuration));

        Assert.Contains("Database:ConnectionString", ex.Message);
        Assert.Contains("Database__ConnectionString", ex.Message);
    }

    [Fact]
    public void Resolve_AbsentKey_ThrowsNamingTheSetting()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<DatabaseConnectionStringMissingException>(
            () => DatabaseConnectionStringResolver.Resolve(configuration));

        Assert.Contains("Database:ConnectionString", ex.Message);
    }

    [Fact]
    public void Resolve_ConfiguredValue_IsReadFromTheSettingAndNormalized()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "server=db;uid=netrisk;pwd=secret;Port=3307;database=netrisk"
            })
            .Build();

        var resolved = new MySqlConnectionStringBuilder(
            DatabaseConnectionStringResolver.Resolve(configuration));

        Assert.Equal("db", resolved.Server);
        Assert.Equal(3307u, resolved.Port);
        Assert.Equal("netrisk", resolved.Database);
        // Still the numbered-SQL normalization: the guard is added to it, not substituted for it.
        Assert.True(resolved.AllowUserVariables);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveOptional_MissingValue_DoesNotThrow(string? configured)
    {
        // SchemaUpgradeService reads the setting in its constructor and its dry-run mode needs no
        // database, so construction must survive a missing value; it reports the same message from
        // the paths that do need a connection.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = configured
            })
            .Build();

        Assert.True(string.IsNullOrWhiteSpace(DatabaseConnectionStringResolver.ResolveOptional(configuration)));
    }

    [Fact]
    public void MissingMessage_IsTheTextTheThrownExceptionCarries()
    {
        // One text for both the throwing and the reporting paths (DatabaseService.Status, the
        // schema-upgrade reports), so the operator sees the same remedy either way.
        var ex = Assert.Throws<DatabaseConnectionStringMissingException>(
            () => DatabaseConnectionStringResolver.Require(null));

        Assert.Equal(DatabaseConnectionStringResolver.MissingMessage, ex.Message);
    }
}
