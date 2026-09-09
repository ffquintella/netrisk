using Contracts.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServerServices.Auth;
using ServerServices.Http;
using ServerServices.Integrations.IssueTrackers;
using ServerServices.Integrations.IssueTrackers.Jira;
using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Notifications;
using ServerServices.Secrets;
using ServerServices.Services;
using ServerServices.Security;

namespace ServerServices.Integrations;

/// <summary>
/// One registration of the whole Track 4 service graph, shared by the API, the background-job host,
/// the console client and the in-memory test base.
///
/// Centralized because the graph has four entry points and a dozen parts: the day the API registers a
/// provider the job host does not, a notification fires when a person clicks and silently does nothing
/// when a job runs. That failure is invisible in tests that only cover one host.
/// </summary>
public static class IntegrationServiceRegistration
{
    /// <summary>
    /// Registers notification channels and dispatch (4.1), issue trackers (4.2), enterprise
    /// authentication (4.3), the Vision One and SecurityScorecard integrations (4.4, 4.5), and the
    /// Jira Service Management and Assets facet (4.6).
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="includeOutboundHttp">
    /// False for a host that supplies its own <see cref="IOutboundHttpClient"/> — the tests do, so that
    /// no test can reach a real host.
    /// </param>
    public static void AddTrack4Integrations(this IServiceCollection services, bool includeOutboundHttp = true)
    {
        // Singleton: one pooled HttpClient for the process. A per-request client exhausts sockets under
        // a busy notification queue.
        if (includeOutboundHttp) services.AddSingleton<IOutboundHttpClient, OutboundHttpClient>();

        // TryAdd, not Add: a host that has already supplied its own protector — the tests do, over a
        // fixed root secret so nothing writes to the install's key file — keeps it. With Add, the last
        // registration wins and this one would silently replace the override.
        services.TryAddSingleton<ISecretProtector, SecretProtector>();

        // External secret vaults. Registered here rather than in a graph of their own because the
        // resolver sits on the credential read path of every service below it: an API that had it and
        // a job host that did not would mean vault-backed connections working when a person clicks and
        // failing when a schedule fires — the exact split this file exists to prevent.
        //
        // The cache is a singleton, and must be: its obfuscation key is per instance, so a transient
        // one would encrypt each entry under a key that is discarded before anything can read it,
        // producing a cache with a 100% miss rate and a vault call per request.
        // Plugins and settings are TryAdd, not Add: the API registers its own IPluginsService as a
        // singleton and keeps it. They are here at all because the vault graph below depends on them,
        // and a host that composed Track 4 without them used to resolve fine — right up until the
        // first credential read went through the resolver, which is a failure in production rather
        // than at startup.
        services.TryAddTransient<ISettingsService, SettingsService>();
        services.TryAddSingleton<IPluginsService, PluginsService>();

        services.TryAddSingleton<IObfuscatedSecretCache, ObfuscatedSecretCache>();
        services.AddTransient<IPluginHttpClient, PluginHttpClientAdapter>();
        services.AddTransient<ISecretVaultService, SecretVaultService>();
        services.AddTransient<ISecretResolver, SecretResolver>();

        // 4.1 — notification channels, dispatch and subscriptions.
        services.AddTransient<INotificationChannel, EmailNotificationChannel>();
        services.AddTransient<INotificationChannel, SlackNotificationChannel>();
        services.AddTransient<INotificationChannel, TeamsNotificationChannel>();
        services.AddTransient<INotificationChannel, WebhookNotificationChannel>();
        services.AddTransient<INotificationChannelRegistry, NotificationChannelRegistry>();
        services.AddTransient<INotificationSubscriptionsService, NotificationSubscriptionsService>();
        services.AddTransient<INotificationDispatcher, NotificationDispatcher>();
        services.AddTransient<INotificationEventPublisher, NotificationEventPublisher>();

        // 4.2 — issue trackers.
        services.AddTransient<IIssueTrackerProvider, JiraIssueTrackerProvider>();
        services.AddTransient<IIssueTrackerProvider, GitHubIssueTrackerProvider>();
        services.AddTransient<IIssueTrackerProvider, GitLabIssueTrackerProvider>();
        services.AddTransient<IIssueTrackerProvider, AzureDevOpsIssueTrackerProvider>();
        services.AddTransient<IIssueTrackerProviderRegistry, IssueTrackerProviderRegistry>();
        services.AddTransient<IIssueTrackerService, IssueTrackerService>();

        // 4.6 — Jira Service Management and Assets. Registered as part of the same graph rather than
        // behind a feature flag: the clients make no call until a connection enables them, and a
        // separate registration is how the API ends up with a service the job host does not have.
        services.AddTransient<IJiraServiceManagementClient, JiraServiceManagementClient>();
        services.AddTransient<IJiraAssetsClient, JiraAssetsClient>();
        services.AddTransient<IJiraMetadataClient, JiraMetadataClient>();
        services.AddTransient<IJiraIntegrationService, JiraIntegrationService>();

        // 4.3 — enterprise authentication. The pending-sign-in store must be a singleton: a per-request
        // instance would lose the PKCE verifier between starting a sign-in and completing it.
        services.AddSingleton<PendingFederatedSignIns>();
        services.AddTransient<IIdentityProvidersService, IdentityProvidersService>();
        services.AddTransient<IScimService, ScimService>();
        services.AddTransient<IWebAuthnService, WebAuthnService>();

        // 4.4 / 4.5 — posture providers.
        services.AddTransient<ITrendMicroClient, TrendMicroClient>();
        services.AddTransient<ITrendMicroService, TrendMicroService>();
        services.AddTransient<ISecurityScorecardClient, SecurityScorecardClient>();
        services.AddTransient<ISecurityScorecardService, SecurityScorecardService>();
    }
}
