using DAL.Enums;
using Model.RiskFlags;

namespace ServerServices.Interfaces;

/// <summary>
/// The eleven mandatory flags, Gate A, the Phase 4 decision and the Top Risks list (Stage 9.5, S46).
///
/// A risk outside the caller's entity scope is <see cref="Model.Exceptions.DataNotFoundException"/>, the
/// same as a missing one. The derivation always reads organisation-wide and writes as the system actor
/// (S46 D9): what the flags of a risk are cannot depend on who asked.
/// </summary>
public interface IRiskFlagsService
{
    /// <summary>The eleven flags and the Gate A condition, with their origins.</summary>
    IReadOnlyList<RiskFlagDescriptor> GetCatalogue();

    /// <summary>The persisted flags, Gate A and the decision in force.</summary>
    Task<RiskFlagsStateDto> GetAsync(int riskId);

    /// <summary>Re-derives flags 3, 4 and 5 of one risk, escalating a Gate A onset, and returns the result.</summary>
    Task<RiskFlagsStateDto> RefreshAsync(int riskId);

    /// <summary>Re-derives every open risk — the nightly job's pass.</summary>
    Task<RiskFlagsRefreshSummary> RefreshAllAsync();

    /// <summary>Declares a flag with a written reason.</summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">Undefined code; missing or too long reason.</exception>
    /// <exception cref="Model.Exceptions.InvalidStateTransitionException">Already declared.</exception>
    Task<RiskFlagsStateDto> DeclareAsync(int riskId, RiskFlagCode code, RiskFlagDeclarationRequest request,
        int actingUserId);

    /// <summary>Withdraws a declaration with a written reason; a derived half is untouched.</summary>
    /// <exception cref="Model.Exceptions.InvalidStateTransitionException">Not declared.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>segregation_of_duties</c> for a Gate A code withdrawn by the risk's submitter, owner or manager.</exception>
    Task<RiskFlagsStateDto> WithdrawAsync(int riskId, RiskFlagCode code, RiskFlagWithdrawalRequest request,
        int actingUserId);

    /// <summary>The decision log of a risk, most recent first.</summary>
    Task<List<RiskDecisionDto>> GetDecisionsAsync(int riskId);

    /// <summary>Records a Phase 4 decision; "act immediately" is escalated and notified.</summary>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>gate_a_non_discretionary</c> for anything but act-immediately while Gate A holds.</exception>
    Task<RiskDecisionDto> RecordDecisionAsync(int riskId, RiskDecisionRequest request, int actingUserId);

    /// <summary>Risks in scope carrying <paramref name="code"/> and/or whose Gate A equals <paramref name="gateA"/>.</summary>
    Task<List<FlaggedRiskDto>> GetFlaggedAsync(RiskFlagCode? code, bool? gateA);

    /// <summary>The executive Top Risks list, 1–50 rows.</summary>
    Task<TopRisksDto> GetTopRisksAsync(int limit);

    /// <summary>
    /// Re-derives the risk's flags and refuses <paramref name="action"/> when Gate A holds — the first merit
    /// check of every acceptance, renewal, closure, deletion and non-immediate decision (S46 §4.7).
    /// </summary>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>gate_a_non_discretionary</c>.</exception>
    Task<GateAEvaluationDto> EnsureGateAAllowsAsync(int riskId, GateAAction action);
}
