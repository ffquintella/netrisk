namespace DAL.Entities;

/// <summary>
/// The entity-wide posture columns Track 4 adds (milestones 4.4.4 and 4.5.2): the aggregated Cyber
/// Risk Index and the external rating that feeds it.
///
/// On <c>entities</c> rather than in the <c>entities_properties</c> bag because these are read by
/// dashboards and ordered by, and a property-bag row cannot be indexed or sorted on without a join
/// per entity.
/// </summary>
public partial class Entity
{
    /// <summary>
    /// 0–100 aggregate cyber risk for this entity. Higher is worse, consistent with the vendor scores
    /// it aggregates. Null until a posture sync has run — an entity with no data must not read as a
    /// perfect score.
    /// </summary>
    public double? CyberRiskIndex { get; set; }

    /// <summary>Letter grade from the external rating provider, when there is one.</summary>
    public string? PostureGrade { get; set; }

    /// <summary>Which integration last wrote the index, so the number is attributable.</summary>
    public string? PostureSource { get; set; }

    public DateTime? PostureUpdatedAt { get; set; }
}
