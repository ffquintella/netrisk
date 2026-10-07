using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One form of loss of a risk scenario, as a calibrated per-event range (Stage 9.7, S48 §4.1) — the Phase 3
/// decomposition of the loss magnitude into response, recovery, productivity, revenue, liability, fine and
/// reputation. When a risk has components, the Monte Carlo samples each one per event and sums them; the single
/// <c>risk_scoring.quant_loss_*</c> range becomes their envelope.
///
/// A table of its own rather than columns on <c>risk_scoring</c>: <c>PUT /Risks/{id}/Scoring</c> copies the whole
/// scoring payload (S47 R3, S48 D1).
/// </summary>
public class RiskLossComponent
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public LossComponent Component { get; set; }

    /// <summary>Per-event loss of this component, minimum.</summary>
    public double LossMin { get; set; }

    public double LossMostLikely { get; set; }

    public double LossMax { get; set; }

    /// <summary>Where the range comes from. Required for a fine — its legal basis.</summary>
    public string? Basis { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? UpdatedBy { get; set; }
}
