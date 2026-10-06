using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One link from a risk to one node of the linkage chain (Stage 9.1, S41 §4.3): a strategic
/// objective, a business process, an IT service, a piece of data, or an asset — an application entity
/// or a host row.
///
/// A table of its own rather than more rows in <c>risk_to_entity</c>: the legacy
/// <c>PUT /Risks/{id}/Entity</c> deletes every row of that table for the risk before writing one, the
/// table cannot hold a host, and it records neither a level nor where the link came from (S41 §11,
/// D2).
///
/// Exactly one of <see cref="EntityId"/> and <see cref="HostId"/> is set. The invariant is held by the
/// service, by <c>ck_risk_chain_links_one_target</c> in the schema and in the EF model, and by
/// <c>Track9RiskChainSchemaTests</c> against a real MariaDB.
///
/// <c>risks.entity_id</c> is untouched by any of this: it is the <b>scope</b> column (who may see the
/// risk, which appetite applies, which campaign reviews it), and the chain is identification.
/// </summary>
public class RiskChainLink
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    /// <summary>Derived from the target when the link is written; never from a payload.</summary>
    public RiskChainLevel ChainLevel { get; set; }

    /// <summary>The entity target, when the link points at an entity of a chain type.</summary>
    public int? EntityId { get; set; }

    /// <summary>The host target, when the link points at a host (level <see cref="RiskChainLevel.Asset"/>).</summary>
    public int? HostId { get; set; }

    public RiskChainLinkOrigin Origin { get; set; } = RiskChainLinkOrigin.Declared;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The user who declared the link. Null for rows copied or mirrored from <c>risk_to_entity</c>.</summary>
    public int? CreatedById { get; set; }

    /// <summary>UTC. Set only when the origin changes — a promotion to Declared or a demotion back to Legacy.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual Entity? Entity { get; set; }

    public virtual Host? Host { get; set; }

    public virtual User? CreatedBy { get; set; }
}
