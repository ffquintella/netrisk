using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Findings;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the Track 3 administration surface: deduplication heuristics, SLA policy, risk
/// acceptances, and CI API tokens.
/// </summary>
public class FindingsAdminRestService(IRestService restService) : RestServiceBase(restService), IFindingsAdminService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // --- 3.3.3 deduplication configuration --------------------------------------------------

    public Task<List<ScannerDedupConfiguration>> GetDedupConfigurationsAsync() =>
        GetAsync<List<ScannerDedupConfiguration>>("/DedupConfigurations", []);

    public Task<ScannerDedupConfiguration> GetDedupConfigurationAsync(string importer) =>
        GetRequiredAsync<ScannerDedupConfiguration>($"/DedupConfigurations/{importer}");

    public Task<DedupOptions> GetDedupOptionsAsync() =>
        GetRequiredAsync<DedupOptions>("/DedupConfigurations/options");

    public Task<ScannerDedupConfiguration> SaveDedupConfigurationAsync(ScannerDedupConfiguration configuration) =>
        SendAsync<ScannerDedupConfiguration>($"/DedupConfigurations/{configuration.Importer}", Method.Put,
            configuration);

    public Task<List<ScannerDedupConfigurationHistory>> GetDedupHistoryAsync(string importer) =>
        GetAsync<List<ScannerDedupConfigurationHistory>>($"/DedupConfigurations/{importer}/history", []);

    public Task<DedupPreviewResult> PreviewDedupAsync(string importer, PreviewFinding left, PreviewFinding right) =>
        SendAsync<DedupPreviewResult>($"/DedupConfigurations/{importer}/preview", Method.Post,
            new { left = ToNormalized(left), right = ToNormalized(right) });

    /// <summary>
    /// Expands the form's flat shape into the JSON the server's <c>NormalizedFinding</c> binds from.
    /// Only the dedup-relevant fields are sent; everything else on that type has no bearing on a key.
    /// </summary>
    private static object ToNormalized(PreviewFinding finding) => new
    {
        tool = finding.Tool,
        ruleId = finding.RuleId,
        toolUniqueId = finding.ToolUniqueId,
        title = finding.Title,
        location = finding.Location,
        component = finding.Component,
        componentVersion = finding.ComponentVersion,
        severity = finding.Severity,
        cves = (finding.Cves ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        host = string.IsNullOrWhiteSpace(finding.HostIp) && string.IsNullOrWhiteSpace(finding.Port)
            ? null
            : new { ip = finding.HostIp, port = finding.Port }
    };

    // --- 3.4.1 SLA policy -------------------------------------------------------------------

    public Task<List<SlaConfiguration>> GetSlaConfigurationsAsync(bool includeSuperseded = false) =>
        GetAsync<List<SlaConfiguration>>("/SlaConfigurations", [],
            ("includeSuperseded", includeSuperseded.ToString().ToLowerInvariant()));

    public Task<List<SlaBenchmarkView>> GetSlaBenchmarksAsync() =>
        GetAsync<List<SlaBenchmarkView>>("/SlaConfigurations/benchmarks", []);

    public Task<SlaConfiguration> SetSlaConfigurationAsync(SlaConfiguration configuration) =>
        SendAsync<SlaConfiguration>("/SlaConfigurations", Method.Post, configuration);

    // --- 3.2.3 risk acceptances -------------------------------------------------------------

    public Task<List<RiskAcceptance>> GetAcceptancesAsync(int? expiringWithinDays = null) =>
        expiringWithinDays == null
            ? GetAsync<List<RiskAcceptance>>("/RiskAcceptances", [])
            : GetAsync<List<RiskAcceptance>>("/RiskAcceptances", [],
                ("expiringWithinDays", expiringWithinDays.Value.ToString()));

    public Task<RiskAcceptance> GetAcceptanceAsync(int id) =>
        GetRequiredAsync<RiskAcceptance>($"/RiskAcceptances/{id}");

    public Task<RiskAcceptance> CreateAcceptanceAsync(RiskAcceptance acceptance, List<int> findingIds) =>
        SendAsync<RiskAcceptance>("/RiskAcceptances", Method.Post,
            new { acceptance, findingIds });

    public Task<RiskAcceptance> UpdateAcceptanceAsync(RiskAcceptance acceptance) =>
        SendAsync<RiskAcceptance>($"/RiskAcceptances/{acceptance.Id}", Method.Put, acceptance);

    public Task<RiskAcceptance> AddFindingsToAcceptanceAsync(int acceptanceId, List<int> findingIds) =>
        SendAsync<RiskAcceptance>($"/RiskAcceptances/{acceptanceId}/findings", Method.Post, findingIds);

    public Task<RiskAcceptance> RevokeAcceptanceAsync(int acceptanceId, string reason) =>
        SendAsync<RiskAcceptance>($"/RiskAcceptances/{acceptanceId}/revoke", Method.Post, new { reason });

    // --- 3.5.1 API tokens -------------------------------------------------------------------

    public Task<List<ApiTokenSummary>> GetApiTokensAsync(bool includeRevoked = false) =>
        GetAsync<List<ApiTokenSummary>>("/ApiTokens", [],
            ("includeRevoked", includeRevoked.ToString().ToLowerInvariant()));

    public Task<List<string>> GetApiTokenScopesAsync() =>
        GetAsync<List<string>>("/ApiTokens/scopes", []);

    public Task<IssuedApiToken> IssueApiTokenAsync(string name, string scopes, DateTime? expiresAt, int? entityId) =>
        SendAsync<IssuedApiToken>("/ApiTokens", Method.Post,
            new { name, scopes, expiresAt, entityId });

    public Task<ApiTokenSummary> RevokeApiTokenAsync(int id) =>
        SendAsync<ApiTokenSummary>($"/ApiTokens/{id}/revoke", Method.Post, body: null);

    // --- plumbing ---------------------------------------------------------------------------

    /// <summary>
    /// A GET whose absence is not an error: an empty list is a legitimate answer for every listing
    /// here, and a fresh install has no rows in any of these tables.
    /// </summary>
    private async Task<T> GetAsync<T>(string route, T fallback, params (string Name, string Value)[] query)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        foreach (var (name, value) in query) request.AddQueryParameter(name, value);

        try
        {
            var response = await client.GetAsync(request);

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

    private async Task<T> GetRequiredAsync<T>(string route)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(route);

        try
        {
            var response = await client.GetAsync(request);

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
    /// A write. A 400 or 422 body carries the server's explanation — which scope was unknown, which
    /// transition was refused — so it is passed through rather than replaced with a generic message.
    /// </summary>
    private async Task<T> SendAsync<T>(string route, Method method, object? body)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest(route);
        if (body != null) request.AddJsonBody(body);

        try
        {
            // ExecuteAsync rather than PostAsync/PutAsync. RestSharp's verb extensions call
            // ThrowIfError, so a 400 or 422 arrived as an HttpRequestException with the body already
            // discarded — which made the structured-error handling below unreachable and turned every
            // rejected write into a generic transport failure. An operator who typed something the
            // server refused was told the server could not be reached.
            var response = await client.ExecuteAsync(request, method);

            // A response with no status code at all never reached the server — that is the transport
            // failure. Neither `ErrorException` nor `ResponseStatus` distinguishes it: RestSharp
            // populates the first and sets the second to Error for any non-2xx, so both would report a
            // 422 as unreachable. The difference matters: one means the server said no, the other that
            // it was never asked.
            if (response.StatusCode == 0)
                throw new RestComunicationException($"Error calling {route}",
                    response.ErrorException ?? new HttpRequestException(response.ErrorMessage));

            Reject(route, method, response.StatusCode, response.Content);

            return JsonSerializer.Deserialize<T>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            // The reliable client is configured to throw before the status check, so the same
            // translation has to happen here or the server's explanation is lost after all.
            if (ex.StatusCode is { } status)
            {
                Reject(route, method, status, null);

                throw new InvalidHttpRequestException($"Error calling {route}", route,
                    method.ToString());
            }

            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    /// <summary>
    /// Turns a refusal status into the exception the caller expects, passing the server's body
    /// through where there is one.
    ///
    /// 400, 409, 422 and 403 all carry a message written for a person — "Residual 9.10 is above the
    /// acceptance ceiling of 6.00", "You cannot accept this risk because you own it". Replacing it
    /// with a generic failure turns a refusal the user can act on into one they cannot.
    /// </summary>
    private static void Reject(string route, Method method, HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NotFound)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden)
            throw new InvalidHttpRequestException(
                string.IsNullOrWhiteSpace(content) ? $"Error calling {route}" : content, route,
                method.ToString());

        if (status is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent))
            throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
    }
}
