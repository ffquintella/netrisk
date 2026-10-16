using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One sensitivity preview of a draft profile (S39 §13.2; S40 §6.3, §7.2, §9.6) — <c>risk_index_preview_requests</c>:
/// queued by the API, claimed atomically and computed by <c>RiskIndexPreviewJob</c> in the job host, never in the API. The
/// report lives here, never on the profile row, until the submission copies the one whose hash matches to
/// <c>risk_index_profiles.sensitivity_report</c>. Deleting the draft deletes its previews (cascade). Not entity-scoped
/// (configuration); its report is read only with unrestricted scope (S40 §8.4).
/// </summary>
public class RiskIndexPreviewRequest
{
    public int Id { get; set; }

    public int ProfileId { get; set; }

    /// <summary>SHA-256 (hex) of the draft's parameters and mode when requested; a different hash makes the report stale.</summary>
    public string ParametersHash { get; set; } = null!;

    public RiskIndexPreviewStatus Status { get; set; } = RiskIndexPreviewStatus.Queued;

    /// <summary>0–100.</summary>
    public int Progress { get; set; }

    public int? RequestedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>UTC; advanced with every progress step. A Running request silent for 15 minutes is queued again.</summary>
    public DateTime? HeartbeatAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? FinishedAt { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>JSON: the report (<c>Model.RiskIndex.RiskIndexPreviewDto</c> without its status).</summary>
    public string? Report { get; set; }

    public virtual RiskIndexProfile Profile { get; set; } = null!;

    public virtual User? RequestedBy { get; set; }
}
