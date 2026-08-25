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

    public DateTime? LastSyncAt { get; set; }

    public IntegrationSyncStatus? LastSyncStatus { get; set; }

    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }
}
