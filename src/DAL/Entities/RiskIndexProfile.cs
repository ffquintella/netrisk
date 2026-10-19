using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A versioned parameter set of the Consolidated Risk Index (S39 §12, §13; S40 §7.1) — <c>risk_index_profiles</c>.
/// One profile is in force at a time, global; its parameters are JSON (typed as <c>Model.RiskIndex.RiskIndexParameters</c>).
/// Active and Retired rows are immutable except for the Active → Retired transition. Not entity-scoped (organization
/// configuration, S40 §7.4); audited field by field except <see cref="SensitivityReport"/> and <see cref="Revision"/>.
/// </summary>
public class RiskIndexProfile
{
    public int Id { get; set; }

    /// <summary>The profile's version number, unique; never reused.</summary>
    public int Version { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>The preset the profile started from (S39 §12.3), when one was chosen.</summary>
    public string? Preset { get; set; }

    public RiskIndexMode Mode { get; set; } = RiskIndexMode.Interim;

    public RiskIndexProfileStatus Status { get; set; } = RiskIndexProfileStatus.Draft;

    /// <summary>The S39 §12.2 parameters as validated JSON (<c>Model.RiskIndex.RiskIndexParameters</c>).</summary>
    public string Parameters { get; set; } = null!;

    /// <summary>Why the profile is proposed. Required on submission.</summary>
    public string? Justification { get; set; }

    /// <summary>The sensitivity preview attached on submission (S39 §13.2), copied from the preview request.</summary>
    public string? SensitivityReport { get; set; }

    /// <summary>The IT Risk Committee's decision (minutes or date, with an attachment). Required to activate.</summary>
    public string? CommitteeDecisionRef { get; set; }

    /// <summary>The Board's approval of the bands "for communication"; required when the bands change.</summary>
    public string? BoardApprovalRef { get; set; }

    /// <summary>Approved as an explicit rebaseline (preset or mode change, or the pace rules exceeded — S39 §12.4).</summary>
    public bool IsRebaseline { get; set; }

    public string? RejectedReason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The author. The approver must be someone else, with no administrator bypass (S39 §13.1).</summary>
    public int? CreatedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Who submitted the draft: an author for the segregation of duties, written in the submission's own save.</summary>
    public int? SubmittedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ApprovedAt { get; set; }

    public int? ApprovedById { get; set; }

    /// <summary>The origin of the approver's explicit permission: <c>role:{id}</c> (a direct grant does not approve).</summary>
    public string? ApprovalGrantSource { get; set; }

    /// <summary>UTC. The instant of the first snapshot computed with this profile.</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>UTC. When the profile stopped being in force — superseded or suspended.</summary>
    public DateTime? RetiredAt { get; set; }

    public int? RetiredById { get; set; }

    /// <summary>Why a profile was suspended without a successor — never overwrites <see cref="Justification"/>.</summary>
    public string? RetireJustification { get; set; }

    /// <summary>The committee's reference for the suspension — never overwrites <see cref="CommitteeDecisionRef"/>.</summary>
    public string? RetireDecisionRef { get; set; }

    /// <summary>The draft's concurrency token, incremented on every change (<c>If-Match</c>, S40 §6.2.1).</summary>
    public int Revision { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual User? ApprovedBy { get; set; }

    public virtual User? RetiredBy { get; set; }

    public virtual User? SubmittedBy { get; set; }
}
