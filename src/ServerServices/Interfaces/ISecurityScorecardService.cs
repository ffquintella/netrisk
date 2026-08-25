using DAL.Entities;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// SecurityScorecard connection management and synchronization (Track 4 milestone 4.5).
/// </summary>
public interface ISecurityScorecardService
{
    Task<List<SecurityScorecardConnectionView>> GetConnectionsAsync(bool includeDisabled = true);

    Task<SecurityScorecardConnectionView> GetConnectionAsync(int id);

    Task<SecurityScorecardConnectionView> CreateConnectionAsync(SecurityScorecardConnection connection,
        string? apiToken);

    /// <summary>A null <paramref name="apiToken"/> leaves the stored one alone.</summary>
    Task<SecurityScorecardConnectionView> UpdateConnectionAsync(SecurityScorecardConnection connection,
        string? apiToken);

    Task DeleteConnectionAsync(int id);

    /// <summary>Domain-and-token test connection (4.5.1).</summary>
    Task<ConnectionTestResult> TestConnectionAsync(int id);

    /// <summary>
    /// One full synchronization: overall score and grade onto the entity's Cyber Risk Index, the ten
    /// factor scores appended to the trend history (4.5.2), domain CVEs and active issues ingested as
    /// findings (4.5.3).
    /// </summary>
    Task<PostureSyncResult> SyncAsync(int connectionId, CancellationToken ct = default);

    /// <summary>Syncs every enabled connection whose interval has elapsed. The job's entry point.</summary>
    Task<PostureSyncResult> SyncDueConnectionsAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// The stored factor history for a connection, newest first — what the trend chart reads. The
    /// synthetic overall row is included and flagged.
    /// </summary>
    Task<List<SecurityScorecardFactor>> GetFactorHistoryAsync(int connectionId, int limit = 500);

    Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50);
}
