using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Exceptions;
using Model.Integrations;
using Model.Notifications;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the Track 4 administration surface: notification channels and subscriptions,
/// issue-tracker connections and links, identity providers, SCIM tokens, and the Vision One and
/// SecurityScorecard integrations.
///
/// Credentials travel outward only. Every read returns a view type with a has-a-token flag rather than
/// the token, and every write puts the secret in a separate field that is null when unchanged — so
/// there is no shape in which this client holds a stored credential.
/// </summary>
public class IntegrationsRestService(IRestService restService)
    : RestServiceBase(restService), IIntegrationsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // --- 4.1 notification channels ----------------------------------------------------------

    public Task<List<NotificationChannel>> GetChannelsAsync(bool includeDisabled = true) =>
        GetAsync<List<NotificationChannel>>("/NotificationChannels", [],
            ("includeDisabled", includeDisabled.ToString().ToLowerInvariant()));

    public Task<List<NotificationChannelProvider>> GetChannelProvidersAsync() =>
        GetAsync<List<NotificationChannelProvider>>("/NotificationChannels/providers", []);

    public Task<List<NotificationEventDescriptor>> GetNotificationEventsAsync() =>
        GetAsync<List<NotificationEventDescriptor>>("/NotificationChannels/events", []);

    public Task<NotificationChannel> CreateChannelAsync(NotificationChannel channel) =>
        SendAsync<NotificationChannel>("/NotificationChannels", Method.Post, channel);

    public Task<NotificationChannel> UpdateChannelAsync(NotificationChannel channel) =>
        SendAsync<NotificationChannel>($"/NotificationChannels/{channel.Id}", Method.Put, channel);

    public Task DeleteChannelAsync(int id) =>
        DeleteAsync($"/NotificationChannels/{id}");

    public Task<ChannelTestResult> TestChannelAsync(int id) =>
        SendAsync<ChannelTestResult>($"/NotificationChannels/{id}/test", Method.Post, null);

    public Task<List<NotificationSubscription>> GetSubscriptionsAsync() =>
        GetAsync<List<NotificationSubscription>>("/NotificationSubscriptions", []);

    public Task<NotificationSubscription> CreateSubscriptionAsync(NotificationSubscription subscription) =>
        SendAsync<NotificationSubscription>("/NotificationSubscriptions", Method.Post, subscription);

    public Task<NotificationSubscription> UpdateSubscriptionAsync(NotificationSubscription subscription) =>
        SendAsync<NotificationSubscription>($"/NotificationSubscriptions/{subscription.Id}", Method.Put,
            subscription);

    public Task DeleteSubscriptionAsync(int id) =>
        DeleteAsync($"/NotificationSubscriptions/{id}");

    public Task<List<NotificationDelivery>> GetDeliveriesAsync(int limit = 200) =>
        GetAsync<List<NotificationDelivery>>("/NotificationSubscriptions/deliveries", [],
            ("limit", limit.ToString()));

    public Task<NotificationDelivery> RequeueDeliveryAsync(int id) =>
        SendAsync<NotificationDelivery>($"/NotificationSubscriptions/deliveries/{id}/requeue",
            Method.Post, null);

    // --- 4.2 issue trackers -----------------------------------------------------------------

    public Task<List<IssueTrackerConnectionView>> GetIssueTrackersAsync(bool includeDisabled = true) =>
        GetAsync<List<IssueTrackerConnectionView>>("/IssueTrackers", [],
            ("includeDisabled", includeDisabled.ToString().ToLowerInvariant()));

    public Task<List<IssueTrackerProviderInfo>> GetIssueTrackerProvidersAsync() =>
        GetAsync<List<IssueTrackerProviderInfo>>("/IssueTrackers/providers", []);

    public Task<IssueTrackerConnectionView> CreateIssueTrackerAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret) =>
        SendAsync<IssueTrackerConnectionView>("/IssueTrackers", Method.Post,
            new { connection, token, webhookSecret });

    public Task<IssueTrackerConnectionView> UpdateIssueTrackerAsync(IssueTrackerConnection connection,
        string? token, string? webhookSecret) =>
        SendAsync<IssueTrackerConnectionView>($"/IssueTrackers/{connection.Id}", Method.Put,
            new { connection, token, webhookSecret });

    public Task DeleteIssueTrackerAsync(int id) => DeleteAsync($"/IssueTrackers/{id}");

    public Task<ConnectionTestResult> TestIssueTrackerAsync(int id) =>
        SendAsync<ConnectionTestResult>($"/IssueTrackers/{id}/test", Method.Post, null);

    public Task<List<IssueStatusMappingView>> GetStatusMappingsAsync(int connectionId) =>
        GetAsync<List<IssueStatusMappingView>>($"/IssueTrackers/{connectionId}/status-mappings", []);

    public Task<List<IssueStatusMappingView>> SetStatusMappingsAsync(int connectionId,
        List<IssueStatusMapping> mappings) =>
        SendAsync<List<IssueStatusMappingView>>($"/IssueTrackers/{connectionId}/status-mappings",
            Method.Put, mappings);

    public Task<IssueSyncResult> SyncIssueTrackerAsync(int id) =>
        SendAsync<IssueSyncResult>($"/IssueTrackers/{id}/sync", Method.Post, null);

    public Task<List<FindingIssueLinkView>> GetIssueSyncConflictsAsync() =>
        GetAsync<List<FindingIssueLinkView>>("/IssueTrackers/conflicts", []);

    public Task<FindingIssueLinkView> ResolveIssueSyncConflictAsync(int linkId) =>
        SendAsync<FindingIssueLinkView>($"/IssueTrackers/conflicts/{linkId}/resolve", Method.Post, null);

    // --- 4.2.2 finding ↔ issue links ---------------------------------------------------------

    public Task<List<FindingIssueLinkView>> GetLinksForFindingAsync(int findingId) =>
        GetAsync<List<FindingIssueLinkView>>($"/FindingIssues/finding/{findingId}", []);

    public Task<IssueDraft> PreviewIssueAsync(int connectionId, int findingId) =>
        GetRequiredAsync<IssueDraft>("/FindingIssues/preview",
            ("connectionId", connectionId.ToString()), ("findingId", findingId.ToString()));

    public Task<FindingIssueLinkView> CreateIssueAsync(int connectionId, int findingId) =>
        SendAsync<FindingIssueLinkView>("/FindingIssues", Method.Post, new { connectionId, findingId });

    public Task<List<FindingIssueLinkView>> CreateIssuesAsync(int connectionId, List<int> findingIds) =>
        SendAsync<List<FindingIssueLinkView>>("/FindingIssues/bulk", Method.Post,
            new { connectionId, findingIds });

    public Task<FindingIssueLinkView> LinkExistingIssueAsync(int connectionId, int findingId,
        string issueKeyOrUrl) =>
        SendAsync<FindingIssueLinkView>("/FindingIssues/link", Method.Post,
            new { connectionId, findingId, issueKey = issueKeyOrUrl });

    public Task UnlinkIssueAsync(int linkId) => DeleteAsync($"/FindingIssues/{linkId}");

    // --- 4.3 enterprise authentication -------------------------------------------------------

    public Task<List<IdentityProviderView>> GetIdentityProvidersAsync(bool includeDisabled = true) =>
        GetAsync<List<IdentityProviderView>>("/IdentityProviders", [],
            ("includeDisabled", includeDisabled.ToString().ToLowerInvariant()));

    public Task<IdentityProviderView> CreateIdentityProviderAsync(IdentityProvider provider,
        string? clientSecret) =>
        SendAsync<IdentityProviderView>("/IdentityProviders", Method.Post,
            new { provider, clientSecret });

    public Task<IdentityProviderView> UpdateIdentityProviderAsync(IdentityProvider provider,
        string? clientSecret) =>
        SendAsync<IdentityProviderView>($"/IdentityProviders/{provider.Id}", Method.Put,
            new { provider, clientSecret });

    public Task DeleteIdentityProviderAsync(int id) => DeleteAsync($"/IdentityProviders/{id}");

    public Task<ConnectionTestResult> TestIdentityProviderAsync(int id) =>
        SendAsync<ConnectionTestResult>($"/IdentityProviders/{id}/test", Method.Post, null);

    public Task<List<ScimTokenView>> GetScimTokensAsync(bool includeRevoked = false) =>
        GetAsync<List<ScimTokenView>>("/ScimTokens", [],
            ("includeRevoked", includeRevoked.ToString().ToLowerInvariant()));

    public Task<ScimTokenView> IssueScimTokenAsync(string name, int? identityProviderId) =>
        SendAsync<ScimTokenView>("/ScimTokens", Method.Post, new { name, identityProviderId });

    public Task<ScimTokenView> RevokeScimTokenAsync(int id) =>
        SendAsync<ScimTokenView>($"/ScimTokens/{id}/revoke", Method.Post, null);

    public Task<List<ScimRequestLog>> GetScimLogAsync(int limit = 200) =>
        GetAsync<List<ScimRequestLog>>("/ScimTokens/log", [], ("limit", limit.ToString()));

    // --- 4.4 Trend Micro Vision One ----------------------------------------------------------

    public Task<List<TrendMicroConnectionView>> GetTrendMicroConnectionsAsync(bool includeDisabled = true) =>
        GetAsync<List<TrendMicroConnectionView>>("/TrendMicro", [],
            ("includeDisabled", includeDisabled.ToString().ToLowerInvariant()));

    public Task<Dictionary<string, string>> GetTrendMicroRegionsAsync() =>
        GetAsync<Dictionary<string, string>>("/TrendMicro/regions", new Dictionary<string, string>());

    public Task<TrendMicroConnectionView> CreateTrendMicroConnectionAsync(TrendMicroConnection connection,
        string? apiKey) =>
        SendAsync<TrendMicroConnectionView>("/TrendMicro", Method.Post, new { connection, apiKey });

    public Task<TrendMicroConnectionView> UpdateTrendMicroConnectionAsync(TrendMicroConnection connection,
        string? apiKey) =>
        SendAsync<TrendMicroConnectionView>($"/TrendMicro/{connection.Id}", Method.Put,
            new { connection, apiKey });

    public Task DeleteTrendMicroConnectionAsync(int id) => DeleteAsync($"/TrendMicro/{id}");

    public Task<ConnectionTestResult> TestTrendMicroConnectionAsync(int id) =>
        SendAsync<ConnectionTestResult>($"/TrendMicro/{id}/test", Method.Post, null);

    public Task<PostureSyncResult> SyncTrendMicroConnectionAsync(int id) =>
        SendAsync<PostureSyncResult>($"/TrendMicro/{id}/sync", Method.Post, null);

    public Task<List<IntegrationSyncLog>> GetTrendMicroLogAsync(int limit = 50) =>
        GetAsync<List<IntegrationSyncLog>>("/TrendMicro/log", [], ("limit", limit.ToString()));

    // --- 4.5 SecurityScorecard ---------------------------------------------------------------

    public Task<List<SecurityScorecardConnectionView>> GetSecurityScorecardConnectionsAsync(
        bool includeDisabled = true) =>
        GetAsync<List<SecurityScorecardConnectionView>>("/SecurityScorecard", [],
            ("includeDisabled", includeDisabled.ToString().ToLowerInvariant()));

    public Task<SecurityScorecardConnectionView> CreateSecurityScorecardConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken) =>
        SendAsync<SecurityScorecardConnectionView>("/SecurityScorecard", Method.Post,
            new { connection, apiToken });

    public Task<SecurityScorecardConnectionView> UpdateSecurityScorecardConnectionAsync(
        SecurityScorecardConnection connection, string? apiToken) =>
        SendAsync<SecurityScorecardConnectionView>($"/SecurityScorecard/{connection.Id}", Method.Put,
            new { connection, apiToken });

    public Task DeleteSecurityScorecardConnectionAsync(int id) => DeleteAsync($"/SecurityScorecard/{id}");

    public Task<ConnectionTestResult> TestSecurityScorecardConnectionAsync(int id) =>
        SendAsync<ConnectionTestResult>($"/SecurityScorecard/{id}/test", Method.Post, null);

    public Task<PostureSyncResult> SyncSecurityScorecardConnectionAsync(int id) =>
        SendAsync<PostureSyncResult>($"/SecurityScorecard/{id}/sync", Method.Post, null);

    public Task<List<SecurityScorecardFactor>> GetSecurityScorecardHistoryAsync(int id, int limit = 500) =>
        GetAsync<List<SecurityScorecardFactor>>($"/SecurityScorecard/{id}/history", [],
            ("limit", limit.ToString()));

    public Task<List<IntegrationSyncLog>> GetSecurityScorecardLogAsync(int limit = 50) =>
        GetAsync<List<IntegrationSyncLog>>("/SecurityScorecard/log", [], ("limit", limit.ToString()));

    // --- plumbing ---------------------------------------------------------------------------

    /// <summary>
    /// Turns a transport failure into <see cref="RestComunicationException"/>.
    ///
    /// The <c>Execute*</c> methods report a failed connection as a response rather than by throwing, so
    /// without this a server that is down and a server that refused the request would be
    /// indistinguishable to the caller — and only one of those is worth retrying.
    ///
    /// The test is "no HTTP status at all", not <c>ResponseStatus</c>: RestSharp also marks a perfectly
    /// well-delivered 4xx as an error response, and treating that as unreachable would throw away the
    /// server's explanation of why it refused.
    /// </summary>
    private static void ThrowIfUnreachable(RestResponse response, string route)
    {
        if (response.StatusCode != 0) return;

        throw new RestComunicationException($"Error calling {route}",
            response.ErrorException ?? new HttpRequestException(
                response.ErrorMessage ?? "The server could not be reached."));
    }

    /// <summary>
    /// A read whose absence is not an error: a 204 or an empty body yields <paramref name="fallback"/>.
    /// Same shape as <c>FindingsAdminRestService</c>, deliberately, so the two behave identically.
    /// </summary>
    private async Task<T> GetAsync<T>(string route, T fallback, params (string Name, string Value)[] query)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        foreach (var (name, value) in query) request.AddQueryParameter(name, value);

        try
        {
            // ExecuteGetAsync, not GetAsync: RestSharp's GetAsync throws on a non-2xx before the status
            // can be inspected, which would turn every "the server refused this and said why" into an
            // opaque transport error.
            var response = await client.ExecuteGetAsync(request);

            ThrowIfUnreachable(response, route);

            if (response.StatusCode == HttpStatusCode.NoContent) return fallback;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error calling {Route}: {Status}", route, response.StatusCode);
                throw new InvalidHttpRequestException($"Error calling {route}", route, "GET");
            }

            return JsonSerializer.Deserialize<T>(response.Content!, JsonOptions) ?? fallback;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    private async Task<T> GetRequiredAsync<T>(string route, params (string Name, string Value)[] query)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        foreach (var (name, value) in query) request.AddQueryParameter(name, value);

        try
        {
            var response = await client.ExecuteGetAsync(request);

            ThrowIfUnreachable(response, route);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException(route, route, new Exception("Not found"));

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException($"Error calling {route}", route, "GET");

            return JsonSerializer.Deserialize<T>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    /// <summary>
    /// A write. A 400, 409 or 422 body carries the server's explanation — which parameter was refused,
    /// which credential could not be decrypted — so it is passed through rather than replaced with a
    /// generic message. A 502 is passed through too, because "the tracker refused it" is not a NetRisk
    /// error and the operator needs to see whose it is.
    /// </summary>
    private async Task<T> SendAsync<T>(string route, Method method, object? body)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        if (body != null) request.AddJsonBody(body);

        try
        {
            var response = method switch
            {
                Method.Put => await client.ExecutePutAsync(request),
                Method.Post => await client.ExecutePostAsync(request),
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported method")
            };

            ThrowIfUnreachable(response, route);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException(route, route, new Exception("Not found"));

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                or HttpStatusCode.Conflict or HttpStatusCode.BadGateway)
                throw new InvalidHttpRequestException(response.Content ?? $"Error calling {route}", route,
                    method.ToString());

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
                throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());

            return JsonSerializer.Deserialize<T>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    /// <summary>
    /// A delete. 204 and 200 are both success; a 400 carries a refusal worth showing — deleting a
    /// channel other channels fall back to is refused with a reason.
    /// </summary>
    private async Task DeleteAsync(string route)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);

        try
        {
            var response = await client.ExecuteDeleteAsync(request);

            ThrowIfUnreachable(response, route);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException(route, route, new Exception("Not found"));

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
                throw new InvalidHttpRequestException(response.Content ?? $"Error calling {route}", route,
                    "DELETE");

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent))
                throw new InvalidHttpRequestException($"Error calling {route}", route, "DELETE");
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }
}
