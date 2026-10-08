using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// An AI model in the inventory (Stage 9.12, S53 §4.1, T212) — the first control MIGR-TI/IA Phase 6 makes mandatory:
/// purpose, data, vendor and version, with the organization's risk tier, how people oversee its outputs, its accountable
/// owner and how long an evaluation stays current.
///
/// <b>Governance, never use.</b> Nothing in the product runs a model or lets one decide: this record describes a model the
/// organization uses elsewhere so its risks enter the same register (flag 11) and its evaluation can be shown — or shown
/// missing. A model is not a principal: it has no credential, no user and no route that acts for it (S53 D1, T215).
///
/// Entity-scoped like a third party: <c>entity_id</c> null is the organization's model, seen by every reader. Never
/// deleted — retired with a reason and kept as evidence (S53 D9).
/// </summary>
public class AiModel : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    /// <summary>Unique in the organization, case-insensitively.</summary>
    public string Name { get; set; } = null!;

    /// <summary>What the model is used for, and by whom.</summary>
    public string Purpose { get; set; } = null!;

    public AiModelKind Kind { get; set; }

    public AiModelSource Source { get; set; }

    /// <summary>The vendor, when <see cref="Source"/> is <see cref="AiModelSource.Vendor"/> — a registered third party.</summary>
    public int? ThirdPartyId { get; set; }

    /// <summary>The version in use. A reading evaluates the version current when it was recorded (S53 D3).</summary>
    public string Version { get; set; } = null!;

    /// <summary>
    /// UTC. Since when <see cref="Version"/> is in use — set when the version changes, or declared. A reading measured, a
    /// period started or an override that occurred before it is not of this version and is refused; NULL (a model
    /// registered without it) sets no lower bound (S53 D3).
    /// </summary>
    public DateTime? VersionSince { get; set; }

    public AiModelStatus Status { get; set; } = AiModelStatus.Proposed;

    /// <summary>NULL: not declared — a finding, read as high for what the model must report.</summary>
    public AiModelRiskTier? RiskTier { get; set; }

    /// <summary>NULL: not declared — a finding.</summary>
    public AiHumanOversight? HumanOversight { get; set; }

    /// <summary>The accountable person — a user, enabled, never the third line ("human roles, never AI").</summary>
    public int? OwnerId { get; set; }

    public int? EntityId { get; set; }

    /// <summary>How many days a metric reading stays current; older, the metric reads stale — never evaluated.</summary>
    public int MaxEvaluationAgeDays { get; set; }

    /// <summary>UTC. When the data the model uses was last declared — even as none. NULL: never declared.</summary>
    public DateTime? DataDeclaredAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>UTC. Set exactly when the status is <see cref="AiModelStatus.Retired"/>.</summary>
    public DateTime? RetiredAt { get; set; }

    public int? RetiredById { get; set; }

    public string? RetireReason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual ThirdParty? ThirdParty { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? Owner { get; set; }

    public virtual User? RetiredBy { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? UpdatedBy { get; set; }

    public virtual ICollection<AiModelDataLink> DataLinks { get; set; } = new List<AiModelDataLink>();
}

/// <summary>
/// A data record a model uses, and how (S53 §4.2) — the <c>organizationData</c> node the Stage 9.11 catalogue is keyed by,
/// so the record's LGPD catalogue is read through the node with no new column on any link (S53 D4). Declared whole.
/// </summary>
public class AiModelDataLink
{
    public int Id { get; set; }

    public int ModelId { get; set; }

    public int EntityId { get; set; }

    public AiModelDataUsage Usage { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// One reading of a model metric (S53 §4.4, T214) — the model's evaluation history. Insert-only like a KRI reading: a
/// mistaken reading is voided with a reason and stays in the history, ignored by the evaluation. A reading evaluates the
/// model version current when it was recorded; a new version starts not evaluated (S53 D3).
/// </summary>
public class AiModelMetricReading
{
    public int Id { get; set; }

    public int ModelId { get; set; }

    public AiModelMetric Metric { get; set; }

    /// <summary>A fraction in [0, 1]; drift is a non-negative statistic. Computed by the server for the override rate.</summary>
    public decimal Value { get; set; }

    /// <summary>The model version evaluated — the model's version when the reading was recorded, never the payload's.</summary>
    public string ModelVersion { get; set; } = null!;

    /// <summary>UTC. When the measure was true. Never in the future.</summary>
    public DateTime MeasuredAt { get; set; }

    /// <summary>UTC. The window measured, when there is one; required for the override rate.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>How many cases the measure covers; for the override rate, the outputs people reviewed in the period.</summary>
    public int? SampleSize { get; set; }

    /// <summary>For the override rate only: the live overrides recorded in the period when the reading was taken.</summary>
    public int? OverrideCount { get; set; }

    /// <summary>How it was measured: the test set, the statistic.</summary>
    public string? Method { get; set; }

    /// <summary>Where the evaluation report is. Never fetched.</summary>
    public string? EvidenceReference { get; set; }

    public int? RecordedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual User? RecordedBy { get; set; }

    public virtual User? VoidedBy { get; set; }
}

/// <summary>
/// A human decision contrary to a model's output, recorded with its author and reason (S53 §4.5) — what the human override
/// rate is computed from. "An override nobody can find afterwards is not an override" (the coverage analysis, §13).
/// Insert-only; a mistaken record is voided with a reason.
/// </summary>
public class AiModelOverride
{
    public int Id { get; set; }

    public int ModelId { get; set; }

    /// <summary>The model version overridden — the model's version when the override was recorded.</summary>
    public string ModelVersion { get; set; } = null!;

    /// <summary>UTC. When the person decided. Never in the future.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>What the model proposed — the kind of output, never a person's data.</summary>
    public string ModelOutput { get; set; } = null!;

    /// <summary>What the person decided instead.</summary>
    public string HumanDecision { get; set; } = null!;

    public string Reason { get; set; } = null!;

    /// <summary>The author: the person who recorded the override.</summary>
    public int? RecordedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? VoidedAt { get; set; }

    public int? VoidedById { get; set; }

    public string? VoidReason { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual User? RecordedBy { get; set; }

    public virtual User? VoidedBy { get; set; }
}

/// <summary>
/// A risk of the register that involves an inventoried model (S53 §4.6, T213) — the AI component's risks in the same
/// register, with the same fields. A link to a model that is not retired derives flag 11. Visible when the risk and the
/// model both are.
/// </summary>
public class AiModelRisk
{
    public int Id { get; set; }

    public int ModelId { get; set; }

    public int RiskId { get; set; }

    /// <summary>How the risk involves the model (discrimination, hallucination, prompt injection, drift…).</summary>
    public string? Note { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual AiModel Model { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
