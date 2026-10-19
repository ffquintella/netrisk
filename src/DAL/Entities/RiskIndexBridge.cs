using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The bridge of a profile change (S39 §9.5; S40 §6.4, §7.2) — <c>risk_index_bridges</c>: on the first snapshot day of a
/// newly active profile, each node and category under both versions (<c>Δ_versão = value_new − value_old</c>, the
/// <c>A_cfg</c> of that day's "what moved") and the new version replayed over the stored days of the last 90 (the overlay).
/// Insert-only, written by the snapshot job in the day's transaction, never audited. Entity-scoped on
/// <see cref="EntityId"/> (S40 §7.4); no entity foreign key: the row is history.
/// </summary>
public class RiskIndexBridge : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The profile that governed the day before.</summary>
    public int FromProfileId { get; set; }

    /// <summary>The newly active profile.</summary>
    public int ToProfileId { get; set; }

    /// <summary>The first snapshot day of <see cref="ToProfileId"/>.</summary>
    public DateOnly BridgeDate { get; set; }

    public RiskIndexScopeKind ScopeKind { get; set; }

    public string ScopeRef { get; set; } = null!;

    /// <summary>The node's entity, for the scope filter; null for the root, asset-class and source nodes.</summary>
    public int? EntityId { get; set; }

    public RiskIndexCategory Category { get; set; }

    /// <summary>The node's value on the bridge day under the previous profile (ICR* or C_k).</summary>
    public double ValueOld { get; set; }

    /// <summary>The node's value on the bridge day under the new profile.</summary>
    public double ValueNew { get; set; }

    /// <summary>JSON: the new profile replayed over the stored days of the last 90 — <c>[{"date","value"}]</c>.</summary>
    public string Overlay { get; set; } = null!;

    /// <summary>The new profile changes what the stored rows cannot replay (anchors, multipliers, sources…): "partial".</summary>
    public bool OverlayPartial { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile FromProfile { get; set; } = null!;

    public virtual RiskIndexProfile ToProfile { get; set; } = null!;
}
