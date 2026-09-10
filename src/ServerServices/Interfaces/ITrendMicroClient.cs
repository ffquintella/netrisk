using DAL.Entities;
using Model.Integrations;

namespace ServerServices.Interfaces;

/// <summary>
/// The Vision One REST calls NetRisk makes (Track 4 milestone 4.4).
///
/// Behind an interface so the sync service can be tested against captured payloads: an integration
/// whose parsing can only be exercised by calling Trend Micro is an integration nobody can test.
/// </summary>
public interface ITrendMicroClient
{
    /// <summary>
    /// Region-aware credential check (4.4.1): a one-row read of the ASRM device endpoint, which also
    /// proves the key carries the ASRM permission.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(TrendMicroConnection connection, string? apiKey,
        CancellationToken ct = default);

    /// <summary>The attack-surface device inventory (4.4.2), all pages.</summary>
    Task<List<TrendMicroDevice>> GetDevicesAsync(TrendMicroConnection connection, string? apiKey,
        CancellationToken ct = default);

    /// <summary>
    /// Per-device CVEs including virtual-patch state (4.4.3), all pages.
    ///
    /// Read from <c>/v3.0/asrm/vulnerableDevices</c> ("Get CVEs detected in a device"), which is the
    /// only Vision One endpoint that carries CVE identities per asset. 2.19.6 read them off
    /// <c>attackSurfaceDevices</c> instead, on the belief that this path did not exist; it does, and
    /// the inventory rows carry only a <c>cveCount</c>, so that sync reported zero findings on a
    /// tenant with 16,000 devices. Note the role behind the key needs *Dashboards &amp; Reports →
    /// Reports → View* for this endpoint, which the inventory endpoint does not require.
    /// </summary>
    Task<List<TrendMicroDeviceVulnerability>> GetVulnerableDevicesAsync(TrendMicroConnection connection,
        string? apiKey, CancellationToken ct = default);

    /// <summary>
    /// Writes asset criticality or an exemption note back to Vision One (4.4.4). Returns false rather
    /// than throwing: a refused write-back must not fail the sync that triggered it.
    /// </summary>
    Task<bool> UpdateDeviceAsync(TrendMicroConnection connection, string? apiKey, string deviceId,
        int? criticality, string? note, CancellationToken ct = default);
}
