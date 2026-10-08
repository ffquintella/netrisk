using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServerServices.Interfaces;
using ServerServices.Security;

namespace ServerServices.Governance;

/// <summary>
/// One registration of the whole Track 8 service graph, shared by the API, the background-job host,
/// the console client, the risk portal and the in-memory test base.
///
/// Centralized for the same reason the Track 4 graph is: the day the API registers the workflow
/// engine and the job host does not, segregation of duties applies when a person clicks and silently
/// does nothing when a job runs — and that failure is invisible in tests that cover one host.
/// </summary>
public static class GovernanceServiceRegistration
{
    /// <summary>
    /// Registers risk acceptance (8.1), the residual strategy (8.2), the workflow engine and
    /// appetite (8.3), the audit trail read side (8.4), mitigation tasks (8.5), the review portal's
    /// services (8.6) and quantitative scoring (8.7) — plus the two security services the deferred
    /// Track 7 findings needed.
    /// </summary>
    public static void AddTrack8Governance(this IServiceCollection services)
    {
        // 8.2 — the residual formula. Registered as a collection so an installation can add its own
        // and select it by name in `risk_workflow_residual_strategy`.
        services.TryAddEnumerable(ServiceDescriptor
            .Transient<IResidualRiskStrategy, MitigationPercentResidualStrategy>());

        // Stage 9.5 (S46) — the eleven flags and Gate A. The workflow engine consults it, so every host
        // that enforces the workflow enforces Gate A too. Its continuity dependency (the flag 4 basis) is
        // added only if the host has not registered one: the API registers it itself, the job host and the
        // console did not, and resolving the workflow there must not fail.
        services.TryAddTransient<IContinuityService, ContinuityService>();
        services.AddTransient<IRiskFlagsService, RiskFlagsService>();

        // Stage 9.6 (S47) — treatment economics: Gate C, the target level and Gate D. It consults Gate A through the
        // flags service and the appetite through the workflow service, so it sits after both are registered.
        services.AddTransient<ITreatmentEconomicsService, TreatmentEconomicsService>();

        // Stage 9.7 (S48) — tail statistics, the loss components, correlations, the portfolio aggregation and the
        // appetite's tail tolerances. Gate B on the tail itself lives in the workflow service, beside the ceiling.
        services.AddTransient<ITailRiskService, TailRiskService>();

        // Stage 9.8 (S49) — KRIs and the mandatory reassessment triggers, and the methodology's metrics panel. Gate B by
        // indicator itself lives in the workflow service, beside the ceiling and the tail; the job host resolves the
        // monitoring service for the nightly evaluation. The panel composes the Stage 9.1 coverage, added here only if
        // the host has not registered it (the API does; the job host and the console do not).
        services.AddTransient<IMonitoringService, MonitoringService>();
        services.TryAddTransient<IRiskChainService, RiskChainService>();
        services.AddTransient<IMethodologyMetricsService, MethodologyMetricsService>();

        // Stage 9.9 (S50) — the archive and its reopening conditions (the monitoring service reopens an archive through
        // it, and the job host resolves it for the quarterly-review notice), incident backtesting (which the metrics
        // panel's M9 composes), and the risk committee, which creates its acceptances through the acceptances service.
        services.AddTransient<IRiskArchiveService, RiskArchiveService>();
        services.AddTransient<IBacktestingService, BacktestingService>();
        services.AddTransient<IRiskCommitteesService, RiskCommitteesService>();

        // Stage 9.10 (S51) — the third-party register: HECVAT, SBOM, sub-processors, data location, contract terms and the
        // concentration by supplier, cloud and identity, which the metrics panel's M8 composes. It reads the continuity
        // graph through the continuity service registered above.
        services.AddTransient<IThirdPartiesService, ThirdPartiesService>();

        // Stage 9.11 (S52) — the LGPD data catalogue: legal basis by purpose, retention, location, international transfer,
        // the RIPD and the legal requirements of the register. No job resolves it: an expired retention signals and never
        // deletes (S52 D5, D17). The nightly flag reconciliation reads the catalogue's tables for flag 5, through
        // RiskFlagsService, and writes nothing to them.
        services.AddTransient<IDataCatalogueService, DataCatalogueService>();

        // Stage 9.12 (S53) — AI governance: the model inventory, the data each model uses (read against the catalogue above),
        // the metric readings and human overrides, and the register's risks that involve a model. The metrics panel's M10
        // composes it. It takes no decision service: governance, never use (S53 D1). The nightly flag reconciliation reads
        // the risk links for flag 11, through RiskFlagsService, and writes nothing to the inventory.
        services.AddTransient<IAiGovernanceService, AiGovernanceService>();

        // 8.3 — enforcement. Everything else consults this, so it goes in first.
        services.AddTransient<IRiskWorkflowService, RiskWorkflowService>();
        services.AddTransient<IRiskAppetitesService, RiskAppetitesService>();

        // 8.1 / 8.5 / 8.6 / 8.7
        services.AddTransient<IRiskAcceptancesService, RiskAcceptancesService>();
        services.AddTransient<IMitigationTasksService, MitigationTasksService>();
        services.AddTransient<IAuditTrailService, AuditTrailService>();
        services.AddTransient<IEntityRiskReviewersService, EntityRiskReviewersService>();
        services.AddTransient<IRiskReviewCampaignsService, RiskReviewCampaignsService>();
        services.AddTransient<IQuantitativeRiskService, QuantitativeRiskService>();

        // Deferred Track 7 findings. The file authorizer is NR-2026-017's second half (the query
        // filter is the first); token revocation is NR-2026-028.
        services.AddTransient<IFileAccessAuthorizer, FileAccessAuthorizer>();
        services.AddTransient<ITokenRevocationService, TokenRevocationService>();
    }

    /// <summary>
    /// Replaces the in-process brute-force tracker with the persisted one (NR-2026-008b).
    ///
    /// Separate from <see cref="AddTrack8Governance"/> because only a host that authenticates users
    /// needs it — the job host and the console do not — and because the decorator wraps the concrete
    /// in-process tracker, which has to be registered as itself for the wrapping to work.
    /// </summary>
    public static void AddPersistedLoginThrottling(this IServiceCollection services)
    {
        services.AddSingleton<LoginAttemptTracker>();
        services.AddSingleton<ILoginAttemptTracker, PersistedLoginAttemptTracker>();
    }
}
