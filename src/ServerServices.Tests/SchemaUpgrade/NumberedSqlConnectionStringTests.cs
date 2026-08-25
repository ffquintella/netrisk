using MySqlConnector;
using ServerServices.SchemaUpgrade;
using Xunit;

namespace ServerServices.Tests.SchemaUpgrade;

/// <summary>
/// The numbered upgrade scripts guard renames with <c>SET @nr_ddl = IF(…); PREPARE … EXECUTE …</c>,
/// which MySqlConnector reads as a parameter placeholder unless <c>AllowUserVariables</c> is on.
/// Deployments write their own connection string, so if this normalization is dropped the upgrade
/// fails before the server ever sees the SQL — and it fails on every install at once.
/// </summary>
public class NumberedSqlConnectionStringTests
{
    [Fact]
    public void Normalize_TurnsOnAllowUserVariables()
    {
        var normalized = NumberedSqlConnectionString.Normalize(
            "server=db;uid=netrisk;pwd=secret;Port=3306;database=netrisk");

        Assert.True(new MySqlConnectionStringBuilder(normalized).AllowUserVariables);
    }

    [Fact]
    public void Normalize_KeepsTheRestOfTheConnectionStringIntact()
    {
        var normalized = new MySqlConnectionStringBuilder(NumberedSqlConnectionString.Normalize(
            "server=db;uid=netrisk;pwd=secret;Port=3307;database=netrisk;ConvertZeroDateTime=True"));

        Assert.Equal("db", normalized.Server);
        Assert.Equal("netrisk", normalized.UserID);
        Assert.Equal("secret", normalized.Password);
        Assert.Equal(3307u, normalized.Port);
        Assert.Equal("netrisk", normalized.Database);
        Assert.True(normalized.ConvertZeroDateTime);
    }

    [Fact]
    public void Normalize_LeavesAnExplicitOptOutOverridden()
    {
        // The scripts do not work without it, so an operator switching it off has to lose: a silent
        // "your setting wins" here would surface as an unexplainable upgrade failure in the field.
        var normalized = NumberedSqlConnectionString.Normalize(
            "server=db;database=netrisk;AllowUserVariables=False");

        Assert.True(new MySqlConnectionStringBuilder(normalized).AllowUserVariables);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_PassesEmptyConfigurationThroughUntouched(string? connectionString)
    {
        // A missing connection string is diagnosed by the caller with a message that names the
        // setting; parsing it here would replace that with a MySqlConnector format exception.
        Assert.Equal(connectionString ?? "", NumberedSqlConnectionString.Normalize(connectionString));
    }
}
