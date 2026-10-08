using Model.DecisionCycle;

namespace ServerServices.Interfaces;

/// <summary>
/// Stage 9.9 (S50 §4.4) — incidents and near misses confronted with the register. An assessor matches each incident to
/// the registered risks that describe it, or records that none does; the outcome is computed from the dates, so a risk
/// registered after the incident is never counted as having foreseen it.
///
/// Errors: <c>InvalidParameterException</c> (400), <c>DataNotFoundException</c> (404 — an incident or risk missing or
/// outside the caller's scope).
/// </summary>
public interface IBacktestingService
{
    /// <summary>One incident's backtest — its outcome, the matched risks the caller may see, and how many are hidden.</summary>
    Task<BacktestIncidentDto> GetIncidentAsync(int incidentId);

    /// <summary>
    /// Records (or replaces) the backtest of an incident: the matched risks, or none with
    /// <c>NoCorrespondingScenario</c> set. Every risk must be visible to the caller.
    /// </summary>
    Task<BacktestIncidentDto> AssessAsync(int incidentId, BacktestAssessmentRequest request, int actingUserId);

    /// <summary>
    /// The backtesting report of the incidents and near misses that occurred in [from, to] (UTC, default the last year),
    /// optionally of one entity: the counts by outcome, the unforeseen and false-negative rates, and the incidents.
    /// </summary>
    Task<BacktestReportDto> GetReportAsync(DateTime? from, DateTime? to, int? entityId);
}
