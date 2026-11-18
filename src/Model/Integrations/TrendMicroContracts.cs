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

    /// <summary>
    /// The vault reference this connection's API key resolves through, or null when the key is stored
    /// literally. Safe to return: it names a secret rather than being one, and it is what lets the
    /// desktop client show "Prod vault: tm-api" beside the field instead of a row of bullets.
    /// </summary>
    public string? ApiKeyVaultReference { get; set; }

    public int? EntityId { get; set; }

    public bool Enabled { get; set; }

    public int SyncIntervalHours { get; set; }

    public bool SyncVulnerabilities { get; set; }

    public bool SyncRiskScores { get; set; }

    public bool VirtualPatchClosesFinding { get; set; }

    public bool PushExemptions { get; set; }

    /// <summary>Collect the devices' custom tags and apply the tag → entity rules (T263, S40 §6.9, §8.8).</summary>
    public bool SyncCustomTags { get; set; }

    /// <summary>Read <c>securityPosture</c> and <c>assetGroups</c> as reference indicators (M57, T271, S40 §6.9, §8.8).</summary>
    public bool SyncPosture { get; set; }

    /// <summary>Read the internet-facing, account, cloud and local-app assets as scored objects (T272).</summary>
    public bool SyncAttackSurfaceObjects { get; set; }

    /// <summary>Count Workbench alerts as the attack-intensity indicator (T273).</summary>
    public bool SyncWorkbenchAlerts { get; set; }

    /// <summary>When the scheduled sync is next due (T276); null until the first scheduled run.</summary>
    public DateTime? NextDueAt { get; set; }

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

    /// <summary>
    /// The device's last detection (<c>lastDetectDateTime</c>, the OAS name; M57, T275, S39 D-11). The candidates read before
    /// (<c>lastUsedIp</c>, <c>lastActivity</c>) are not in the OAS of <c>attackSurfaceDevices</c>.
    /// </summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>The device's first sighting (<c>firstSeenDateTime</c>; T275).</summary>
    public DateTime? FirstSeen { get; set; }

    /// <summary>
    /// The device's custom asset tags (<c>assetCustomTags</c>) in canonical <c>key: value</c> form (T263, S40 §6.6): what
    /// the tag → entity rules match.
    /// </summary>
    public List<string> Tags { get; set; } = new();

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

    /// <summary>Exploit attempts seen in the tenant (<c>exploitAttemptCount</c>), kept apart from the global activity (T275, D-11).</summary>
    public int? ExploitAttemptCount { get; set; }

    /// <summary><c>globalExploitActivityLevel</c> as Vision One sent it (<c>high</c> = actively exploited in the wild; T275).</summary>
    public string? GlobalExploitActivity { get; set; }

    /// <summary>
    /// Vision One's mitigation status for this CVE on this device: <c>new</c>, <c>inProgress</c>,
    /// <c>accepted</c> or <c>mitigated</c>. Recorded on the finding so a triager can see that Trend
    /// already has an opinion about it. <c>closed</c> (the console's "Remediated") and
    /// <c>dismissed</c> never reach here — the client drops those rather than importing a finding
    /// nothing would close.
    /// </summary>
    public string? MitigationStatus { get; set; }

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

    /// <summary>
    /// Legacy: no sync computes an entity-wide index since M57, T270 (S39 D-09), so this is always null; kept so older
    /// clients that read it show "—" instead of failing.
    /// </summary>
    public double? CyberRiskIndex { get; set; }

    public int Errors { get; set; }

    public List<string> Messages { get; set; } = new();

    /// <summary>The scan-import row the ingested findings were recorded under, when there was one.</summary>
    public int? ImportId { get; set; }

    /// <summary>Hosts with no entity that a tag → entity rule attributed (T263, S40 §6.6).</summary>
    public int HostsAssignedByRule { get; set; }

    /// <summary>Hosts a <c>FollowMapping</c> rule moved from another entity (T263, S40 §6.6).</summary>
    public int HostsReassignedByRule { get; set; }

    /// <summary>Devices whose tags were recorded in <c>host_external_ids</c> (T263).</summary>
    public int TagsRecorded { get; set; }

    /// <summary>Reference-indicator readings written: tenant CRI, configuration adoption, asset groups, attack intensity (T271, T273).</summary>
    public int IndicatorsWritten { get; set; }

    /// <summary>Vendor assets read from the attack-surface endpoints (T272).</summary>
    public int VendorAssetsSynced { get; set; }

    /// <summary>Vendor assets a complete read no longer listed, now <c>Gone</c> (T272).</summary>
    public int VendorAssetsGone { get; set; }

    /// <summary>Workbench alerts counted in the last 30 days (T273).</summary>
    public int WorkbenchAlerts { get; set; }

    /// <summary>SecurityScorecard score rows recovered from the history endpoints (T274).</summary>
    public int HistoryRowsBackfilled { get; set; }

    /// <summary>SecurityScorecard per-issue-type impact rows written (T274).</summary>
    public int IssueSummariesWritten { get; set; }
}

/// <summary>Vision One's <c>securityPosture</c> reduced to what NetRisk keeps (M57, T271, S40 §9.5).</summary>
public class TrendMicroSecurityPosture
{
    /// <summary>The tenant's Cyber Risk Index, 0–100; null when the payload carried none.</summary>
    public double? RiskIndex { get; set; }

    public string? RiskLevel { get; set; }

    /// <summary>The per-category levels and the other indicators of the payload, as JSON, kept in <c>details</c>.</summary>
    public string? CategoriesJson { get; set; }

    /// <summary>Each agent feature's adoption, 0–1 (<c>agentFeatureStatus[].adoptionRate</c>).</summary>
    public List<(string Feature, double AdoptionRate)> FeatureAdoption { get; set; } = new();

    /// <summary>The mean adoption, 0–1, or null when no feature reported one (V1-SCS reads <c>1 − adoption</c>).</summary>
    public double? MeanAdoption => FeatureAdoption.Count == 0 ? null : FeatureAdoption.Average(f => f.AdoptionRate);
}

/// <summary>One Vision One asset group (<c>/v3.0/asrm/assetGroups</c>; T271).</summary>
public class TrendMicroAssetGroup
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>The group's risk index, 0–100; null when the payload carried none.</summary>
    public double? RiskScore { get; set; }

    public string? RiskLevel { get; set; }

    public int? AssetCount { get; set; }
}

/// <summary>One internet-facing FQDN or IP, account, cloud asset or local app (T272).</summary>
public class TrendMicroAttackSurfaceAsset
{
    public VendorAssetKind Kind { get; set; }

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Vision One risk score, 0–100; null when the asset carried none.</summary>
    public double? RiskScore { get; set; }

    public string? RiskLevel { get; set; }

    /// <summary>Asset criticality normalized to 1–5, as for a device.</summary>
    public int? Criticality { get; set; }

    public string? CriticalityRaw { get; set; }

    public DateTime? FirstSeen { get; set; }

    public DateTime? LastDetected { get; set; }

    public List<string> Tags { get; set; } = new();
}

/// <summary>One Workbench alert, reduced to what the attack-intensity count needs (T273).</summary>
public class TrendMicroWorkbenchAlert
{
    public string Id { get; set; } = string.Empty;

    public string? Severity { get; set; }

    public DateTime CreatedAt { get; set; }
}
