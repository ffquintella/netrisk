using Microsoft.Extensions.Configuration;
using Model.Exceptions;
using ServerServices.SchemaUpgrade;

namespace ServerServices.Services;

/// <summary>
/// The single place the server-side code reads <c>Database:ConnectionString</c> and decides what a
/// missing value means.
///
/// It can only mean "stop and say which setting is missing". An empty connection string is not a
/// neutral default: MySqlConnector reads it as <c>server=localhost;port=3306</c>, so before this
/// guard existed an unset setting reached <c>new MySqlConnection("")</c> at six call sites and
/// surfaced either as "Unable to connect to any of the specified MySQL hosts" or — on a host that
/// happens to run a local MariaDB — as a clean connection to the wrong database reported as
/// "Schema does not exist". Both diagnoses point the operator at the database server, which is fine,
/// and away from the configuration, which is where the fault is.
///
/// <see cref="NumberedSqlConnectionString.Normalize"/> deliberately passes an empty value straight
/// through for this reason: parsing it there would replace this message with a MySqlConnector
/// format exception that names nothing.
/// </summary>
public static class DatabaseConnectionStringResolver
{
    /// <summary>Configuration key holding the MySQL connection string.</summary>
    public const string SettingKey = "Database:ConnectionString";

    /// <summary>The same key as an environment variable, which is how deployments supply it.</summary>
    public const string EnvironmentVariableKey = "Database__ConnectionString";

    /// <summary>
    /// The one diagnostic text, used both by the thrown
    /// <see cref="DatabaseConnectionStringMissingException"/> and by the paths whose contract is to
    /// report rather than throw (<c>DatabaseService.Status</c>, the schema-upgrade reports).
    /// </summary>
    public const string MissingMessage =
        SettingKey + " is not configured. An empty connection string is not a default: MySqlConnector "
        + "reads it as server=localhost;port=3306, so leaving it unset silently points NetRisk at the "
        + "local machine instead of the configured database. Set it in appsettings or user-secrets as '"
        + SettingKey + "', or in the environment as '" + EnvironmentVariableKey + "'. When the value "
        + "comes from a deployment env file loaded by the container entrypoint, remember that "
        + "'docker exec' does not inherit those exports — pass it explicitly ('docker exec -e "
        + EnvironmentVariableKey + "=...') or source the env file in the same shell.";

    /// <summary>
    /// Reads, guards and normalizes the connection string for direct MySqlConnector use.
    /// </summary>
    /// <exception cref="DatabaseConnectionStringMissingException">
    /// The setting is absent, empty or whitespace.
    /// </exception>
    public static string Resolve(IConfiguration configuration) => Require(configuration[SettingKey]);

    /// <inheritdoc cref="Resolve(IConfiguration)"/>
    public static string Require(string? configuredValue) =>
        string.IsNullOrWhiteSpace(configuredValue)
            ? throw new DatabaseConnectionStringMissingException(MissingMessage)
            : NumberedSqlConnectionString.Normalize(configuredValue);

    /// <summary>
    /// Reads and normalizes without guarding, for the caller that resolves earlier than it needs a
    /// database: <c>SchemaUpgradeService</c> reads the setting in its constructor, but its dry-run
    /// mode only emits SQL, so a missing value must not fail construction. That service reports
    /// <see cref="MissingMessage"/> from the paths that do need a connection.
    /// </summary>
    public static string ResolveOptional(IConfiguration configuration) =>
        NumberedSqlConnectionString.Normalize(configuration[SettingKey]);
}
