using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One EPSS reading for one CVE from one source (Stage 9.4, S45 §4.2), table <c>epss_scores</c>. Both
/// sources are kept side by side so the reading that lost the convergence stays visible.
///
/// Public data about a CVE, not about a finding, so it carries no entity scope.
/// </summary>
public class EpssScore
{
    public int Id { get; set; }

    public string CveId { get; set; } = null!;

    public EpssSource Source { get; set; }

    public double Score { get; set; }

    public double? Percentile { get; set; }

    public DateTime AsOf { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Only when the score, percentile or date actually changed.</summary>
    public DateTime? UpdatedAt { get; set; }
}
