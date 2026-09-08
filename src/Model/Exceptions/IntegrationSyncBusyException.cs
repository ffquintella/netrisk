using System;

namespace Model.Exceptions;

/// <summary>
/// A synchronization was asked for while one was already running for the same connection (Track 4).
///
/// Distinct from a generic exception because the API surfaces it as 409 rather than 500 or 502: the
/// request was refused, nothing failed, and — decisively — a 5xx is what the desktop client's
/// reliable REST wrapper retries. A refused duplicate that answered 502 would be retried up to ten
/// more times, which is how one click on "Sync now" turned into eleven Running rows in three seconds
/// on the sync-log screen.
/// </summary>
public class IntegrationSyncBusyException : Exception
{
    public IntegrationSyncBusyException(string provider, string connectionName, DateTime startedAtUtc)
        : base($"A {provider} synchronization for '{connectionName}' has been running since "
               + $"{startedAtUtc:u}. Only one run per connection is allowed at a time.")
    {
        Provider = provider;
        ConnectionName = connectionName;
        StartedAtUtc = startedAtUtc;
    }

    public string Provider { get; }

    public string ConnectionName { get; }

    /// <summary>When the run that holds the connection started, so the operator can judge whether it is stuck.</summary>
    public DateTime StartedAtUtc { get; }
}
