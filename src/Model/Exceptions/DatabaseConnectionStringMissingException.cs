namespace Model.Exceptions;

/// <summary>
/// The database connection string is not configured at all.
///
/// Its own exception type because the failure it replaces was actively misleading rather than merely
/// unhelpful: MySqlConnector reads an empty connection string as <c>server=localhost;port=3306</c>,
/// so an unset setting either failed with "Unable to connect to any of the specified MySQL hosts" —
/// naming neither the setting nor the fallback — or, on a host that happens to run a local MariaDB,
/// connected successfully to the wrong database and reported "Schema does not exist".
///
/// The message is built by <c>ServerServices.Services.DatabaseConnectionStringResolver</c>, which is
/// the single place the setting is read; paths that must report instead of throwing use the same text.
/// </summary>
public class DatabaseConnectionStringMissingException : Exception
{
    public DatabaseConnectionStringMissingException(string message) : base(message)
    {
    }

    public DatabaseConnectionStringMissingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
