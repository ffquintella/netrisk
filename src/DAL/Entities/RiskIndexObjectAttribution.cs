using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One entity an object was attributed to on one day (S39 §10.2; S40 §7.2) — <c>risk_index_object_attributions</c>: what a
/// derived node (an activity, "Unassigned", an intersection) is recomputed from, without parsing the object rows' JSON.
/// An object attributed to no entity of the map has one row with a null <see cref="AttributedEntityId"/>: the
/// "Unassigned" node. Insert-only, purged after 400 days. Entity-scoped on <see cref="ScopeEntityId"/>, the scope entity
/// of the source row, like the object row — never on the entity it is attributed to.
/// </summary>
public class RiskIndexObjectAttribution : Interfaces.IEntityScoped
{
    public long Id { get; set; }

    /// <summary>The UTC day the row belongs to.</summary>
    public DateOnly SnapshotDate { get; set; }

    public int ProfileId { get; set; }

    public RiskIndexObjectKind ObjectKind { get; set; }

    /// <summary>The object's identity within its kind, as in <c>risk_index_object_days.object_ref</c>.</summary>
    public string ObjectRef { get; set; } = null!;

    /// <summary>The entity of attribution (column <c>entity_id</c>); null files the object under "Unassigned".</summary>
    public int? AttributedEntityId { get; set; }

    /// <summary>The scope entity of the source row (<c>risks.entity_id</c>, <c>hosts.entity_id</c>, …).</summary>
    public int? ScopeEntityId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;

    /// <summary>The write guard reads the scope entity of the source row: the column the scope filter reads.</summary>
    int? Interfaces.IEntityScoped.EntityId
    {
        get => ScopeEntityId;
        set => ScopeEntityId = value;
    }
}
