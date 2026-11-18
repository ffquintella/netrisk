namespace DAL.Entities;

/// <summary>
/// One issue type's contribution to a SecurityScorecard factor at one capture (S40 §7.2
/// <c>security_scorecard_issue_summaries</c>; M57, T274): the <c>issue_summary[]</c> entry of <c>/factors</c>, kept so the
/// drill-down can say which issue types cost the factor its points. Never a scored input (S39 §3.2 SSC-ISS).
/// </summary>
public class SecurityScorecardIssueSummary
{
    public int Id { get; set; }

    public int FactorId { get; set; }

    /// <summary>The issue type's machine name (<c>patching_cadence_high</c>).</summary>
    public string IssueType { get; set; } = null!;

    public string? Severity { get; set; }

    /// <summary>How many issues of the type the factor carries; null when SecurityScorecard did not say.</summary>
    public int? IssueCount { get; set; }

    /// <summary>The points the issue type costs the factor; null when SecurityScorecard did not say — never 0 for "unknown".</summary>
    public double? TotalScoreImpact { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual SecurityScorecardFactor Factor { get; set; } = null!;
}
