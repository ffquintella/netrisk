using DAL.Enums;

namespace Model.TreatmentEconomics;

/// <summary>The bounds every input of Stage 9.6 is validated against (S47 §4.5, §4.8, §4.9) — one place, so the
/// service, the pure rules and the tests agree.</summary>
public static class TreatmentEconomicsLimits
{
    /// <summary>The largest amount accepted, in the unit of the FAIR inputs (10¹²).</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    public const int MinHorizonYears = 1;
    public const int MaxHorizonYears = 30;
    public const int MaxCounterpartyLength = 255;
    public const int MaxCostBasisLength = 1000;
    public const decimal MaxEffortPersonDays = 99_999_999m;
    public const int MaxDurationDays = 3650;
    public const int MaxPrerequisites = 50;
    public const int MaxRationaleLength = 2000;
    public const decimal MaxTargetScore = 10m;
    public const int MaxTaskTextLength = 4000;
    public const int MaxPortfolioMitigations = 1000;
}

/// <summary>A monetary cost, declared as a block (S47 §4.1): every amount present, zero allowed.</summary>
public class TreatmentCostRequest
{
    public decimal? OneTime { get; set; }

    public decimal? Annual { get; set; }

    public decimal? SideEffectsAnnual { get; set; }

    /// <summary>1–30; required when <see cref="OneTime"/> is above zero.</summary>
    public int? HorizonYears { get; set; }
}

/// <summary>
/// <c>PUT /TreatmentEconomics/Mitigations/{id}</c> — replaces the treatment option, the cost and the
/// prerequisites of a mitigation. A null <see cref="Cost"/> means "no monetary cost declared" (Gate C: not
/// assessable); a null <see cref="PrerequisiteMitigationIds"/> means none.
/// </summary>
public class MitigationEconomicsRequest
{
    public TreatmentOption? Option { get; set; }

    /// <summary>Required (1–255) for <see cref="TreatmentOption.TransferShare"/>.</summary>
    public string? TransferCounterparty { get; set; }

    public TreatmentCostRequest? Cost { get; set; }

    /// <summary>Where the estimate comes from, ≤ 1 000.</summary>
    public string? CostBasis { get; set; }

    public decimal? EffortPersonDays { get; set; }

    public int? DurationDays { get; set; }

    public List<int>? PrerequisiteMitigationIds { get; set; }
}

/// <summary>A declared cost with the two derived figures (S47 §4.5).</summary>
public class TreatmentCostDto
{
    public decimal OneTime { get; set; }

    public decimal Annual { get; set; }

    public decimal SideEffectsAnnual { get; set; }

    public int? HorizonYears { get; set; }

    /// <summary>Gate C's cost: annual + side effects + one-time ÷ horizon.</summary>
    public decimal AnnualizedTotal { get; set; }

    /// <summary>Gate D's budget draw: one-time + annual (side effects are a cost, not an outlay).</summary>
    public decimal FirstYear { get; set; }
}

/// <summary>Gate C on one mitigation (S47 §4.6).</summary>
public class GateCResultDto
{
    public GateCOutcome Outcome { get; set; }

    /// <summary>Every reason that holds, in enum order; empty unless <see cref="Outcome"/> is NotAssessable.</summary>
    public List<GateCNotAssessableReason> NotAssessableReasons { get; set; } = new();

    /// <summary>The mean annual loss before the treatment (<c>quant_ale_mean</c>).</summary>
    public double? ExpectedLossBefore { get; set; }

    /// <summary>The mean annual loss after it (<c>quant_residual_ale_mean</c>). Never the median.</summary>
    public double? ExpectedLossAfter { get; set; }

    /// <summary>E[L before] − E[L after].</summary>
    public double? Benefit { get; set; }

    public double? AnnualizedCost { get; set; }

    /// <summary>Benefit − annualized cost.</summary>
    public double? NetBenefit { get; set; }

    /// <summary>Benefit ÷ annualized cost; null when the cost is zero.</summary>
    public double? BenefitCostRatio { get; set; }

    /// <summary>Gordon–Loeb (2002) reference: E[L before] ÷ e (≈ 36.8 %). Informational — never decides (S47 D3).</summary>
    public double? GordonLoebReference { get; set; }

    /// <summary>Whether the annualized cost is above the reference. Informational only.</summary>
    public bool? ExceedsGordonLoebReference { get; set; }

    /// <summary>Gate A holds on the risk: the treatment is mandatory, and this result is informational.</summary>
    public bool GateAHolds { get; set; }

    /// <summary>When the quantitative analysis was computed.</summary>
    public DateTime? QuantComputedAt { get; set; }

    /// <summary>The mitigation or its economics changed after the analysis — the E[L] after may be out of date.</summary>
    public bool Stale { get; set; }

    /// <summary>A sentence the desktop and the reports can show verbatim.</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>One action-plan line with what Phase 5 still needs (S47 §4.8).</summary>
public class ActionPlanTaskDto
{
    public int TaskId { get; set; }

    public string Title { get; set; } = string.Empty;

    public MitigationTaskStatus Status { get; set; }

    public string? AcceptanceCriterion { get; set; }

    public string? CompletionEvidence { get; set; }

    public DateTime? CompletionEvidenceAt { get; set; }

    public int? CompletionEvidenceById { get; set; }

    /// <summary>Owner, due date, acceptance criterion and — when completed — completion evidence that are missing.</summary>
    public List<ActionPlanElement> Missing { get; set; } = new();
}

/// <summary>The economics of one mitigation, with Gate C (S47 §6).</summary>
public class MitigationEconomicsDto
{
    public int MitigationId { get; set; }

    public int RiskId { get; set; }

    /// <summary>Whether an economics row exists (an option has been declared).</summary>
    public bool Declared { get; set; }

    public TreatmentOption? Option { get; set; }

    public string? TransferCounterparty { get; set; }

    /// <summary>Null when no monetary cost is declared.</summary>
    public TreatmentCostDto? Cost { get; set; }

    public string? CostBasis { get; set; }

    public decimal? EffortPersonDays { get; set; }

    public int? DurationDays { get; set; }

    public List<int> PrerequisiteMitigationIds { get; set; } = new();

    /// <summary>The ordinal <c>mitigation_cost</c> value, unchanged beside the monetary cost.</summary>
    public int OrdinalCost { get; set; }

    public string? OrdinalCostName { get; set; }

    /// <summary>The editable <c>planning_strategy</c> label, shown beside the typed option.</summary>
    public string? PlanningStrategyName { get; set; }

    public int MitigationPercent { get; set; }

    public GateCResultDto GateC { get; set; } = new();

    public List<ActionPlanTaskDto> ActionPlan { get; set; } = new();

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }
}

/// <summary><c>PUT /TreatmentEconomics/Risks/{id}/Target</c> (S47 §4.3).</summary>
public class RiskTargetRequest
{
    /// <summary>0–10, the scale of the residual score.</summary>
    public decimal? TargetScore { get; set; }

    /// <summary>Annual expected loss, ≥ 0.</summary>
    public decimal? TargetExpectedLoss { get; set; }

    public DateOnly? TargetDate { get; set; }

    /// <summary>Why this level, 1–2 000. Required.</summary>
    public string? Rationale { get; set; }
}

/// <summary>The target compared with where the risk is now and with the appetite (S47 §4.9).</summary>
public class RiskTargetStatusDto
{
    /// <summary>Residual score, or inherent where no residual exists (the appetite's rule).</summary>
    public double? CurrentScore { get; set; }

    /// <summary>Residual mean annual loss, or the inherent mean where no residual mean exists.</summary>
    public double? CurrentExpectedLoss { get; set; }

    /// <summary>Current ≤ target score; null without both.</summary>
    public bool? ScoreMet { get; set; }

    public bool? ExpectedLossMet { get; set; }

    /// <summary>Current − target; positive is the distance still to go.</summary>
    public double? ScoreGap { get; set; }

    public double? ExpectedLossGap { get; set; }

    public double? AppetiteCeiling { get; set; }

    /// <summary>Target score ≤ appetite ceiling; null without an appetite or a target score.</summary>
    public bool? WithinAppetite { get; set; }

    /// <summary>The target date has passed and a declared target is not met.</summary>
    public bool Overdue { get; set; }

    public string Explanation { get; set; } = string.Empty;
}

/// <summary>The target risk level of a risk (S47 §4.3).</summary>
public class RiskTargetDto
{
    public int RiskId { get; set; }

    public decimal? TargetScore { get; set; }

    public decimal? TargetExpectedLoss { get; set; }

    public DateOnly? TargetDate { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public int? SetById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public RiskTargetStatusDto Status { get; set; } = new();
}

/// <summary>Everything Stage 9.6 says about one risk (S47 §6): Gate A, the protected flags, the appetite, the
/// target and every visible mitigation with its Gate C.</summary>
public class RiskTreatmentEconomicsDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool GateA { get; set; }

    public List<RiskFlagCode> GateAConditions { get; set; } = new();

    /// <summary>Flag 6 set.</summary>
    public bool Systemic { get; set; }

    /// <summary>Flag 8 set.</summary>
    public bool Tail { get; set; }

    /// <summary>Residual (or inherent) above the appetite ceiling; null without an appetite.</summary>
    public bool? AboveAppetite { get; set; }

    public RiskTargetDto? Target { get; set; }

    public List<MitigationEconomicsDto> Mitigations { get; set; } = new();
}

/// <summary><c>POST /TreatmentEconomics/Portfolio</c> — the Gate D constraints (S47 §4.7).</summary>
public class PortfolioSelectionRequest
{
    /// <summary>Required, ≥ 0: the cash available for first-year costs.</summary>
    public decimal? Budget { get; set; }

    /// <summary>Optional: the people capacity of the period, in person-days.</summary>
    public decimal? PeopleCapacityPersonDays { get; set; }

    /// <summary>Optional: every selected treatment's critical path must end by this date.</summary>
    public DateOnly? Deadline { get; set; }

    /// <summary>Optional; today (UTC) by default.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Optional subset; by default every visible mitigation of an open risk that is not completed.</summary>
    public List<int>? MitigationIds { get; set; }
}

/// <summary>What Gate D did with one candidate (S47 §4.7).</summary>
public class PortfolioItemDto
{
    public int MitigationId { get; set; }

    public int RiskId { get; set; }

    public string RiskSubject { get; set; } = string.Empty;

    public TreatmentOption? Option { get; set; }

    /// <summary>The tier it was considered in — its own, or the highest of the treatments that depend on it.</summary>
    public PortfolioTier Tier { get; set; }

    public PortfolioItemStatus Status { get; set; }

    /// <summary>1-based order of selection; null when not selected.</summary>
    public int? SelectionOrder { get; set; }

    /// <summary>The candidate whose selection pulled this one in as a prerequisite.</summary>
    public int? SelectedFor { get; set; }

    /// <summary>Why — one sentence per reason, written for a person.</summary>
    public List<string> Reasons { get; set; } = new();

    public GateCResultDto GateC { get; set; } = new();

    public decimal? FirstYearCost { get; set; }

    public decimal? EffortPersonDays { get; set; }

    public int? DurationDays { get; set; }

    /// <summary>Days from the start to the end of its critical path; null when unknown.</summary>
    public int? CompletionDay { get; set; }

    public List<int> PrerequisiteMitigationIds { get; set; } = new();

    public bool GateA { get; set; }

    public bool Systemic { get; set; }

    public bool Tail { get; set; }

    public bool? AboveAppetite { get; set; }

    /// <summary>Above the appetite and not selected: Gate B says treat or escalate.</summary>
    public bool RequiresEscalation { get; set; }
}

/// <summary>The Gate D selection (S47 §4.7). Computed on request, never stored.</summary>
public class PortfolioSelectionDto
{
    public DateTime GeneratedAt { get; set; }

    public decimal Budget { get; set; }

    public decimal BudgetUsed { get; set; }

    public decimal? PeopleCapacityPersonDays { get; set; }

    public decimal PeopleUsed { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? Deadline { get; set; }

    /// <summary>The sum of the Gate C benefits (mean annual loss avoided) of the selected treatments that have one.</summary>
    public double ExpectedReduction { get; set; }

    /// <summary>How many selected treatments have no known benefit (mandatory or protected without a quantitative analysis).</summary>
    public int SelectedWithUnknownBenefit { get; set; }

    public int Considered { get; set; }

    public int Selected { get; set; }

    /// <summary>A Gate A treatment was not selected — the budget, the people or the plan must change; never silent.</summary>
    public bool GateAShortfall { get; set; }

    /// <summary>A tail or systemic treatment was not selected.</summary>
    public bool ProtectedShortfall { get; set; }

    public int EscalationsRequired { get; set; }

    /// <summary>Selected first, in selection order; then the rest by tier and id.</summary>
    public List<PortfolioItemDto> Items { get; set; } = new();
}
