namespace Model.Database;

public class DatabaseStatus
{
    /// <summary>
    /// <see cref="Status"/> value for "nothing was contacted, because the connection string is not
    /// configured". Distinct from <c>Offline</c> on purpose: reporting a configuration fault as an
    /// offline server is what used to send operators to look at the database host.
    /// </summary>
    public const string Misconfigured = "Misconfigured";


    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string Version { get; set; } = "---";
    
    public string ServerVersion { get; set; } = "---";
}