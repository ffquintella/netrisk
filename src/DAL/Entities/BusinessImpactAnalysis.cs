using System;

namespace DAL.Entities;

/// <summary>
/// The business impact analysis of one business process or IT service (Stage 9.3, S43 §4.1): its
/// MTPD/MAO, RTO and RPO, in whole minutes.
///
/// A table of its own rather than entity properties: these values set process criticality and the
/// continuity basis of flag 4, so they need their own permission, a validated range and the audit
/// trail, none of which an EAV property saved through <c>PUT /Entities</c> has (S43 §11, D1).
///
/// <c>NULL</c> means "not declared" and <c>0</c> is a value (RPO 0 is synchronous replication). A
/// process with no row has no BIA — it is absent, never RTO 0 and never infinite. At least one of the
/// three objectives is set (<c>ck_business_impact_analyses_declared</c>), and the RTO never exceeds the
/// MTPD on the same row (<c>ck_business_impact_analyses_rto_within_mtpd</c>).
/// </summary>
public class BusinessImpactAnalysis
{
    public int Id { get; set; }

    /// <summary>The <c>businessProcess</c> or <c>itService</c> entity; one BIA per node.</summary>
    public int EntityId { get; set; }

    /// <summary>MTPD/MAO — the same concept under the ISO 22301 and BCI names (S43 §11, D3).</summary>
    public int? MtpdMinutes { get; set; }

    public int? RtoMinutes { get; set; }

    public int? RpoMinutes { get; set; }

    /// <summary>UTC. When the analysis was made, not when the row was last edited.</summary>
    public DateTime AssessedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }
}
