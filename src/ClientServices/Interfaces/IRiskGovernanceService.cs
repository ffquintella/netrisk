using DAL.Entities;
using DAL.Enums;
using Model.Governance;

namespace ClientServices.Interfaces;

/// <summary>
/// The desktop client's view of the Track 8 governance surface: acceptance, appetite, the audit
/// trail, treatment tasks, pending-risk triage and quantitative scoring.
///
/// One client for the whole track rather than one per controller. The desktop screens that consume
/// it are the risk editor's acceptance panel, the appetite admin screen and the triage list, and
/// each of those touches several of these endpoints — splitting the client along controller lines
/// would mean three injections per view-model for no gain.
/// </summary>
public interface IRiskGovernanceService
{
    // --- 8.1 acceptance ---------------------------------------------------------------------

    Task<List<RiskAcceptance>> GetAcceptancesAsync(int riskId);

    /// <summary>The acceptance in force, or null when the risk is not accepted.</summary>
    Task<RiskAcceptance?> GetActiveAcceptanceAsync(int riskId);

    Task<RiskAcceptance> CreateAcceptanceAsync(int riskId, RiskAcceptanceRequest request);

    Task<RiskAcceptance> RenewAcceptanceAsync(int riskId, int acceptanceId, RiskAcceptanceRequest request);

    Task<RiskAcceptance> RevokeAcceptanceAsync(int riskId, int acceptanceId, string reason);

    Task<List<RiskAcceptance>> GetExpiringAcceptancesAsync(int days = 30);

    // --- 8.2 both scores --------------------------------------------------------------------

    Task<List<RiskScorePair>> GetScorePairsAsync(List<int>? riskIds = null);

    // --- 8.3 appetite and counter-signature -------------------------------------------------

    Task<AppetiteEvaluation> GetAppetiteEvaluationAsync(int riskId);

    Task<List<AppetiteBreachCount>> GetRisksAboveAppetiteAsync();

    Task<List<RiskAppetite>> GetAppetitesAsync();

    /// <summary>The organization-wide appetite, or null when none is configured — which is the
    /// seeded state and means nothing is gated.</summary>
    Task<RiskAppetite?> GetGlobalAppetiteAsync();

    Task<RiskAppetite> SaveAppetiteAsync(RiskAppetite appetite);

    Task DeleteAppetiteAsync(int id);

    Task<MgmtReview> CountersignAsync(int riskId, int reviewId, string? overrideReason = null);

    // --- 8.4 audit trail --------------------------------------------------------------------

    Task<List<AuditLog>> GetRiskAuditTrailAsync(int riskId, int limit = 1000);

    // --- 8.5 tasks, triage and review flags -------------------------------------------------

    Task<List<MitigationTask>> GetTasksByMitigationAsync(int mitigationId);

    Task<List<MitigationTask>> GetTasksByRiskAsync(int riskId);

    Task<MitigationTask> CreateTaskAsync(MitigationTaskRequest request);

    Task<MitigationTask> UpdateTaskAsync(MitigationTaskRequest request);

    Task DeleteTaskAsync(int id);

    Task<List<PendingRiskListing>> GetPendingRisksAsync(
        PendingRiskStatus? status = PendingRiskStatus.Pending);

    Task<Risk> PromotePendingRiskAsync(int pendingId, PendingRiskPromotion edits);

    Task DismissPendingRiskAsync(int pendingId, string reason);

    Task RequestReviewAsync(int riskId, string reason);

    Task<List<Risk>> GetReviewRequestedAsync();

    // --- 8.6 portal-facing reads the desktop also needs -------------------------------------

    Task<List<EntityRiskReviewer>> GetEntityReviewersAsync(int entityId);

    Task<EntityRiskReviewer> AppointReviewerAsync(int entityId, int userId, bool isPrimary);

    Task RemoveReviewerAsync(int id);

    Task<List<CampaignStatistics>> GetCampaignStatisticsAsync(int? entityId = null);

    // --- 8.7 quantitative -------------------------------------------------------------------

    Task<QuantitativeRiskResult?> GetQuantitativeAsync(int riskId);

    Task<QuantitativeRiskResult> ComputeQuantitativeAsync(int riskId, QuantitativeRiskInput input);
}
