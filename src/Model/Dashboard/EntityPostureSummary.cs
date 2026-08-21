namespace Model.Dashboard;

/// <summary>
/// Posture rollup for a single business entity, as shown on one card of the Master Dashboard
/// (Track 2 milestone 2.3.3). Every count here is already scoped to <see cref="EntityId"/>.
/// </summary>
public class EntityPostureSummary
{
    /// <summary>
    /// The business entity these numbers belong to, or <c>null</c> for the synthetic
    /// "unassigned" bucket that collects records whose <c>entity_id</c> is still null
    /// (the 2.3.1 backfill leaves the column nullable, so this bucket is expected).
    /// </summary>
    public int? EntityId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    /// <summary>Entity lifecycle status, so the GUI can de-emphasise retired entities.</summary>
    public string? EntityStatus { get; set; }

    public int OpenRisks { get; set; }
    public int RisksHigh { get; set; }
    public int RisksMedium { get; set; }
    public int RisksLow { get; set; }

    /// <summary>Mean <c>CalculatedRisk</c> over this entity's open, scored risks. 0 when it has none.</summary>
    public double AverageRiskScore { get; set; }

    public int OpenVulnerabilities { get; set; }
    public int VulnerabilitiesCritical { get; set; }
    public int VulnerabilitiesHigh { get; set; }
    public int VulnerabilitiesMedium { get; set; }
    public int VulnerabilitiesLow { get; set; }

    public int OpenIncidents { get; set; }

    /// <summary>
    /// Composite 0–100 posture indicator, higher meaning worse. Deliberately a triage heuristic
    /// for ordering the cards — not a quantified risk figure (that is Track 8.7's FAIR-lite work).
    /// </summary>
    public double PostureScore { get; set; }
}
