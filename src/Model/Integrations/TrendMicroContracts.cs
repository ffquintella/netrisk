using DAL.Enums;

namespace Model.Integrations;

/// <summary>
/// A Trend Micro Vision One connection as a client sees it (Track 4 milestone 4.4.1). The API key is
/// never returned — only whether one is set.
/// </summary>
public class TrendMicroConnectionView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public bool HasApiKey { get; set; }

    public int? EntityId { get; set; }

    public bool Enabled { get; set; }

    public int SyncIntervalHours { get; set; }

    public bool SyncVulnerabilities { get; set; }

    public bool SyncRiskScores { get; set; }

    public bool VirtualPatchClosesFinding { get; set; }

    public bool PushExemptions { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }
}

/// <summary>
/// The Vision One regions and their API roots (Track 4 milestone 4.4.1).
///
/// Enumerated in code rather than typed into a free-text field because a token issued in one region is
/// rejected by every other, and "why does my valid API key return 401" is the support call this list
/// exists to prevent.
/// </summary>
public static class TrendMicroRegions
{
    public static readonly IReadOnlyDictionary<string, string> BaseUrls = new Dictionary<string, string>
    {
        ["us"] = "https://api.xdr.trendmicro.com",
        ["eu"] = "https://api.eu.xdr.trendmicro.com",
        ["jp"] = "https://api.xdr.trendmicro.co.jp",
        ["sg"] = "https://api.sg.xdr.trendmicro.com",
        ["au"] = "https://api.au.xdr.trendmicro.com",
        ["in"] = "https://api.in.xdr.trendmicro.com",
        ["mea"] = "https://api.mea.xdr.trendmicro.com"
    };

    /// <summary>The API root for a region code, or null when the code is not one Vision One serves.</summary>
    public static string? BaseUrlFor(string? region) =>
        region == null ? null : BaseUrls.GetValueOrDefault(region.Trim().ToLowerInvariant());
}

/// <summary>
/// One asset from <c>/v3.0/asrm/attackSurfaceDevices</c>, reduced to what NetRisk maps
/// (Track 4 milestone 4.4.2).
/// </summary>
public class TrendMicroDevice
{
    /// <summary>Vision One's own id — stored as the host's external id, so a resync updates rather than duplicates.</summary>
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Fqdn { get; set; }

    public List<string> IpAddresses { get; set; } = new();

    public List<string> MacAddresses { get; set; } = new();

    public string? OperatingSystem { get; set; }

    public string? OsVersion { get; set; }

    /// <summary>Vision One asset criticality, normalized to 1–5.</summary>
    public int? Criticality { get; set; }

    /// <summary>Vision One cyber risk score, 0–100. Higher is worse.</summary>
    public int? RiskScore { get; set; }

    /// <summary>Risk level label as Vision One words it, kept for the host comment.</summary>
    public string? RiskLevel { get; set; }

    public DateTime? LastSeen { get; set; }

    /// <summary>The primary address: the first IPv4, or the first address of any kind.</summary>
    public string? PrimaryIp =>
        IpAddresses.FirstOrDefault(ip => ip.Count(c => c == '.') == 3) ?? IpAddresses.FirstOrDefault();

    public string? PrimaryMac => MacAddresses.FirstOrDefault();
}

/// <summary>
/// One CVE on one device from <c>/v3.0/asrm/vulnerableDevices</c>
/// (Track 4 milestone 4.4.3).
/// </summary>
public class TrendMicroDeviceVulnerability
{
    public string DeviceId { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public string CveId { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public double? CvssScore { get; set; }

    /// <summary>Vision One's severity word, mapped onto NetRisk's scale by the ingestion pipeline.</summary>
    public string? Severity { get; set; }

    /// <summary>EPSS exploit probability, when Vision One supplies it.</summary>
    public double? EpssScore { get; set; }

    public bool ExploitAvailable { get; set; }

    /// <summary>
    /// True when a Trend Micro virtual patch (IPS rule) already covers this CVE on this device.
    ///
    /// The interesting field of the whole integration: a virtual patch is a real compensating control,
    /// and whether it closes the NetRisk finding is a policy decision the connection carries rather
    /// than something the ingestion assumes.
    /// </summary>
    public bool VirtualPatchApplied { get; set; }

    /// <summary>The IPS rule id that provides the virtual patch, recorded in the finding's audit trail.</summary>
    public string? VirtualPatchRuleId { get; set; }

    public DateTime? FirstDetected { get; set; }

    public DateTime? LastDetected { get; set; }
}

/// <summary>What one Vision One or SecurityScorecard sync run did (Track 4 milestones 4.4 and 4.5).</summary>
public class PostureSyncResult
{
    public int HostsCreated { get; set; }

    public int HostsUpdated { get; set; }

    public int FindingsCreated { get; set; }

    public int FindingsUpdated { get; set; }

    public int VirtualPatchesApplied { get; set; }

    /// <summary>Factor/score rows written for the trend history.</summary>
    public int PostureRowsWritten { get; set; }

    /// <summary>The entity-wide index this run computed, when it computed one.</summary>
    public double? CyberRiskIndex { get; set; }

    public int Errors { get; set; }

    public List<string> Messages { get; set; } = new();

    /// <summary>The scan-import row the ingested findings were recorded under, when there was one.</summary>
    public int? ImportId { get; set; }
}
