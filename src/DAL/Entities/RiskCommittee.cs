using System;
using System.Collections.Generic;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A risk committee (Stage 9.9, S50 §4.5): the collegiate approver the methodology's Phase 0 constitutes beside the
/// individual authorizing manager. A decision of the committee needs <see cref="RequiredApprovals"/> distinct members
/// — at least two, or it would be an individual approval with a committee's name on it.
///
/// Entity-scoped like a KRI: <c>entity_id</c> null is the organization's committee, which may decide any risk; a
/// committee of an entity decides only that entity's risks. Retired, never deleted: its decisions are evidence.
/// </summary>
public class RiskCommittee : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>The charter or the act that constituted it.</summary>
    public string? Mandate { get; set; }

    public int? EntityId { get; set; }

    public int RequiredApprovals { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? RetiredAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? UpdatedBy { get; set; }

    public virtual ICollection<RiskCommitteeMember> Members { get; set; } = new List<RiskCommitteeMember>();
}

/// <summary>A voting member of a committee (S50 §4.5). Removing a member deletes the row; the trail keeps who it was.</summary>
public class RiskCommitteeMember
{
    public int Id { get; set; }

    public int CommitteeId { get; set; }

    public int UserId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual RiskCommittee Committee { get; set; } = null!;

    public virtual User User { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A risk acceptance (or renewal) submitted to a committee (S50 §4.6): the proposal, its status, and — once approved —
/// the acceptance it produced, created in the same write as the vote that reached the required approvals.
/// </summary>
public class RiskCommitteeDecision
{
    public int Id { get; set; }

    public int CommitteeId { get; set; }

    public int RiskId { get; set; }

    public RiskCommitteeDecisionKind Kind { get; set; }

    /// <summary>The acceptance a renewal renews.</summary>
    public int? RenewsAcceptanceId { get; set; }

    public RiskCommitteeDecisionStatus Status { get; set; } = RiskCommitteeDecisionStatus.Open;

    /// <summary>The committee's required approvals when the decision was opened.</summary>
    public int RequiredApprovals { get; set; }

    public string? Name { get; set; }

    public string BusinessJustification { get; set; } = null!;

    public string? CompensatingControls { get; set; }

    /// <summary>UTC. The expiry the acceptance will carry.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>The minutes or the agenda item.</summary>
    public string? MinutesReference { get; set; }

    public int? OpenedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>UTC. When it was approved, rejected or withdrawn.</summary>
    public DateTime? ClosedAt { get; set; }

    public string? WithdrawalReason { get; set; }

    /// <summary>The acceptance the approval created.</summary>
    public int? AcceptanceId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token, incremented by every vote, withdrawal and retirement (S50 §4.6, R3): two members
    /// casting the deciding vote at the same moment cannot both create the acceptance — the second write finds the row
    /// changed and is refused.
    /// </summary>
    public int Version { get; set; }

    public virtual RiskCommittee Committee { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;

    public virtual RiskAcceptance? RenewsAcceptance { get; set; }

    public virtual RiskAcceptance? Acceptance { get; set; }

    public virtual User? OpenedBy { get; set; }

    public virtual ICollection<RiskCommitteeVote> Votes { get; set; } = new List<RiskCommitteeVote>();
}

/// <summary>A member's vote on a decision (S50 §4.6). One per decision and member, final, insert-only.</summary>
public class RiskCommitteeVote
{
    public int Id { get; set; }

    public int DecisionId { get; set; }

    /// <summary>Null only if the account is later deleted; the trail keeps who voted.</summary>
    public int? VoterId { get; set; }

    public RiskCommitteeVoteChoice Choice { get; set; }

    public string? Comment { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CastAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual RiskCommitteeDecision Decision { get; set; } = null!;

    public virtual User? Voter { get; set; }
}
