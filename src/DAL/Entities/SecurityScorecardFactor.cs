namespace DAL.Entities;

/// <summary>
/// One factor score at one point in time (Track 4 milestone 4.5.2) — Network Security, Patching
/// Cadence, DNS Health, and the rest of the ten.
///
/// Append-only: a row per capture rather than an updated current value, because the point of syncing
/// factor scores is the trend. Overwriting yesterday's Patching Cadence leaves you knowing the score
/// and not whether it is getting worse.
/// </summary>
public class SecurityScorecardFactor
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    public int? EntityId { get; set; }

    /// <summary>The factor's machine name as SecurityScorecard reports it (<c>patching_cadence</c>).</summary>
    public string FactorName { get; set; } = null!;

    /// <summary>0–100.</summary>
    public int Score { get; set; }

    /// <summary>Letter grade A–F, when the API supplies one for the factor.</summary>
    public string? Grade { get; set; }

    /// <summary>Number of active issues contributing to the factor, when reported.</summary>
    public int? IssueCount { get; set; }

    /// <summary>
    /// True for the synthetic row carrying the company's overall score and grade, so the whole
    /// posture history is one query rather than a join across two shapes.
    /// </summary>
    public bool IsOverall { get; set; }

    public DateTime CapturedAt { get; set; }

    public virtual SecurityScorecardConnection? Connection { get; set; }

    public virtual Entity? Entity { get; set; }
}
