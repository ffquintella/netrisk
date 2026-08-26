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
