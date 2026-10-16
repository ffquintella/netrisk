using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One entity a pre-computed entity node held on one day (S40 §7.2, §9.3) — <c>risk_index_node_members</c>: the closure
/// entities, and the scope entities of every object attributed to the node. The official read of a node is served only
/// when the reader's claims cover all of them (S40 §11.3); a node with no member rows is refused to a scoped reader.
/// Insert-only, kept as long as the snapshot it authorizes (1825 days). Not scope-filtered: it is the map the claims are
/// compared with, global configuration like the entity map itself (S40 §7.4).
/// </summary>
public class RiskIndexNodeMember
{
    public long Id { get; set; }

    /// <summary>The UTC day of the snapshot the member belongs to.</summary>
    public DateOnly SnapshotDate { get; set; }

    public int ProfileId { get; set; }

    /// <summary>The node: an entity id (<c>risk_index_snapshots.entity_id</c> of its rows). No foreign key: history.</summary>
    public int NodeEntityId { get; set; }

    public RiskIndexNodeMemberKind MemberKind { get; set; }

    /// <summary>
    /// The member entity. Null only for <see cref="RiskIndexNodeMemberKind.Scope"/>: some attributed object's source row
    /// has no scope entity, so only an unrestricted reader may have the node's official snapshot.
    /// </summary>
    public int? MemberEntityId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;
}
