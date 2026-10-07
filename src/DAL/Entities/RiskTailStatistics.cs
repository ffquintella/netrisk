using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The tail statistics of one Monte Carlo run of a risk — E[L], P95 and CVaR95 with their 95 % confidence intervals,
/// the annual probability of a loss and the mean loss of a loss year (Stage 9.7, S48 §4.2–4.3). One row per risk and
/// run, written by <c>QuantitativeRiskService</c> in the same save as <c>risk_scoring</c>.
///
/// It carries a copy of the inputs it simulated (frequency, magnitude, effectiveness, seed, iterations) so the
/// portfolio aggregation reproduces this exact run even after a payload copy through <c>PUT /Risks/{id}/Scoring</c>
/// changed <c>risk_scoring.quant_*</c> (S48 D1).
/// </summary>
public class RiskTailStatistics
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public TailRun Run { get; set; }

    public int Iterations { get; set; }

    public int Seed { get; set; }

    /// <summary>The confidence level of the intervals — 0.950.</summary>
    public decimal ConfidenceLevel { get; set; }

    public double LefMin { get; set; }

    public double LefMostLikely { get; set; }

    public double LefMax { get; set; }

    /// <summary>The per-event magnitude range simulated — the single range, or the envelope of the components.</summary>
    public double MagnitudeMin { get; set; }

    public double MagnitudeMostLikely { get; set; }

    public double MagnitudeMax { get; set; }

    public MagnitudeSource MagnitudeSource { get; set; }

    /// <summary>0 for the inherent run; the mitigation's effectiveness (0–1) for the residual one.</summary>
    public double MitigationEffectiveness { get; set; }

    public double ExpectedLoss { get; set; }

    public double ExpectedLossCiLow { get; set; }

    public double ExpectedLossCiHigh { get; set; }

    public double P95 { get; set; }

    public double P95CiLow { get; set; }

    public double P95CiHigh { get; set; }

    public double Cvar95 { get; set; }

    public double Cvar95CiLow { get; set; }

    public double Cvar95CiHigh { get; set; }

    /// <summary>The fraction of simulated years with any loss.</summary>
    public double ProbabilityOfLoss { get; set; }

    /// <summary>E[L | L &gt; 0] — null when no simulated year had a loss.</summary>
    public double? ConditionalLoss { get; set; }

    /// <summary>UTC — when the run was simulated.</summary>
    public DateTime ComputedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    /// <summary>The components simulated and their contributions; empty for a single-range run.</summary>
    public virtual ICollection<RiskTailComponent> Components { get; set; } = new List<RiskTailComponent>();
}
