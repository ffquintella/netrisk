using System;

namespace DAL.Entities;

/// <summary>
/// A declared correlation between the annual losses of two risk scenarios (Stage 9.7, S48 §4.5) — the input of the
/// Gaussian-copula portfolio aggregation. The pair is stored normalized (<c>risk_a_id &lt; risk_b_id</c>), the
/// coefficient is 0–1 (S48 D5), and a write that would make the correlation matrix of its connected group not
/// positive semidefinite is refused, never repaired (S48 D6). An undeclared pair is independent.
/// </summary>
public class RiskCorrelation
{
    public int Id { get; set; }

    public int RiskAId { get; set; }

    public int RiskBId { get; set; }

    /// <summary>0–1, three decimals.</summary>
    public decimal Coefficient { get; set; }

    public string Rationale { get; set; } = null!;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Risk RiskA { get; set; } = null!;

    public virtual Risk RiskB { get; set; } = null!;

    public virtual User? UpdatedBy { get; set; }
}
