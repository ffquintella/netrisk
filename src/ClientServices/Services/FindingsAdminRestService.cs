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

            return Parse<T>(route, Method.Get, response) ?? fallback;
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

            return Parse<T>(route, Method.Get, response)!;
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
        // reportErrorResponses: true is what makes the status handling below reachable at all.
        // The default client sets RestSharp's ThrowOnAnyError, which raises an HttpRequestException
        // carrying only "Request failed with status code BadRequest" before ExecuteAsync returns —
        // the body is already gone by then. So dropping the verb extensions was necessary but not
        // sufficient: the `Reject(..., response.Content)` call below never ran in the application,
        // only in the tests, whose stub answers a non-2xx instead of throwing. An operator who
        // named an unknown scope was told "Error calling /ApiTokens" and nothing else.
        using var client = MutatingClient(reportErrorResponses: true);

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

            return Parse<T>(route, method, response)!;
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
    /// Reads the body of a response that has already been accepted, or says what actually arrived.
    ///
    /// A 2xx body that is not JSON is not a JSON problem. It means the thing that answered was not
    /// this API — a proxy's sign-in page, a load balancer's error page, an SPA's index.html, a
    /// <c>Server:Url</c> aimed at the website — and System.Text.Json describes that as
    /// <c>'&lt;' is an invalid start of a value. Path: $ | LineNumber: 1</c>, which reads like a bug
    /// in the endpoint being called. It cost an afternoon on the token issuer, which was answering
    /// correctly the whole time. The status, the media type and the first bytes of the body name the
    /// real problem, so they go in the message.
    /// </summary>
    private static T? Parse<T>(string route, Method method, RestResponse response)
    {
        var verb = method.ToString().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException(
                $"{verb} {route}: the server answered {(int)response.StatusCode} with an empty body " +
                "where JSON was expected.", route, verb);

        try
        {
            return JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidHttpRequestException(
                $"{verb} {route}: the server answered {(int)response.StatusCode} with " +
                $"{MediaType(response)} rather than JSON. Whatever replied is not the NetRisk API — " +
                "check the configured server address, and whether a proxy or sign-in page is " +
                $"answering for it. The body begins: \"{Snippet(response.Content)}\" ({ex.Message})",
                route, verb);
        }
    }

    private static string MediaType(RestResponse response) =>
        string.IsNullOrWhiteSpace(response.ContentType) ? "an unlabelled body" : response.ContentType;

    /// <summary>
    /// The start of the body, on one line, short enough to read in a log entry. Enough to recognise
    /// an HTML page or a redirect notice; not enough to paste a whole document into the log.
    /// </summary>
    private static string Snippet(string content)
    {
        var flat = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 120 ? flat : flat[..120] + "…";
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

        // Unauthorized belongs with them since AuthChallengeHandler started translating the API's
        // 302-to-the-identity-provider into a 401 with a body that says the session expired. Left
        // out of this list, that sentence was replaced with "Error calling /ApiTokens" — which is
        // the same thing the operator was told before, for the same underlying reason.
        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized)
            throw new InvalidHttpRequestException(
                string.IsNullOrWhiteSpace(content) ? $"Error calling {route}" : content, route,
                method.ToString());

        if (status is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent))
            throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
    }
}
