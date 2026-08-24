using DAL.Entities;
using DAL.Enums;

namespace ServerServices.Interfaces;

/// <summary>
/// Finding triage lifecycle and its audit trail (Track 3 milestone 3.2).
///
/// Every status change in the system goes through here — manual triage, imports, and jobs alike —
/// because the transition matrix and the history row have to be written together. A caller that
/// sets <c>status_id</c> directly produces a finding whose timeline has a gap, and a timeline with
/// gaps is not evidence of anything.
/// </summary>
public interface IFindingLifecycleService
{
    /// <summary>
    /// Moves a finding to <paramref name="to"/> and records why.
    ///
    /// Throws <see cref="Model.Exceptions.InvalidStateTransitionException"/> for an illegal
    /// transition (surfaced as HTTP 422) and
    /// <see cref="Model.Exceptions.InvalidParameterException"/> when the target state needs a
    /// justification or a canonical finding it was not given.
    /// </summary>
    Task<Vulnerability> TransitionAsync(int findingId, FindingStatus to, int? userId,
        FindingStatusChangeSource source, string? justification = null, int? duplicateOfId = null,
        int? riskAcceptanceId = null);

    /// <summary>
    /// Records a finding's creation as the first row of its timeline (from-status null). Called by
    /// the ingestion pipeline and by manual creation, so every finding has a first event.
    /// </summary>
    Task RecordCreationAsync(int findingId, FindingStatus initialStatus, int? userId,
        FindingStatusChangeSource source, DateTime? at = null);

    /// <summary>The finding's timeline, newest first.</summary>
    Task<List<FindingStatusHistory>> GetHistoryAsync(int findingId);

    /// <summary>Which states this finding may move to right now, for the triage UI.</summary>
    Task<List<FindingStatus>> GetAllowedTransitionsAsync(int findingId);

    // --- Risk acceptance (3.2.3) -----------------------------------------------------------

    /// <summary>
    /// Creates an acceptance and moves every listed finding to
    /// <see cref="FindingStatus.RiskAccepted"/>, recording an event per finding.
    ///
    /// A finding that cannot legally move is reported rather than skipped silently — an acceptance
    /// that covers less than the operator thinks is worse than one that fails to save.
    /// </summary>
    Task<RiskAcceptance> CreateAcceptanceAsync(RiskAcceptance acceptance, IReadOnlyList<int> findingIds, int? userId);

    Task<RiskAcceptance> GetAcceptanceAsync(int id);

    /// <summary>
    /// Acceptances, optionally only those expiring within <paramref name="expiringWithinDays"/> —
    /// the filter the management view leads with.
    /// </summary>
    Task<List<RiskAcceptance>> GetAcceptancesAsync(int? expiringWithinDays = null);

    Task<RiskAcceptance> UpdateAcceptanceAsync(RiskAcceptance acceptance, int? userId);

    /// <summary>Adds findings to a live acceptance, suppressing each and recording the event.</summary>
    Task<RiskAcceptance> AddFindingsToAcceptanceAsync(int acceptanceId, IReadOnlyList<int> findingIds, int? userId);

    /// <summary>
    /// Withdraws an acceptance before expiry and reactivates everything it covered. The reason is
    /// mandatory: revoking is as consequential as accepting.
    /// </summary>
    Task<RiskAcceptance> RevokeAcceptanceAsync(int acceptanceId, string reason, int? userId);

    /// <summary>
    /// The daily expiry pass (3.2.4). Expires acceptances past their date, reactivates their
    /// findings with <c>source=Job</c>, and returns what it did so the caller can notify.
    /// Idempotent: running it twice on the same day changes nothing the second time.
    /// </summary>
    Task<AcceptanceExpiryResult> ProcessExpiredAcceptancesAsync(DateTime nowUtc, int[]? warningThresholdDays = null);
}

/// <summary>
/// What one expiry pass did. Returned rather than notified from inside the service so the job owns
/// the messaging and the service stays testable without a mail server.
/// </summary>
public class AcceptanceExpiryResult
{
    /// <summary>Acceptances moved to Expired on this pass.</summary>
    public List<RiskAcceptance> Expired { get; set; } = new();

    /// <summary>Findings reactivated, by acceptance id.</summary>
    public Dictionary<int, List<int>> ReactivatedFindings { get; set; } = new();

    /// <summary>
    /// Acceptances that crossed a pre-expiry warning threshold on this pass, with the threshold
    /// crossed. Each acceptance appears at most once per threshold, ever.
    /// </summary>
    public List<(RiskAcceptance Acceptance, int DaysBefore)> Warnings { get; set; } = new();
}
