using System;

namespace DAL.Entities;

/// <summary>
/// The target risk level of the register (Stage 9.6, S47 §4.3) — MIGR-TI/IA Phase 0's third level, "after the
/// planned treatment, within appetite", beside the inherent and the residual. On the 0–10 scale the residual
/// and the appetite use, and/or as an annual expected loss.
///
/// A table of its own rather than a column on <c>risks</c>, because <c>PUT /Risks/{id}</c> copies the whole
/// payload (S46 D1, S47 D1). A target above the appetite is recorded and flagged, not refused (S47 D9).
/// </summary>
public class RiskTarget
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    /// <summary>0–10, the scale of the residual score.</summary>
    public decimal? TargetScore { get; set; }

    /// <summary>Annual expected loss, in the unit of the FAIR inputs.</summary>
    public decimal? TargetExpectedLoss { get; set; }

    public DateOnly? TargetDate { get; set; }

    public string Rationale { get; set; } = null!;

    public int? SetById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? SetBy { get; set; }
}
