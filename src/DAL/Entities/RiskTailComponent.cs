using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One loss component of a tail-statistics run: the range that was simulated (a copy, S48 D1) and its contribution to
/// the run's E[L] and — by Euler allocation — to its CVaR95 (Stage 9.7, S48 §4.4.1). The contributions of a run add
/// up to its E[L] and CVaR95.
/// </summary>
public class RiskTailComponent
{
    public int Id { get; set; }

    public int TailStatisticsId { get; set; }

    public LossComponent Component { get; set; }

    public double LossMin { get; set; }

    public double LossMostLikely { get; set; }

    public double LossMax { get; set; }

    /// <summary>The component's mean annual loss.</summary>
    public double ExpectedLoss { get; set; }

    /// <summary>The component's mean over the iterations in the tail of the total — its share of the CVaR95.</summary>
    public double Cvar95 { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskTailStatistics TailStatistics { get; set; } = null!;
}
