using MySqlConnector;

namespace ServerServices.SchemaUpgrade;

/// <summary>
/// Normalizes the configured connection string for running the numbered upgrade SQL
/// (<c>DB/Structure/{n}.sql</c> and <c>DB/Data/{n}.sql</c>).
///
/// Those scripts guard renames — the one kind of DDL MariaDB has no <c>IF NOT EXISTS</c> for — with
/// a <c>SET @nr_ddl = IF(…); PREPARE … EXECUTE …</c> block, so a version that failed part-way can be
/// applied again without tripping over the half it already did. MySqlConnector reads <c>@name</c> as
/// a parameter placeholder unless <c>AllowUserVariables</c> is on, and would reject those scripts
/// with <c>Parameter '@nr_ddl' must be defined</c> before the server ever saw them. Deployments
/// configure their own connection string, so this cannot be left to whoever writes it.
/// </summary>
public static class NumberedSqlConnectionString
{
    public static string Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString ?? "";

        var builder = new MySqlConnectionStringBuilder(connectionString) { AllowUserVariables = true };

        return builder.ConnectionString;
    }
}
