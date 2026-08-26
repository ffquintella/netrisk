using DAL.Entities;
using Model.Governance;

namespace ServerServices.Interfaces;

/// <summary>
/// Formal, expiring, authorized acceptance of a <em>risk</em> (Track 8 milestone 8.1.2).
///
/// Distinct from <see cref="IFindingLifecycleService"/>'s acceptance methods, which cover the
/// finding-level acceptance Track 3 built on the same table: the two share a table and share nothing
/// else, because accepting a scanner finding and accepting a register risk go through different
/// authority checks and write to different timelines.
/// </summary>
public interface IRiskAcceptancesService
{
    /// <summary>Every acceptance recorded against a risk, newest first.</summary>
    Task<List<RiskAcceptance>> GetByRiskAsync(int riskId);

    /// <summary>The acceptance in force for a risk, or null. "In force" means Active and unexpired.</summary>
    Task<RiskAcceptance?> GetActiveAsync(int riskId);

    /// <summary>Acceptances expiring within <paramref name="days"/>, soonest first.</summary>
    Task<List<RiskAcceptance>> GetExpiringAsync(int days);

    /// <summary>
    /// Records an acceptance.
    ///
    /// Refuses when: the justification or the expiry is missing; the expiry is not in the future;
    /// the caller lacks the <c>review_*</c> band matching the risk's residual severity; the caller
    /// is too close to the risk (8.3.2); or the residual is above the appetite ceiling (8.3.3).
    /// Above the dual-approval threshold it is created with the review awaiting counter-signature
    /// rather than refused.
    /// </summary>
    Task<RiskAcceptance> CreateAsync(int riskId, RiskAcceptanceRequest request, int actingUserId);

    /// <summary>
    /// Renews an acceptance: a new row linked to its predecessor, with a fresh justification and a
    /// fresh expiry. The predecessor becomes <c>Renewed</c> and is kept — moving the old row's
    /// expiry date would erase what was actually approved and until when.
    /// </summary>
    Task<RiskAcceptance> RenewAsync(int acceptanceId, RiskAcceptanceRequest request, int actingUserId);

    /// <summary>Withdraws an acceptance. The reason is mandatory.</summary>
    Task<RiskAcceptance> RevokeAsync(int acceptanceId, string reason, int actingUserId);

    /// <summary>
    /// The daily expiry pass (8.1.3): lapsed acceptances become <c>Expired</c> and their risks are
    /// flagged for review; T-30 and T-7 warnings are reported for the ones about to lapse.
    /// Idempotent — a second run on the same day repeats neither the expiry nor the warning.
    /// </summary>
    Task<RiskAcceptanceExpiryResult> ProcessExpiryAsync(DateTime asOfUtc);
}

/// <summary>What one expiry pass did, so the job can notify without re-deriving it.</summary>
public class RiskAcceptanceExpiryResult
{
    /// <summary>Acceptances that lapsed on this pass, with the risk each one covered.</summary>
    public List<RiskAcceptance> Expired { get; } = [];

    /// <summary>Acceptances approaching expiry, with the threshold that fired.</summary>
    public List<(RiskAcceptance Acceptance, int DaysBefore)> Warnings { get; } = [];
}
