using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// The treatment option and the monetary cost of one mitigation (Stage 9.6, S47 §4.1) — what Gate C
/// computes against and Gate D budgets with. The ordinal <c>mitigations.mitigation_cost</c> scale stays as
/// it is, for organisations that do not estimate in money.
///
/// A table of its own rather than columns on <c>mitigations</c>: <c>PUT /Mitigations/{id}</c> adapts a DTO
/// that does not carry these fields over the stored row, so every client that does not know them would
/// clear them with no trail (S47 D1, the same reason as S46 D1).
///
/// A cost is declared as a block: the three amounts are all null ("not declared", which Gate C reports as
/// not assessable) or all present (zero is a declared cost) — <c>ck_mitigation_economics_cost_complete</c>.
/// </summary>
public class MitigationEconomics
{
    public int Id { get; set; }

    public int MitigationId { get; set; }

    public TreatmentOption TreatmentOption { get; set; }

    /// <summary>The insurer, supplier or contract party; required for <see cref="DAL.Enums.TreatmentOption.TransferShare"/>.</summary>
    public string? TransferCounterparty { get; set; }

    /// <summary>Implementation cost, paid once.</summary>
    public decimal? CostOneTime { get; set; }

    /// <summary>Recurring cost per year: licences, operation, insurance premium.</summary>
    public decimal? CostAnnual { get; set; }

    /// <summary>Side effects per year: productivity, friction, revenue forgone when avoiding.</summary>
    public decimal? CostSideEffectsAnnual { get; set; }

    /// <summary>Years over which <see cref="CostOneTime"/> is amortized, 1–30; required when it is above zero.</summary>
    public int? CostHorizonYears { get; set; }

    /// <summary>Where the estimate comes from — a quote, a contract, an internal estimate.</summary>
    public string? CostBasis { get; set; }

    /// <summary>People effort, for Gate D's capacity constraint.</summary>
    public decimal? EffortPersonDays { get; set; }

    /// <summary>Lead time to complete, for Gate D's deadline constraint.</summary>
    public int? DurationDays { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    /// <summary>Whether a monetary cost is declared (the block is all-or-nothing).</summary>
    public bool CostDeclared => CostAnnual is not null && CostOneTime is not null && CostSideEffectsAnnual is not null;

    public virtual Mitigation Mitigation { get; set; } = null!;

    public virtual User? UpdatedBy { get; set; }
}
