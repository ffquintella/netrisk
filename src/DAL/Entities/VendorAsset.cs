using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A vendor asset that is not a host (S40 §7.2 <c>vendor_assets</c>; M57, T272): a Vision One internet-facing FQDN or public
/// IP, a domain or service account, a cloud asset or a local app. The index scores it as an object of its own (S39 §3.2
/// V1-IFA, V1-ACC, V1-CLD, V1-APP); its readings are <c>vendor_scores</c> rows with <see cref="VendorScoreSource.V1VendorAsset"/>.
/// Filtered on <see cref="EntityId"/> (S40 §7.4); not audited, since a sync rewrites it daily (§7.5).
/// </summary>
public class VendorAsset : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The integration that reports the asset (<c>IntegrationKind</c>).</summary>
    public IntegrationKind Provider { get; set; }

    /// <summary>The connection that reported it, in its own provider's table (not a foreign key).</summary>
    public int ConnectionId { get; set; }

    public VendorAssetKind AssetKind { get; set; }

    /// <summary>The vendor's id of the asset (unique per provider, connection and kind).</summary>
    public string ExternalId { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>The vendor's criticality normalized to 1–5, when it gave one; the index caps a Vision One value (S39 §5.1).</summary>
    public int? Criticality { get; set; }

    /// <summary>The criticality as the vendor spelled it (<c>high</c>, <c>80</c>), kept for the drill-down.</summary>
    public string? CriticalityRaw { get; set; }

    /// <summary>The connection's default entity; a scoped user sees the asset only inside it.</summary>
    public int? EntityId { get; set; }

    /// <summary>The vendor's tags, a JSON array of canonical strings; null when none.</summary>
    public string? Tags { get; set; }

    /// <summary>The vendor's first sighting when it gave one, else NetRisk's first read.</summary>
    public DateTime FirstSeenAt { get; set; }

    /// <summary>The vendor's last detection when it gave one, else the last sync that read the asset.</summary>
    public DateTime LastSeenAt { get; set; }

    public VendorAssetStatus Status { get; set; } = VendorAssetStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity? Entity { get; set; }
}
