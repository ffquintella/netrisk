using System.Globalization;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Monitoring;
using Model.TailRisk;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Monitoring;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.8 (S49 §4.9) — the methodology's metrics panel: the ten metrics of MIGR-TI/IA Phase 7 in one reading, each
/// with whether it can be computed today and which Track 9 stage delivers it, plus the health of the KRIs and of the
/// reassessment triggers this stage adds.
///
/// A composition, never a store (S49 D12): M1, M4, M6 and M7 are the readings Stages 9.1, 9.7, 9.4 and 9.3 already
/// expose, called through their services so the panel and their own routes cannot disagree; M2, M3 and M5 are computed
/// here for the first time from data that exists; M8, M9 and M10 compose the third-party register (Stage 9.10), the archive
/// and backtesting (Stage 9.9) and the AI model inventory (Stage 9.12). Everything runs in the caller's scope — each source
/// is scoped, and the counts here read through the model's filters. A source that fails is logged as an error and its metric reads
/// <see cref="MetricAvailability.NotAvailable"/>: the panel never fails whole for one number.
/// </summary>
public class MethodologyMetricsService(
    ILogger logger,
    IDalService dalService,
    IRiskChainService riskChain,
    IExploitationSignalsService exploitationSignals,
    IContinuityService continuity,
    ITailRiskService tailRisk,
    IBacktestingService backtesting,
    IThirdPartiesService thirdParties,
    IAiGovernanceService aiGovernance)
    : ServiceBase(logger, dalService), IMethodologyMetricsService
{
    private const string Closed = RiskWorkflowService.StatusClosed;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public async Task<MethodologyMetricsDto> GetAsync()
    {
        var now = Clock();

        var panel = new MethodologyMetricsDto { ComputedAt = now };

        panel.Metrics.Add(await Safely(MethodologyMetric.CriticalProcessCoverage, CriticalProcessCoverageAsync));
        panel.Metrics.Add(await Safely(MethodologyMetric.OwnerAndEvidence, OwnerAndEvidenceAsync));
        panel.Metrics.Add(await Safely(MethodologyMetric.DiscoveryToDecision, () => DiscoveryToDecisionAsync(now)));
        panel.Metrics.Add(await Safely(MethodologyMetric.AggregateExposure, AggregateExposureAsync));
        panel.Metrics.Add(await Safely(MethodologyMetric.ControlEffectiveness, ControlEffectivenessAsync));
        panel.Metrics.Add(await Safely(MethodologyMetric.KevRemediation, KevRemediationAsync));
        panel.Metrics.Add(await Safely(MethodologyMetric.RestorationVerification, RestorationVerificationAsync));
        // Stage 9.10 (S51 §4.8, amending S49 §3.3): computed from the third-party register's concentration.
        panel.Metrics.Add(await Safely(MethodologyMetric.ThirdPartyConcentration, ThirdPartyConcentrationAsync));
        // Stage 9.9 (S50 §4.4, amending S49 §3.3): computed from the archive and the incident backtesting.
        panel.Metrics.Add(await Safely(MethodologyMetric.ReopenedAndUnforeseen, () => ReopenedAndUnforeseenAsync(now)));
        // Stage 9.12 (S53 §4.9, amending S49 §3.3): computed from the AI model inventory's evaluations.
        panel.Metrics.Add(await Safely(MethodologyMetric.ArtificialIntelligence, ArtificialIntelligenceAsync));

        await using var db = DalService.GetContext();
        panel.Kris = await KriHealthAsync(db, now);
        panel.Reassessment = await ReassessmentHealthAsync(db, now);

        return panel;
    }

    // --- M1–M7 ------------------------------------------------------------------------------------

    private async Task<MethodologyMetricDto> CriticalProcessCoverageAsync()
    {
        var coverage = await riskChain.GetCriticalProcessCoverageAsync();

        var metric = Metric(MethodologyMetric.CriticalProcessCoverage, MetricAvailability.Partial, "9.1",
            "GET /RiskChain/Coverage/CriticalProcesses");
        metric.Unit = "ratio";
        metric.Numerator = coverage.CoveredCount;
        metric.Denominator = coverage.CriticalProcessCount;
        metric.Value = coverage.CoverageRatio is { } ratio ? (double)ratio : null;
        metric.Detail = (coverage.CriticalProcessCount == 0
                            ? "No process is marked critical, so the coverage is not computable — neither 0 % nor 100 %."
                            : $"{coverage.CoveredCount} of {coverage.CriticalProcessCount} critical processes have an open " +
                              "risk linked.") +
                        " Discovered-asset coverage has no measure: there is no discovery inventory to compare the " +
                        "register against." +
                        (coverage.IsScopeRestricted ? " Counted over the risks in your scope only." : string.Empty);
        return metric;
    }

    private async Task<MethodologyMetricDto> OwnerAndEvidenceAsync()
    {
        await using var db = DalService.GetContext();

        var open = db.Risks.AsNoTracking().Where(r => r.Status != Closed);
        var total = await open.CountAsync();
        var complete = await open.CountAsync(r => r.Owner != null && r.Owner > 0 && r.EvidenceConfidence != null &&
                                                  r.EvidenceConfidence != EvidenceConfidence.Hypothesis);

        var metric = Metric(MethodologyMetric.OwnerAndEvidence, MetricAvailability.Available, "9.8", "GET /Monitoring/Metrics");
        metric.Unit = "ratio";
        metric.Numerator = complete;
        metric.Denominator = total;
        metric.Value = total == 0 ? null : (double)complete / total;
        metric.Detail = total == 0
            ? "There is no open risk, so the share is not computable."
            : $"{complete} of {total} open risks have an owner and evidence declared as confirmed or indicative; a " +
              "hypothesis, or no declared confidence, does not count as evidence.";
        return metric;
    }

    private async Task<MethodologyMetricDto> DiscoveryToDecisionAsync(DateTime now)
    {
        await using var db = DalService.GetContext();

        var firstDecisions = await db.RiskDecisions.AsNoTracking()
            .GroupBy(d => d.RiskId)
            .Select(g => new { RiskId = g.Key, First = g.Min(d => d.DecidedAt) })
            .ToListAsync();
        var decided = firstDecisions.ToDictionary(d => d.RiskId, d => d.First);

        var risks = await db.Risks.AsNoTracking()
            .Select(r => new { r.Id, r.SubmissionDate, r.Status })
            .ToListAsync();

        // A decision recorded before the submission date (an imported register) counts as immediate, not negative.
        var durations = risks.Where(r => decided.ContainsKey(r.Id))
            .Select(r => Math.Max(0, (decided[r.Id] - r.SubmissionDate).TotalDays))
            .ToList();
        var undecided = risks.Where(r => r.Status != Closed && !decided.ContainsKey(r.Id)).ToList();

        var metric = Metric(MethodologyMetric.DiscoveryToDecision, MetricAvailability.Available, "9.8", "GET /Monitoring/Metrics");
        metric.Unit = "days";
        metric.Value = durations.Count == 0 ? null : Math.Round(durations.Average(), 1);
        metric.Numerator = durations.Count;
        metric.Detail =
            (durations.Count == 0
                ? "No risk has a Phase 4 decision recorded yet."
                : $"Mean of {durations.Count} risk(s), from submission to the first Phase 4 decision.") +
            (undecided.Count == 0
                ? " Every open risk has a decision."
                : $" {undecided.Count} open risk(s) have none; the oldest was submitted " +
                  $"{(int)Math.Floor((now - undecided.Min(r => r.SubmissionDate)).TotalDays)} day(s) ago.");
        return metric;
    }

    private async Task<MethodologyMetricDto> AggregateExposureAsync()
    {
        var metric = Metric(MethodologyMetric.AggregateExposure, MetricAvailability.Available, "9.7", "POST /TailRisk/Portfolio");
        metric.Unit = "loss/year";

        PortfolioTailDto portfolio;
        try
        {
            portfolio = await tailRisk.AggregatePortfolioAsync(new PortfolioTailRequest { Basis = PortfolioBasis.Residual });
        }
        catch (InvalidParameterException ex)
        {
            // The portfolio's own size limit (S48 §4.6): the metric says so, the panel goes on.
            metric.Availability = MetricAvailability.NotAvailable;
            metric.Detail = ex.Message;
            return metric;
        }

        if (portfolio.Members.Count == 0)
        {
            metric.Availability = MetricAvailability.NotAvailable;
            metric.Detail = "No open risk has tail statistics, so there is no aggregate to compare with the appetite.";
            return metric;
        }

        metric.Value = portfolio.ExpectedLoss;
        metric.Numerator = portfolio.Members.Count;
        metric.Denominator = portfolio.Members.Count + portfolio.NotQuantified.Count;
        metric.Detail =
            $"Residual portfolio of {portfolio.Members.Count} quantified risk(s) of " +
            $"{portfolio.Members.Count + portfolio.NotQuantified.Count}: E[L] {Money(portfolio.ExpectedLoss)}, P95 " +
            $"{Money(portfolio.P95)}, CVaR95 {Money(portfolio.Cvar95)} (seed {portfolio.Seed}). Gate B on the portfolio: " +
            portfolio.Appetite.Explanation;
        return metric;
    }

    private async Task<MethodologyMetricDto> ControlEffectivenessAsync()
    {
        await using var db = DalService.GetContext();

        var rows = await db.Risks.AsNoTracking()
            .Where(r => r.Status != Closed)
            .Join(db.RiskScorings, r => r.Id, s => s.Id, (r, s) => new { r.EntityId, s.CalculatedRisk, s.ResidualRisk })
            .Where(x => x.ResidualRisk != null && x.CalculatedRisk > 0)
            .ToListAsync();

        var metric = Metric(MethodologyMetric.ControlEffectiveness, MetricAvailability.Partial, "9.8",
            "GET /Monitoring/Metrics");
        metric.Unit = "ratio";
        metric.Denominator = rows.Count;

        if (rows.Count == 0)
        {
            metric.Detail = "No open risk has both an inherent and a residual score.";
            return metric;
        }

        static double Reduction(float inherent, float residual) => Math.Max(0, (inherent - residual) / inherent);

        metric.Value = Math.Round(rows.Average(x => Reduction(x.CalculatedRisk, x.ResidualRisk!.Value)), 4);

        var entityIds = rows.Where(x => x.EntityId != null).Select(x => x.EntityId!.Value).Distinct().ToList();
        var names = await db.EntitiesProperties.AsNoTracking()
            .Where(p => entityIds.Contains(p.Entity) && p.Type == "name")
            .Select(p => new { p.Entity, p.Value })
            .ToListAsync();

        var weakest = rows.GroupBy(x => x.EntityId)
            .Select(g => new
            {
                Name = g.Key is { } id ? names.FirstOrDefault(n => n.Entity == id)?.Value ?? $"#{id}" : "no entity",
                Mean = g.Average(x => Reduction(x.CalculatedRisk, x.ResidualRisk!.Value))
            })
            .OrderBy(x => x.Mean).ThenBy(x => x.Name, StringComparer.Ordinal)
            .Take(10)
            .ToList();

        metric.Detail =
            $"Mean reduction from inherent to residual score over {rows.Count} open risk(s). Smallest reduction first: " +
            string.Join("; ", weakest.Select(w => $"{w.Name} {w.Mean.ToString("P0", Invariant)}")) +
            ". By business entity — the register has no control-domain dimension, so 'by domain' is not computable.";
        return metric;
    }

    private async Task<MethodologyMetricDto> KevRemediationAsync()
    {
        var kev = await exploitationSignals.GetKevRemediationMetricAsync();

        var metric = Metric(MethodologyMetric.KevRemediation, MetricAvailability.Available, "9.4",
            "GET /ExploitationSignals/Metrics/KevRemediation");
        metric.Unit = "days";
        metric.Value = kev.MedianDaysToMitigate;
        metric.Numerator = kev.MitigatedKevFindings;
        metric.Detail =
            (kev.MedianDaysToMitigate is null
                ? "No KEV finding has been mitigated with a date yet."
                : $"Median days to mitigate over {kev.MitigatedKevFindings} KEV finding(s).") +
            $" {kev.OpenKevFindings} open KEV finding(s), {kev.OpenPastCisaDueDate} past the CISA due date.";
        return metric;
    }

    private async Task<MethodologyMetricDto> RestorationVerificationAsync()
    {
        var restoration = await continuity.GetRestorationVerificationMetricAsync();
        var rto = restoration.CriticalProcesses.Rto;
        var rpo = restoration.CriticalProcesses.Rpo;

        var metric = Metric(MethodologyMetric.RestorationVerification, MetricAvailability.Available, "9.3",
            "GET /Continuity/Metrics/RestorationVerification");
        metric.Unit = "ratio";
        metric.Numerator = rto.Met;
        metric.Denominator = rto.Declared;
        metric.Value = rto.MetRatio is { } ratio ? (double)ratio : null;
        metric.Detail = rto.Declared == 0
            ? "No critical process declares an RTO, so the share is not computable."
            : $"Critical processes: RTO met by a valid restoration test in {rto.Met} of {rto.Declared} " +
              $"({rto.NotMet} not met, {rto.Unverified} unverified); RPO met in {rpo.Met} of {rpo.Declared}. " +
              "An objective with no valid test is unverified, never met.";
        return metric;
    }

    // --- the mechanism ------------------------------------------------------------------------------

    private static async Task<KriHealthDto> KriHealthAsync(AuditableContext db, DateTime now)
    {
        var kris = await db.Kris.AsNoTracking().ToListAsync();
        var readings = await MonitoringService.ValidReadingsAsync(db, kris.Select(k => k.Id).ToList());

        var health = new KriHealthDto();
        foreach (var kri in kris)
        {
            var state = KriEvaluator.Evaluate(MonitoringService.Thresholds(kri), readings.GetValueOrDefault(kri.Id) ?? [],
                now).State;

            switch (state)
            {
                case KriState.Retired: health.Retired++; continue;
                case KriState.WithinTolerance: health.WithinTolerance++; break;
                case KriState.Warning: health.Warning++; break;
                case KriState.Breached: health.Breached++; break;
                case KriState.Stale: health.Stale++; break;
                case KriState.NoReading: health.NoReading++; break;
            }

            health.Active++;
        }

        return health;
    }

    private static async Task<ReassessmentHealthDto> ReassessmentHealthAsync(AuditableContext db, DateTime now)
    {
        var since = now.AddDays(-90);
        var triggers = await MonitoringService.ReadTriggersAsync(db, db.RiskReassessmentTriggers);

        var answered = triggers.Where(t => t.State == ReassessmentTriggerState.Answered).ToList();

        return new ReassessmentHealthDto
        {
            EventsLast90Days = await db.ReassessmentEvents.AsNoTracking().CountAsync(e => e.OccurredAt >= since),
            Pending = triggers.Count(t => t.State == ReassessmentTriggerState.Pending),
            Answered = answered.Count,
            RiskClosed = triggers.Count(t => t.State == ReassessmentTriggerState.RiskClosed),
            MeanDaysToAnswer = answered.Count == 0
                ? null
                : Math.Round(answered.Average(t => (t.AnsweredAt!.Value - t.RaisedAt).TotalDays), 1)
        };
    }

    // --- M8 (Stage 9.10) ------------------------------------------------------------------------------

    /// <summary>
    /// M8 (S51 §4.8): the largest share of the organization's active critical processes that depend on one supplier — each
    /// process counted once per supplier however many links lead to it — with the most concentrated cloud and identity
    /// provider in the detail. Over the third parties the caller sees; each count over the whole organization (S51 D8).
    /// </summary>
    private async Task<MethodologyMetricDto> ThirdPartyConcentrationAsync()
    {
        var report = await thirdParties.GetConcentrationAsync();
        var top = report.Suppliers.Entries.FirstOrDefault();

        var metric = Metric(MethodologyMetric.ThirdPartyConcentration, MetricAvailability.Available, "9.10",
            "GET /ThirdParties/Concentration");
        metric.Unit = "ratio";
        metric.Denominator = report.CriticalProcessCount;
        metric.Numerator = top?.DependentCriticalProcessCount;
        metric.Value = report.Suppliers.MaxShare is { } share ? (double)share : null;

        string Leader(Model.ThirdParties.ConcentrationDimensionDto dimension, string what) =>
            dimension.Entries.FirstOrDefault() is { } first
                ? $" Most concentrated {what}: {first.Name}, {first.DependentCriticalProcessCount} critical process(es)."
                : $" No {what} is registered as active.";

        metric.Detail =
            (report.CriticalProcessCount == 0
                ? "No active process is critical, so no share is computable — neither 0 % nor 100 %."
                : top is null
                    ? "No active supplier is registered, so there is no concentration to measure."
                    : $"{top.Name} supports {top.DependentCriticalProcessCount} of {report.CriticalProcessCount} critical " +
                      "process(es), each counted once however many services or links lead to it.") +
            Leader(report.Cloud, "cloud provider") +
            Leader(report.Identity, "identity provider") +
            " Dependencies are the declared BIA dependencies; a supplied service with none declared counts nothing." +
            (report.IsScopeRestricted ? " Listed over the third parties in your scope only." : string.Empty);
        return metric;
    }

    // --- M9 (Stage 9.9) -------------------------------------------------------------------------------

    /// <summary>
    /// M9 over the last year (S50 §4.4): the unforeseen rate of the assessed incidents and near misses is the headline,
    /// with the false negatives of the cut, the incidents still unassessed, and the share of archives reopened.
    /// </summary>
    private async Task<MethodologyMetricDto> ReopenedAndUnforeseenAsync(DateTime now)
    {
        var from = now.AddDays(-Model.DecisionCycle.DecisionCycleLimits.DefaultReportDays);
        var report = await backtesting.GetReportAsync(from, now, null);

        await using var db = DalService.GetContext();
        var archives = await db.RiskArchives.AsNoTracking()
            .Where(a => a.ArchivedAt <= now && (a.ReopenedAt == null || a.ReopenedAt >= from))
            .Select(a => new { a.ReopenedAt, a.ReopenOrigin })
            .ToListAsync();
        var reopened = archives.Where(a => a.ReopenedAt != null).ToList();

        var metric = Metric(MethodologyMetric.ReopenedAndUnforeseen, MetricAvailability.Available, "9.9", "GET /Backtesting");
        metric.Unit = "ratio";
        metric.Numerator = report.NotForeseen + report.RegisteredAfterOccurrence;
        metric.Denominator = report.Assessed;
        metric.Value = report.UnforeseenRate;
        metric.Detail =
            (report.Assessed == 0
                ? "No incident or near miss of the last year has been backtested yet, so the unforeseen rate is not computable."
                : $"{report.NotForeseen + report.RegisteredAfterOccurrence} of {report.Assessed} backtested incident(s) and " +
                  $"near miss(es) of the last year were not foreseen ({report.RegisteredAfterOccurrence} only by a risk " +
                  "registered afterwards).") +
            (report.ForeseenTreated + report.ForeseenDismissed == 0
                ? " None was foreseen, so the false-negative rate is not computable."
                : $" False negatives of the cut: {report.ForeseenDismissed} of " +
                  $"{report.ForeseenTreated + report.ForeseenDismissed} foreseen.") +
            (report.NotAssessed == 0 ? string.Empty : $" {report.NotAssessed} still not assessed.") +
            (archives.Count == 0
                ? " No archive was live in the period."
                : $" Archives: {reopened.Count} of {archives.Count} live in the period were reopened " +
                  $"({reopened.Count(a => a.ReopenOrigin == RiskArchiveReopenOrigin.Condition)} by a condition, " +
                  $"{reopened.Count(a => a.ReopenOrigin == RiskArchiveReopenOrigin.QuarterlyReview)} at a review, " +
                  $"{reopened.Count(a => a.ReopenOrigin == RiskArchiveReopenOrigin.Manual)} by hand).");
        return metric;
    }

    // --- M10 (Stage 9.12) ------------------------------------------------------------------------------

    /// <summary>
    /// M10 (S53 §4.9): the share of the AI models in use (pilot or production) whose current version is evaluated on every
    /// metric its risk tier requires, with how many are not evaluated, incomplete or stale, and the coverage of each metric.
    /// A model with no recorded evaluation counts in the denominator as not evaluated — never as zero-valued, never as a pass
    /// (T215); no average of metric values is taken, because an accuracy of one model and of another are not one quantity.
    /// </summary>
    private async Task<MethodologyMetricDto> ArtificialIntelligenceAsync()
    {
        var summary = await aiGovernance.GetMetricsSummaryAsync();

        var metric = Metric(MethodologyMetric.ArtificialIntelligence, MetricAvailability.Available, "9.12", "GET /AiModels");
        metric.Unit = "ratio";
        metric.Numerator = summary.Evaluated;
        metric.Denominator = summary.InUse;
        metric.Value = summary.InUse == 0 ? null : (double)summary.Evaluated / summary.InUse;

        string Coverage(Model.AiGovernance.AiMetricCoverageDto c) =>
            $"{Tools.AiGovernance.AiModelEvaluator.Label(c.Metric)} {c.Evaluated} of {c.Required}";

        metric.Detail =
            (summary.InUse == 0
                ? "No AI model is in use (pilot or production), so the share is not computable — neither 0 % nor 100 %."
                : $"{summary.Evaluated} of {summary.InUse} AI model(s) in use are evaluated on every metric their risk tier " +
                  $"requires, for their current version; {summary.NotEvaluated} not evaluated, {summary.Incomplete} incomplete, " +
                  $"{summary.Stale} stale. A model with no recorded evaluation is not evaluated, never a pass. Current readings: " +
                  string.Join("; ", summary.EvaluatedByMetric.Where(c => c.Required > 0).Select(Coverage)) +
                  $". {summary.OverridesLast90Days} human override(s) recorded in the last 90 days.") +
            (summary.IsScopeRestricted ? " Counted over the models in your scope only." : string.Empty);
        return metric;
    }

    // --- helpers ------------------------------------------------------------------------------------

    /// <summary>Runs one metric; a failure is logged and the metric reads not available — the panel goes on (S49 D12).</summary>
    private async Task<MethodologyMetricDto> Safely(MethodologyMetric which, Func<Task<MethodologyMetricDto>> compute)
    {
        try
        {
            return await compute();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "The methodology metric {Metric} could not be computed", which);

            var metric = Metric(which, MetricAvailability.NotAvailable, StageOf(which), null);
            metric.Detail = "The source of this metric failed; the error is in the server log.";
            return metric;
        }
    }

    private static MethodologyMetricDto Metric(MethodologyMetric which, MetricAvailability availability, string stage,
        string? source) => new()
    {
        Code = $"M{(int)which}",
        Metric = which,
        Name = NameOf(which),
        Availability = availability,
        Stage = stage,
        Source = source
    };

    /// <summary>The methodology's own wording of each metric (MIGR-TI/IA Phase 7).</summary>
    public static string NameOf(MethodologyMetric metric) => metric switch
    {
        MethodologyMetric.CriticalProcessCoverage => "Coverage of critical processes and discovered assets",
        MethodologyMetric.OwnerAndEvidence => "Risks with an owner and evidence",
        MethodologyMetric.DiscoveryToDecision => "Mean time from discovery to decision",
        MethodologyMetric.AggregateExposure => "Aggregate exposure above appetite (E[L] and P95)",
        MethodologyMetric.ControlEffectiveness => "Control effectiveness and residual by domain",
        MethodologyMetric.KevRemediation => "Time to remediate KEV items",
        MethodologyMetric.RestorationVerification => "Restoration tested against the declared RTO/RPO",
        MethodologyMetric.ThirdPartyConcentration => "Concentration in third parties",
        MethodologyMetric.ReopenedAndUnforeseen => "Reopened risks, unforeseen incidents and false negatives",
        MethodologyMetric.ArtificialIntelligence => "AI: precision, recall, calibration, drift, human override",
        _ => metric.ToString()
    };

    private static string StageOf(MethodologyMetric metric) => metric switch
    {
        MethodologyMetric.CriticalProcessCoverage => "9.1",
        MethodologyMetric.AggregateExposure => "9.7",
        MethodologyMetric.KevRemediation => "9.4",
        MethodologyMetric.RestorationVerification => "9.3",
        MethodologyMetric.ThirdPartyConcentration => "9.10",
        MethodologyMetric.ReopenedAndUnforeseen => "9.9",
        MethodologyMetric.ArtificialIntelligence => "9.12",
        _ => "9.8"
    };

    private static string Money(double value) => value.ToString("N0", Invariant);
}
