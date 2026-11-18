using System;

namespace DAL.Entities;

/// <summary>
/// One vendor identity of a host (S40 §7.2 <c>host_external_ids</c>): the provider's id for the asset and what the provider
/// last said about it. T263 writes the Vision One rows with the device's custom tags — the candidates and the preview of
/// the tag → entity rules read them; T267/T275 extend the table into the identity chain and the detection times.
/// Filtered through its host (S40 §7.4), and not audited: a sync rewrites it daily (§7.5).
/// </summary>
public class HostExternalId
{
    public int Id { get; set; }

    public int HostId { get; set; }

    /// <summary>The provider's name as <c>hosts.external_provider</c> spells it (<c>TrendMicroVisionOne</c>).</summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The connection that last reported the asset (beyond S40 §7.2, which keys the row by provider only): the rules are
    /// per connection, so its candidates and preview must read only its own devices.
    /// </summary>
    public int? ConnectionId { get; set; }

    public string ExternalId { get; set; } = null!;

    /// <summary>The vendor's tags, a JSON array of canonical strings (<c>["BU: Escola de Direito"]</c>); null when none.</summary>
    public string? Tags { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Host Host { get; set; } = null!;
}
