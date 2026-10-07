using DAL.Enums;

namespace Model.RiskFlags;

/// <summary>One flag of one risk, both halves (S46 §4.2). A flag never declared nor derived reads all false.</summary>
public class RiskFlagStateDto
{
    public RiskFlagCode Code { get; set; }

    /// <summary>1–11; null for the Gate A condition "no legitimate acceptance".</summary>
    public int? Number { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Declared or derived.</summary>
    public bool IsSet { get; set; }

    public bool Declared { get; set; }

    public string? DeclaredReason { get; set; }

    public DateTime? DeclaredAt { get; set; }

    public int? DeclaredById { get; set; }

    public bool Derived { get; set; }

    public string? DerivedBasis { get; set; }

    /// <summary>Flag 4 only: the highest continuity-threat weight (1.0 confirmed, the configured weight when unverified).</summary>
    public decimal? DerivedWeight { get; set; }

    public DateTime? DerivedChangedAt { get; set; }

    public string? DerivedNote { get; set; }

    public RiskFlagDerivation Derivation { get; set; }

    public bool NonDiscretionary { get; set; }
}

/// <summary>The Gate A predicate on one risk (S46 §4.7).</summary>
public class GateAEvaluationDto
{
    public bool Holds { get; set; }

    /// <summary>The conditions that are set, in code order.</summary>
    public List<RiskFlagCode> Conditions { get; set; } = new();

    public string Explanation { get; set; } = string.Empty;
}

/// <summary>The flags, Gate A and the decision in force on one risk.</summary>
public class RiskFlagsStateDto
{
    public int RiskId { get; set; }

    /// <summary>The eleven flags, 1–11, always eleven entries.</summary>
    public List<RiskFlagStateDto> Flags { get; set; } = new();

    /// <summary>The Gate A condition that is not a flag (code 12).</summary>
    public RiskFlagStateDto NoLegitimateAcceptance { get; set; } = new();

    public GateAEvaluationDto GateA { get; set; } = new();

    /// <summary>The latest decision, or null when none was ever recorded.</summary>
    public RiskDecisionDto? CurrentDecision { get; set; }
}

public class RiskFlagDeclarationRequest
{
    /// <summary>Required, 1–1 000 characters: why the assessor declares the flag.</summary>
    public string? Reason { get; set; }
}

public class RiskFlagWithdrawalRequest
{
    /// <summary>Required, 1–1 000 characters: why the declaration no longer holds. Persisted and exported.</summary>
    public string? Reason { get; set; }
}

public class RiskDecisionRequest
{
    public RiskDecisionKind? Decision { get; set; }

    /// <summary>Required, 1–2 000 characters.</summary>
    public string? Reason { get; set; }
}

public class RiskDecisionDto
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public RiskDecisionKind Decision { get; set; }

    public RiskDecisionSource Source { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<RiskFlagCode> GateAConditions { get; set; } = new();

    public DateTime DecidedAt { get; set; }

    /// <summary>Null when the system recorded it at a Gate A onset.</summary>
    public int? DecidedById { get; set; }

    public DateTime? EscalatedAt { get; set; }
}

/// <summary>One row of <c>GET /RiskFlags/Flagged</c>.</summary>
public class FlaggedRiskDto
{
    public int RiskId { get; set; }

    public string ReferenceId { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    /// <summary>The codes set on the risk, declared or derived, in code order.</summary>
    public List<RiskFlagCode> Flags { get; set; } = new();

    public bool GateA { get; set; }
}

/// <summary>What one reconciliation pass did (the nightly job's report).</summary>
public class RiskFlagsRefreshSummary
{
    public int RisksEvaluated { get; set; }

    /// <summary>Derived halves that became true.</summary>
    public int FlagsRaised { get; set; }

    /// <summary>Derived halves that reverted to false because their basis was lost.</summary>
    public int FlagsReverted { get; set; }

    public int GateAOnsets { get; set; }

    public int Escalations { get; set; }
}

/// <summary>The score trend of a Top Risks row (S46 §4.9).</summary>
public class RiskTrendDto
{
    public RiskTrendDirection Direction { get; set; } = RiskTrendDirection.Unknown;

    public double? Current { get; set; }

    public double? Baseline { get; set; }

    public double? Delta { get; set; }

    public DateTime? BaselineAt { get; set; }

    public int WindowDays { get; set; }

    public int Points { get; set; }
}

/// <summary>The next decision of a Top Risks row (S46 §4.9).</summary>
public class NextDecisionDto
{
    public NextDecisionKind Kind { get; set; } = NextDecisionKind.DecisionPending;

    /// <summary>UTC; null for <see cref="NextDecisionKind.None"/> and <see cref="NextDecisionKind.DecisionPending"/>.</summary>
    public DateTime? DueAt { get; set; }

    public bool Overdue { get; set; }
}

/// <summary>One row of the executive Top Risks list (S46 §4.9).</summary>
public class TopRiskDto
{
    public int Rank { get; set; }

    public int RiskId { get; set; }

    public string ReferenceId { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    public int? OwnerId { get; set; }

    public string? OwnerName { get; set; }

    public bool GateA { get; set; }

    public List<RiskFlagCode> GateAConditions { get; set; } = new();

    public List<RiskFlagCode> Flags { get; set; } = new();

    public RiskDecisionKind? Decision { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>The inherent (calculated) score.</summary>
    public double? Inherent { get; set; }

    public double? Residual { get; set; }

    /// <summary>Expected annual loss (<c>quant_ale_mean</c>), when the risk was quantified.</summary>
    public double? ExpectedAnnualLoss { get; set; }

    public int? BusinessRank { get; set; }

    public RiskTrendDto Trend { get; set; } = new();

    /// <summary>Evidence confidence (M40); null = not declared.</summary>
    public EvidenceConfidence? Confidence { get; set; }

    public NextDecisionDto NextDecision { get; set; } = new();
}

public class TopRisksDto
{
    public DateTime GeneratedAt { get; set; }

    public int Limit { get; set; }

    /// <summary>Open risks in the caller's scope the list was chosen from.</summary>
    public int OpenRisks { get; set; }

    public List<TopRiskDto> Items { get; set; } = new();
}
