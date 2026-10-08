using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Model.DecisionCycle;

namespace ClientServices.Interfaces;

/// <summary>
/// REST client for the closing of MIGR-TI/IA's decision cycle (Stage 9.9, S50 §7): the archive with its reopening
/// conditions and quarterly review, incident backtesting, and the risk committee. The third line has no client surface of
/// its own: it is a role, and its writes are refused by the server.
/// </summary>
public interface IDecisionCycleService
{
    // --- archive --------------------------------------------------------------------------------

    Task<List<RiskArchiveDto>> GetArchivesAsync(bool dueOnly = false, bool includeEnded = false);

    Task<List<RiskArchiveDto>> GetRiskArchivesAsync(int riskId);

    Task<RiskArchiveDto> ArchiveAsync(int riskId, RiskArchiveRequest request);

    Task<RiskArchiveDto> ReopenArchiveAsync(int riskId, RiskArchiveReopenRequest request);

    Task<RiskArchiveDto> ReviewArchiveAsync(int riskId, RiskArchiveReviewRequest request);

    // --- backtesting ----------------------------------------------------------------------------

    Task<BacktestReportDto> GetBacktestingReportAsync(DateTime? from = null, DateTime? to = null, int? entityId = null);

    Task<BacktestIncidentDto> GetIncidentBacktestAsync(int incidentId);

    Task<BacktestIncidentDto> AssessIncidentAsync(int incidentId, BacktestAssessmentRequest request);

    // --- committees -----------------------------------------------------------------------------

    Task<List<RiskCommitteeDto>> GetCommitteesAsync(bool includeRetired = false);

    Task<RiskCommitteeDto> GetCommitteeAsync(int committeeId);

    Task<RiskCommitteeDto> CreateCommitteeAsync(RiskCommitteeRequest request);

    Task<RiskCommitteeDto> UpdateCommitteeAsync(int committeeId, RiskCommitteeRequest request);

    Task<RiskCommitteeDto> RetireCommitteeAsync(int committeeId);

    Task<RiskCommitteeDto> AddCommitteeMemberAsync(int committeeId, int userId);

    Task RemoveCommitteeMemberAsync(int committeeId, int userId);

    Task<List<RiskCommitteeDecisionDto>> GetCommitteeDecisionsAsync(int? committeeId = null, int? riskId = null,
        bool openOnly = false);

    Task<RiskCommitteeDecisionDto> GetCommitteeDecisionAsync(int decisionId);

    Task<RiskCommitteeDecisionDto> OpenCommitteeDecisionAsync(int committeeId, RiskCommitteeDecisionRequest request);

    Task<RiskCommitteeDecisionDto> VoteAsync(int decisionId, RiskCommitteeVoteRequest request);

    Task<RiskCommitteeDecisionDto> WithdrawCommitteeDecisionAsync(int decisionId, RiskCommitteeWithdrawRequest request);
}
