using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A Trend Micro Vision One tenant connection (Track 4 milestone 4.4.1).
///
/// The base URL is per connection because Vision One is regional — <c>api.xdr.trendmicro.com</c>,
/// <c>api.eu.xdr.trendmicro.com</c>, <c>api.au.xdr.trendmicro.com</c> — and a token issued in one
/// region is rejected by the others. Hard-coding a single host is the classic way this integration
/// works for exactly one customer.
/// </summary>
public class TrendMicroConnection
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Region code shown in the UI (<c>us</c>, <c>eu</c>, <c>jp</c>, …), for the picker.</summary>
    public string Region { get; set; } = null!;

    /// <summary>Regional API root, derived from <see cref="Region"/> but overridable.</summary>
    public string BaseUrl { get; set; } = null!;

    /// <summary>Encrypted Vision One API key, presented as <c>Authorization: Bearer</c>.</summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>Business entity the synchronized hosts and findings belong to.</summary>
    public int? EntityId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>How often the sync job runs. Daily by default, per the spec.</summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Ingest CVEs from at-risk devices as NetRisk findings. Separable from inventory sync: several
    /// deployments want the asset inventory and keep vulnerability data in their own scanner.
    /// </summary>
    public bool SyncVulnerabilities { get; set; } = true;

    /// <summary>Sync device risk scores and roll them into the entity's Cyber Risk Index.</summary>
    public bool SyncRiskScores { get; set; } = true;

    /// <summary>
    /// Treat a CVE that Vision One reports as covered by a virtual patch as mitigated rather than
    /// active. Opt-in: a virtual patch is a compensating control, and whether that closes the finding
    /// is a policy decision, not ours.
    /// </summary>
    public bool VirtualPatchClosesFinding { get; set; }

    /// <summary>
    /// Push asset criticality and acceptance-derived exemptions back to Vision One. Off by default —
    /// writing into somebody's EDR console is not something an integration should start doing on its
    /// own.
    /// </summary>
    public bool PushExemptions { get; set; }

    /// <summary>
    /// Read each device's <c>assetCustomTags</c>, keep them in <c>host_external_ids.tags</c> and apply the connection's
    /// tag → entity rules (<c>vendor_entity_mappings</c>; S40 §6.6, §6.9, §8.8 <c>SyncCustomTags</c>; T263). Off by default:
    /// a rule moves a host to another entity, and with it who sees the host, so the operator opts in.
    /// </summary>
    public bool SyncCustomTags { get; set; }

    /// <summary>
    /// Read <c>securityPosture</c> and <c>assetGroups</c> into <c>vendor_scores</c> as reference indicators (V1Cri,
    /// V1SecurityConfig, V1AssetGroup; M57, T271, S40 §6.9). Off by default: the posture needs CREM credits and the
    /// "Third-party auditing (API only)" permission.
    /// </summary>
    public bool SyncPosture { get; set; }

    /// <summary>
    /// Read the internet-facing FQDNs and IPs, domain and service accounts, cloud assets and local apps into
    /// <c>vendor_assets</c> as scored objects (T272). Off by default: these endpoints spend CREM credits.
    /// </summary>
    public bool SyncAttackSurfaceObjects { get; set; }

    /// <summary>
    /// Count the last 30 days of Workbench alerts per day and severity as the attack-intensity indicator (V1AttackIntensity;
    /// T273). Never a scored factor. Off by default: it needs the Workbench permission.
    /// </summary>
    public bool SyncWorkbenchAlerts { get; set; }

    /// <summary>
    /// When the scheduled sync is next due (M57, T276, S39 D-13): the start of the next interval slot, never the end of the
    /// last run, so a 24-hour interval syncs every day. A manual sync does not move it.
    /// </summary>
    public DateTime? NextDueAt { get; set; }

    /// <summary>
    /// Whether the inventory sync moves a matched host that already has an entity onto <see cref="EntityId"/> (M58, T282,
    /// S39 D-20). <see cref="RestampPolicy.OnlyUnassigned"/> (default) stamps only hosts with none;
    /// <see cref="RestampPolicy.FollowMapping"/> also re-stamps the rest, never over a higher-ranked
    /// <see cref="Host.EntitySource"/> (a manual, CMDB or tag-rule assignment). A winning tag rule is applied instead.
    /// </summary>
    public RestampPolicy EntityRestampPolicy { get; set; } = RestampPolicy.OnlyUnassigned;

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }
}
