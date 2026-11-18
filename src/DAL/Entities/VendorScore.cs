using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One vendor reading of an object, piecewise-constant (S40 §7.2 <c>vendor_scores</c>; M56, T267): a new row when the value
/// changes, otherwise only <see cref="LastCapturedAt"/> advances. The current reading is the row with the latest
/// <see cref="LastCapturedAt"/>. T267 writes Tenable ACR/AES per host; M57 adds the Vision One sources (T270–T273).
/// Filtered on <see cref="EntityId"/> (S40 §7.4); not audited (§7.5).
/// </summary>
public class VendorScore : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    public VendorScoreSource Source { get; set; }

    /// <summary>The connection that captured the reading, in its own provider's table (not a foreign key).</summary>
    public int ConnectionId { get; set; }

    public VendorScoreObjectKind ObjectKind { get; set; } = VendorScoreObjectKind.Host;

    public int? HostId { get; set; }

    /// <summary>The vendor asset the reading is about, for <see cref="VendorScoreObjectKind.VendorAsset"/> rows (M57, T272).</summary>
    public long? VendorAssetId { get; set; }

    /// <summary>The vendor's id of the object (a Tenable asset uuid, a Vision One asset group id, <c>tenant</c>).</summary>
    public string? ExternalRef { get; set; }

    public int? EntityId { get; set; }

    public double Value { get; set; }

    /// <summary>The top of the vendor's scale (AES 1000, ACR 10).</summary>
    public double ScaleMax { get; set; }

    public string? Level { get; set; }

    /// <summary>JSON the vendor sent with the reading (for AES and ACR, the asset's last assessment time).</summary>
    public string? Details { get; set; }

    public DateTime FirstCapturedAt { get; set; }

    public DateTime LastCapturedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Host? Host { get; set; }

    public virtual VendorAsset? VendorAsset { get; set; }
}
