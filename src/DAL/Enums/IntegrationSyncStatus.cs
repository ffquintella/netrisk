namespace DAL.Enums;

/// <summary>
/// Outcome of one integration synchronization run (Track 4 milestones 4.2–4.5), persisted in
/// <c>integration_sync_logs.status</c>.
///
/// <see cref="PartiallySucceeded"/> earns its place: an inventory sync that imported 900 of 1000
/// devices is neither a success nor a failure, and reporting it as either one is how a persistent
/// per-device mapping bug goes unnoticed for months.
/// </summary>
public enum IntegrationSyncStatus
{
    Running = 1,
    Succeeded = 2,
    PartiallySucceeded = 3,
    Failed = 4,

    /// <summary>
    /// A manual sync request waiting for the job host (S40 §7.3, §8.7; T265): the API writes the row and returns, and the
    /// Tenable job claims it atomically on its next tick. Never written for an integration that syncs inside the request.
    /// </summary>
    Queued = 5
}
