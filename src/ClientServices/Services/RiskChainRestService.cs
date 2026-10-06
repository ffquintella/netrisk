using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.Risks.Chain;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the risk linkage chain (Stage 9.1, S41 §7).
///
/// Every call — reads included — goes through the error-reporting client rather than the reliable
/// one. The reads on this surface answer 403 (no <c>hosts</c>) and 422 (<c>entity_not_in_chain</c>)
/// with a sentence meant for the user, and the reliable client throws on any non-2xx before the body
/// can be read. A retry buys nothing on a 4xx, and losing the explanation turns a refusal the user can
/// act on into "error calling /RiskChain/…".
/// </summary>
public class RiskChainRestService(IRestService restService)
    : RestServiceBase(restService), IRiskChainService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<RiskChainDto> GetRiskChainAsync(int riskId) =>
        Read<RiskChainDto>(await ExecuteAsync($"/RiskChain/Risks/{riskId}", Method.Get));

    public async Task<RiskChainLinkDto> AddLinkAsync(int riskId, RiskChainLinkCreateDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Read<RiskChainLinkDto>(await ExecuteAsync($"/RiskChain/Risks/{riskId}/Links", Method.Post, request));
    }

    public async Task<RiskChainLinkDto?> DeleteLinkAsync(int riskId, int linkId)
    {
        var response = await ExecuteAsync($"/RiskChain/Risks/{riskId}/Links/{linkId}", Method.Delete);

        // 204: deleted. 200: demoted back to Legacy, and the body is the link as it now stands.
        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(response.Content))
            return null;

        return Read<RiskChainLinkDto>(response);
    }

    public async Task<List<RiskChainMatchDto>> GetRisksByEntityAsync(int entityId, bool inferred = false) =>
        Read<List<RiskChainMatchDto>>(await ExecuteAsync($"/RiskChain/Entities/{entityId}/Risks", Method.Get,
            query: ("inferred", inferred ? "true" : "false")));

    public async Task<List<RiskChainMatchDto>> GetRisksByHostAsync(int hostId) =>
        Read<List<RiskChainMatchDto>>(await ExecuteAsync($"/RiskChain/Hosts/{hostId}/Risks", Method.Get));

    public async Task<CriticalProcessCoverageDto> GetCriticalProcessCoverageAsync() =>
        Read<CriticalProcessCoverageDto>(await ExecuteAsync("/RiskChain/Coverage/CriticalProcesses", Method.Get));

    // --- transport --------------------------------------------------------------------------

    private static T Read<T>(RestResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException("The server answered with an empty body.",
                response.ResponseUri?.AbsolutePath ?? "", response.Request.Method.ToString());

        return JsonSerializer.Deserialize<T>(response.Content, JsonOptions)
               ?? throw new InvalidHttpRequestException("The server answered with an unreadable body.",
                   response.ResponseUri?.AbsolutePath ?? "", response.Request.Method.ToString());
    }

    private async Task<RestResponse> ExecuteAsync(string route, Method method, object? body = null,
        params (string Name, string Value)[] query)
    {
        using var client = RestService.GetClient(reportErrorResponses: true);

        var request = new RestRequest(route);
        if (body != null) request.AddJsonBody(body);
        foreach (var (name, value) in query) request.AddQueryParameter(name, value);

        try
        {
            var response = await client.ExecuteAsync(request, method);

            // No status at all means the server was never reached — the transport failure, distinct
            // from the server saying no.
            if (response.StatusCode == 0)
                throw new RestComunicationException($"Error calling {route}",
                    response.ErrorException ?? new HttpRequestException(response.ErrorMessage));

            Reject(route, method, response.StatusCode, response.Content);

            return response;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode is { } status)
            {
                Reject(route, method, status, null);
                throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
            }

            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    /// <summary>
    /// 404 is a not-found; 400, 403, 409 and 422 carry a message written for a person and are passed
    /// through; anything else that is not a success is a generic failure.
    /// </summary>
    private void Reject(string route, Method method, HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NotFound)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden)
        {
            Logger.Warning("{Method} {Route} refused with {Status}", method, route, status);
            throw new InvalidHttpRequestException(
                string.IsNullOrWhiteSpace(content) ? $"Error calling {route}" : content, route,
                method.ToString());
        }

        if (status is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent))
        {
            Logger.Error("Error calling {Route}: {Status}", route, status);
            throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
        }
    }
}
