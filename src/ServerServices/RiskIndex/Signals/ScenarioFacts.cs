using DAL.Enums;

namespace ServerServices.RiskIndex.Signals;

/// <summary>A management review of a risk, with what the audit trail says about its creation (S39 §6.1).</summary>
public sealed record ReviewFact
{
    public int Id { get; init; }
    public int ReviewerId { get; init; }

    /// <summary>The date the client sent (<c>POST /MgmtReviews</c> stores it as given, D-07).</summary>
    public DateTime SubmissionDate { get; init; }

    /// <summary>When <c>audit_logs</c> recorded the row's creation, and that save's correlation id; null without a row.</summary>
    public DateTime? AuditedAt { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>The same save created a risk acceptance, or recorded a campaign decision (shared correlation id).</summary>
    public bool SavedWithAcceptance { get; init; }

    public bool SavedWithCampaignDecision { get; init; }

    public bool RequiresCountersignature { get; init; }
    public int? SecondReviewerId { get; init; }
}

/// <summary>A risk acceptance, any status (S39 §6.1–§6.3).</summary>
public sealed record AcceptanceFact
{
    public int Id { get; init; }
    public RiskAcceptanceStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public int? RenewedFromId { get; init; }
    public double? ResidualScoreSnapshot { get; init; }
    public int? CreatedById { get; init; }
    public int? RequestedById { get; init; }
    public int AuthorizingManagerId { get; init; }

    /// <summary><c>fim(a) = RevokedAt ?? ExpiresAt</c>.</summary>
    public DateTime End => RevokedAt ?? ExpiresAt;
}

/// <summary>A campaign item of a risk, with its campaign (S39 §6.3 GOV.C, §6.5).</summary>
public sealed record CampaignItemFact
{
    public int Id { get; init; }
    public int CampaignId { get; init; }
    public RiskReviewCampaignStatus CampaignStatus { get; init; }
    public DateTime CampaignDueDate { get; init; }
    public DateTime CampaignCreatedAt { get; init; }
    public RiskReviewDecision Decision { get; init; }
    public DateTime? DecidedAt { get; init; }
    public int? DecidedById { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A mitigation task of a risk (S39 §6.3 GOV.T, APT-BRK, CMP-*).</summary>
public sealed record TaskFact
{
    public int Id { get; init; }
    public MitigationTaskStatus Status { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>The campaign rank that weights a scenario (S39 §6.5): its rank in the latest completed campaign of its
/// entity, and how many items that campaign ranked.</summary>
public sealed record CampaignRankFact(int Rank, int RankedItems, DateTime CompletedAt);

/// <summary>
/// Everything the index reads about one open scenario from the register, from reliable fields only (S39 §6): never
/// <c>MgmtReview.Review</c>, <c>NextStep</c> or <c>NextReview</c>, and never the raw <c>ReviewRequested</c> flag (§6.7).
/// </summary>
public sealed record ScenarioFacts
{
    public int RiskId { get; init; }
    public DateTime SubmissionDate { get; init; }
    public int? SubmittedBy { get; init; }
    public int? Owner { get; init; }
    public int? Manager { get; init; }

    /// <summary><c>risks.entity_id</c>: the scope entity, and the one appetite and campaigns key on.</summary>
    public int? EntityId { get; init; }

    /// <summary>The inherent score <c>CalculatedRisk</c> and the stored residual (0–10).</summary>
    public double Inherent { get; init; }

    public double? Residual { get; init; }
    public DateTime? ResidualUpdatedAt { get; init; }
    public int ScoringMethod { get; init; }

    /// <summary>The impact level 1–5 (<c>ClassicImpact</c>), or null when undefined (S39 §5.4: read as 3, context missing).</summary>
    public int? ImpactLevel { get; init; }

    public DateTime? ReviewRequestedAt { get; init; }
    public string? ReviewRequestedReason { get; init; }

    /// <summary>The request was written by an acceptance's expiry or revocation (audit correlation) — REV-REQ ignores it.</summary>
    public bool ReviewRequestFromAcceptance { get; init; }

    public IReadOnlyList<ReviewFact> Reviews { get; init; } = [];
    public IReadOnlyList<AcceptanceFact> Acceptances { get; init; } = [];
    public IReadOnlyList<CampaignItemFact> CampaignItems { get; init; } = [];
    public IReadOnlyList<TaskFact> Tasks { get; init; } = [];

    /// <summary><c>MaxAcceptableResidual</c> of the entity's appetite row, else the global one; null when none is configured.</summary>
    public double? AppetiteCeiling { get; init; }

    public CampaignRankFact? Rank { get; init; }

    /// <summary>A plan is linked (<c>Risk.IncidentResponsePlanId</c>) and approved, and when it was last tested.</summary>
    public bool ApprovedResponsePlan { get; init; }

    public DateTime? ResponsePlanLastTest { get; init; }

    /// <summary>The hosts linked to the scenario through its findings (<c>risks_to_vulnerabilities</c>), for GOV.G.</summary>
    public IReadOnlyList<int> LinkedHostIds { get; init; } = [];
}
