using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using Model.Governance;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the Track 8 governance surface.
///
/// The error handling is the interesting part. A 422 from these endpoints carries a message written
/// to be shown to a person — "Residual 9.10 is above the acceptance ceiling of 6.00", "You cannot
/// accept this risk because you own it" — so the body is passed through rather than replaced with a
/// generic failure. Replacing it would turn a refusal the user can act on into one they cannot.
/// </summary>
public class RiskGovernanceRestService(IRestService restService)
    : RestServiceBase(restService), IRiskGovernanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // --- 8.1 acceptance ---------------------------------------------------------------------

    public Task<List<RiskAcceptance>> GetAcceptancesAsync(int riskId) =>
        GetAsync<List<RiskAcceptance>>($"/Risks/{riskId}/Acceptances", []);

    public async Task<RiskAcceptance?> GetActiveAcceptanceAsync(int riskId) =>
        await GetOptionalAsync<RiskAcceptance>($"/Risks/{riskId}/Acceptances/Active");

    public Task<RiskAcceptance> CreateAcceptanceAsync(int riskId, RiskAcceptanceRequest request) =>
        SendAsync<RiskAcceptance>($"/Risks/{riskId}/Acceptances", Method.Post, request);

    public Task<RiskAcceptance> RenewAcceptanceAsync(int riskId, int acceptanceId,
        RiskAcceptanceRequest request) =>
        SendAsync<RiskAcceptance>($"/Risks/{riskId}/Acceptances/{acceptanceId}/Renew", Method.Post,
            request);

    public Task<RiskAcceptance> RevokeAcceptanceAsync(int riskId, int acceptanceId, string reason) =>
        SendAsync<RiskAcceptance>($"/Risks/{riskId}/Acceptances/{acceptanceId}/Revoke", Method.Put,
            new RiskAcceptanceRevocation { Reason = reason });

    public Task<List<RiskAcceptance>> GetExpiringAcceptancesAsync(int days = 30) =>
        GetAsync<List<RiskAcceptance>>("/RiskAcceptances/Expiring", [],
            ("days", days.ToString()));

    // --- 8.2 both scores --------------------------------------------------------------------

    public Task<List<RiskScorePair>> GetScorePairsAsync(List<int>? riskIds = null)
    {
        var query = riskIds is null
            ? Array.Empty<(string, string)>()
            : riskIds.ConvertAll(id => ("ids", id.ToString())).ToArray();

        return GetAsync<List<RiskScorePair>>("/Risks/Scores", [], query);
    }

    // --- 8.3 appetite and counter-signature -------------------------------------------------

    public Task<AppetiteEvaluation> GetAppetiteEvaluationAsync(int riskId) =>
        GetRequiredAsync<AppetiteEvaluation>($"/Risks/{riskId}/Appetite");

    public Task<List<AppetiteBreachCount>> GetRisksAboveAppetiteAsync() =>
        GetAsync<List<AppetiteBreachCount>>("/Risks/AboveAppetite", []);

    public Task<List<RiskAppetite>> GetAppetitesAsync() =>
        GetAsync<List<RiskAppetite>>("/RiskAppetites", []);

    public Task<RiskAppetite?> GetGlobalAppetiteAsync() =>
        GetOptionalAsync<RiskAppetite>("/RiskAppetites/Global");

    public Task<RiskAppetite> SaveAppetiteAsync(RiskAppetite appetite) =>
        SendAsync<RiskAppetite>("/RiskAppetites", Method.Post, appetite);

    public Task DeleteAppetiteAsync(int id) => DeleteAsync($"/RiskAppetites/{id}");

    public Task<MgmtReview> CountersignAsync(int riskId, int reviewId, string? overrideReason = null) =>
        SendAsync<MgmtReview>($"/Risks/{riskId}/MgmtReviews/{reviewId}/Countersign", Method.Post,
            new { segregationOverrideReason = overrideReason });

    // --- 8.4 audit trail --------------------------------------------------------------------

    public Task<List<AuditLog>> GetRiskAuditTrailAsync(int riskId, int limit = 1000) =>
        GetAsync<List<AuditLog>>($"/Risks/{riskId}/AuditTrail", [], ("limit", limit.ToString()));

    // --- 8.5 tasks, triage and review flags -------------------------------------------------

    public Task<List<MitigationTask>> GetTasksByMitigationAsync(int mitigationId) =>
        GetAsync<List<MitigationTask>>($"/MitigationTasks/ByMitigation/{mitigationId}", []);

    public Task<List<MitigationTask>> GetTasksByRiskAsync(int riskId) =>
        GetAsync<List<MitigationTask>>($"/Risks/{riskId}/MitigationTasks", []);

    public Task<MitigationTask> CreateTaskAsync(MitigationTaskRequest request) =>
        SendAsync<MitigationTask>("/MitigationTasks", Method.Post, request);

    public Task<MitigationTask> UpdateTaskAsync(MitigationTaskRequest request) =>
        SendAsync<MitigationTask>($"/MitigationTasks/{request.Id}", Method.Put, request);

    public Task DeleteTaskAsync(int id) => DeleteAsync($"/MitigationTasks/{id}");

    public Task<List<PendingRiskListing>> GetPendingRisksAsync(
        PendingRiskStatus? status = PendingRiskStatus.Pending)
    {
        var query = status is null
            ? Array.Empty<(string, string)>()
            : [("status", ((int)status.Value).ToString())];

        return GetAsync<List<PendingRiskListing>>("/Risks/Pending", [], query);
    }

    public Task<Risk> PromotePendingRiskAsync(int pendingId, PendingRiskPromotion edits) =>
        SendAsync<Risk>($"/Risks/Pending/{pendingId}/Promote", Method.Post, edits);

    public Task DismissPendingRiskAsync(int pendingId, string reason) =>
        SendVoidAsync($"/Risks/Pending/{pendingId}/Dismiss", Method.Post,
            new PendingRiskDismissal { Reason = reason });

    public Task RequestReviewAsync(int riskId, string reason) =>
        SendVoidAsync($"/Risks/{riskId}/RequestReview", Method.Post, new { reason });

    public Task<List<Risk>> GetReviewRequestedAsync() =>
        GetAsync<List<Risk>>("/Risks/ReviewRequested", []);

    // --- 8.6 reviewer administration --------------------------------------------------------

    public Task<List<EntityRiskReviewer>> GetEntityReviewersAsync(int entityId) =>
        GetAsync<List<EntityRiskReviewer>>($"/EntityRiskReviewers/ByEntity/{entityId}", []);

    public Task<EntityRiskReviewer> AppointReviewerAsync(int entityId, int userId, bool isPrimary) =>
        SendAsync<EntityRiskReviewer>("/EntityRiskReviewers", Method.Post,
            new { entityId, userId, isPrimary });

    public Task RemoveReviewerAsync(int id) => DeleteAsync($"/EntityRiskReviewers/{id}");

    public Task<List<CampaignStatistics>> GetCampaignStatisticsAsync(int? entityId = null)
    {
        var query = entityId is null
            ? Array.Empty<(string, string)>()
            : [("entityId", entityId.Value.ToString())];

        return GetAsync<List<CampaignStatistics>>("/RiskReviewCampaigns/Statistics", [], query);
    }

    // --- 8.7 quantitative -------------------------------------------------------------------

    public Task<QuantitativeRiskResult?> GetQuantitativeAsync(int riskId) =>
        GetOptionalAsync<QuantitativeRiskResult>($"/Risks/{riskId}/Quantitative");

    public Task<QuantitativeRiskResult> ComputeQuantitativeAsync(int riskId,
        QuantitativeRiskInput input) =>
        SendAsync<QuantitativeRiskResult>($"/Risks/{riskId}/Quantitative", Method.Post, input);

    // --- transport --------------------------------------------------------------------------

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
    /// A read whose "nothing there" answer is 204 rather than 404 — the active acceptance, the global
    /// appetite, the quantitative result. Null is the honest translation, and it is materially
    /// different from an exception: "this risk is not accepted" is not an error.
    /// </summary>
    private async Task<T?> GetOptionalAsync<T>(string route) where T : class
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(route);

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return null;

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException($"Error calling {route}", route, "GET");

            if (string.IsNullOrWhiteSpace(response.Content)) return null;

            return JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    private async Task<T> SendAsync<T>(string route, Method method, object? body)
    {
        var response = await SendCoreAsync(route, method, body);

        return JsonSerializer.Deserialize<T>(response.Content!, JsonOptions)!;
    }

    private async Task SendVoidAsync(string route, Method method, object? body) =>
        await SendCoreAsync(route, method, body);

    private async Task<RestResponse> SendCoreAsync(string route, Method method, object? body)
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

            return response;
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

    private async Task DeleteAsync(string route)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(route);

        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException(route, route, new Exception("Not found"));

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException($"Error calling {route}", route, "DELETE");
        }
        catch (HttpRequestException ex)
        {
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
