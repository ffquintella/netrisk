using System;

namespace DAL.Entities;

/// <summary>
/// The monetary tolerances of a risk appetite — per scenario and for the portfolio, on E[L], P95 and CVaR95 — that
/// Gate B compares the tail statistics against (Stage 9.7, S48 §4.7). One row per appetite.
///
/// A table of its own rather than columns on <c>risk_appetites</c>: the desktop saves the appetite as a whole entity,
/// and a client that does not know a column would erase it (S48 D1). An appetite row with no tail limits does not
/// inherit the organization-wide ones (S48 D8).
/// </summary>
public class RiskAppetiteTailLimit
{
    public int Id { get; set; }

    public int AppetiteId { get; set; }

    public decimal? MaxScenarioExpectedLoss { get; set; }

    public decimal? MaxScenarioP95 { get; set; }

    public decimal? MaxScenarioCvar95 { get; set; }

    public decimal? MaxPortfolioExpectedLoss { get; set; }

    public decimal? MaxPortfolioP95 { get; set; }

    public decimal? MaxPortfolioCvar95 { get; set; }

    /// <summary>The Phase 0 decision that set these limits.</summary>
    public string Rationale { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual RiskAppetite Appetite { get; set; } = null!;

    public virtual User? UpdatedBy { get; set; }
}
