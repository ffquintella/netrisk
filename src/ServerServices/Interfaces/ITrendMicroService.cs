using DAL.Entities;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// Trend Micro Vision One connection management and synchronization
/// (Track 4 milestone 4.4).
/// </summary>
public interface ITrendMicroService
{
    Task<List<TrendMicroConnectionView>> GetConnectionsAsync(bool includeDisabled = true);

    Task<TrendMicroConnectionView> GetConnectionAsync(int id);

    Task<TrendMicroConnectionView> CreateConnectionAsync(TrendMicroConnection connection, string? apiKey);

    /// <summary>A null <paramref name="apiKey"/> leaves the stored one alone.</summary>
    Task<TrendMicroConnectionView> UpdateConnectionAsync(TrendMicroConnection connection, string? apiKey);

    Task DeleteConnectionAsync(int id);

    /// <summary>The region-aware test connection utility (4.4.1).</summary>
    Task<ConnectionTestResult> TestConnectionAsync(int id);

    /// <summary>The region codes and API roots the connection form offers.</summary>
    IReadOnlyDictionary<string, string> GetRegions();

    /// <summary>
    /// Runs one full synchronization for a connection: inventory (4.4.2), CVEs with virtual-patch
    /// state (4.4.3), and risk scores rolled into the entity's Cyber Risk Index (4.4.4). Which parts
    /// run is decided by the connection's flags.
    /// </summary>
    Task<PostureSyncResult> SyncAsync(int connectionId, CancellationToken ct = default);

    /// <summary>Syncs every enabled connection whose interval has elapsed. The job's entry point.</summary>
    Task<PostureSyncResult> SyncDueConnectionsAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Pushes an exemption to Vision One for a finding NetRisk has accepted (4.4.4). Only acts when the
    /// connection opted in; returns false otherwise, so the caller can say so rather than assume.
    /// </summary>
    Task<bool> PushExemptionAsync(int findingId, string reason, CancellationToken ct = default);

    /// <summary>Recent sync-log rows for this integration.</summary>
    Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50);
}
