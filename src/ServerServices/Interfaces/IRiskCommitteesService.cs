using DAL.Enums;
using Model.DecisionCycle;

namespace ServerServices.Interfaces;

/// <summary>
/// Stage 9.9 (S50 §4.5–§4.6) — the risk committee as a collegiate approver beside the individual authorizing manager.
/// A committee has members and a required number of approvals (at least two); an acceptance or renewal submitted to it
/// is created, in the same write, by the vote that reaches that number, after the same gates an individual acceptance
/// passes — except the individual severity band, which the collegiate decision replaces (S50 D8).
///
/// Errors: <c>InvalidParameterException</c> (400), <c>DataNotFoundException</c> (404 — also out of scope),
/// <c>PermissionInvalidException</c> (403 — a vote by a non-member, a withdrawal by someone other than the submitter or an
/// administrator), <c>DataAlreadyExistsException</c> (409 — a second open decision on the risk, a second vote, a live
/// acceptance), <c>RuleBrokenException</c> (422: <c>committee_retired</c>, <c>committee_entity_mismatch</c>,
/// <c>committee_quorum_unreachable</c>, <c>committee_decision_closed</c>, <c>third_line_cannot_approve</c>,
/// <c>segregation_of_duties</c>, and every gate an acceptance answers with).
/// </summary>
public interface IRiskCommitteesService
{
    Task<List<RiskCommitteeDto>> GetCommitteesAsync(bool includeRetired);

    Task<RiskCommitteeDto> GetCommitteeAsync(int committeeId);

    Task<RiskCommitteeDto> CreateCommitteeAsync(RiskCommitteeRequest request, int actingUserId);

    Task<RiskCommitteeDto> UpdateCommitteeAsync(int committeeId, RiskCommitteeRequest request, int actingUserId);

    Task<RiskCommitteeDto> RetireCommitteeAsync(int committeeId, int actingUserId);

    /// <summary>Adds a voting member (idempotent). The third line may not sit as a voter (422).</summary>
    Task<RiskCommitteeDto> AddMemberAsync(int committeeId, int userId, int actingUserId);

    Task RemoveMemberAsync(int committeeId, int userId, int actingUserId);

    /// <summary>The decisions the caller can see, newest first, filtered by committee, risk and open status.</summary>
    Task<List<RiskCommitteeDecisionDto>> GetDecisionsAsync(int? committeeId, int? riskId, bool openOnly);

    Task<RiskCommitteeDecisionDto> GetDecisionAsync(int decisionId);

    /// <summary>Submits an acceptance (or a renewal) of a risk to the committee; announced through <c>committee.decision_opened</c>.</summary>
    Task<RiskCommitteeDecisionDto> OpenDecisionAsync(int committeeId, RiskCommitteeDecisionRequest request,
        int actingUserId);

    /// <summary>
    /// A member's vote — final. The vote that reaches the required approvals creates the acceptance in the same write; a
    /// gate that refuses it then refuses the vote, and nothing is recorded.
    /// </summary>
    Task<RiskCommitteeDecisionDto> VoteAsync(int decisionId, RiskCommitteeVoteRequest request, int actingUserId);

    /// <summary>Withdraws an open decision, with a reason — by whoever submitted it, or an administrator.</summary>
    Task<RiskCommitteeDecisionDto> WithdrawAsync(int decisionId, RiskCommitteeWithdrawRequest request, int actingUserId);
}
