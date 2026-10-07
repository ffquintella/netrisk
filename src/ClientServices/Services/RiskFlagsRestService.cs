using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using Model.Exceptions;
using Model.RiskFlags;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the eleven mandatory flags, Gate A, the Phase 4 decision and the Top Risks list
/// (Stage 9.5, S46 §7).
///
/// Every call goes through the error-reporting client, as the exploitation-signals client does: a Gate A
/// refusal or a segregation-of-duties refusal answers 422 with a sentence written for the person, and the
/// reliable client would otherwise throw on the status before the body could be read.
/// </summary>
public class RiskFlagsRestService(IRestService restService)
    : RestServiceBase(restService), IRiskFlagsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public async Task<List<RiskFlagDescriptor>> GetCatalogueAsync() =>
        Read<List<RiskFlagDescriptor>>(await ExecuteAsync("/RiskFlags/Catalogue", Method.Get));

    public async Task<RiskFlagsStateDto> GetRiskFlagsAsync(int riskId) =>
        Read<RiskFlagsStateDto>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}", Method.Get));

    public async Task<RiskFlagsStateDto> RefreshAsync(int riskId) =>
        Read<RiskFlagsStateDto>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}/Refresh", Method.Post));

    public async Task<RiskFlagsStateDto> DeclareAsync(int riskId, RiskFlagCode code, string reason) =>
        Read<RiskFlagsStateDto>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}/Flags/{(int)code}", Method.Put,
            new RiskFlagDeclarationRequest { Reason = reason }));

    public async Task<RiskFlagsStateDto> WithdrawAsync(int riskId, RiskFlagCode code, string reason) =>
        Read<RiskFlagsStateDto>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}/Flags/{(int)code}/Withdraw",
            Method.Post, new RiskFlagWithdrawalRequest { Reason = reason }));

    public async Task<List<RiskDecisionDto>> GetDecisionsAsync(int riskId) =>
        Read<List<RiskDecisionDto>>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}/Decisions", Method.Get));

    public async Task<RiskDecisionDto> RecordDecisionAsync(int riskId, RiskDecisionKind decision, string reason) =>
        Read<RiskDecisionDto>(await ExecuteAsync($"/RiskFlags/Risks/{riskId}/Decisions", Method.Post,
            new RiskDecisionRequest { Decision = decision, Reason = reason }));

    public async Task<List<FlaggedRiskDto>> GetFlaggedAsync(RiskFlagCode? flag = null, bool? gateA = null)
    {
        var query = new List<(string, string)>();
        if (flag != null) query.Add(("flag", ((int)flag.Value).ToString(CultureInfo.InvariantCulture)));
        if (gateA != null) query.Add(("gateA", gateA.Value ? "true" : "false"));

        return Read<List<FlaggedRiskDto>>(await ExecuteAsync("/RiskFlags/Flagged", Method.Get, null, query.ToArray()));
    }

    public async Task<TopRisksDto> GetTopRisksAsync(int limit = 10) =>
        Read<TopRisksDto>(await ExecuteAsync("/RiskFlags/TopRisks", Method.Get, null,
            ("limit", limit.ToString(CultureInfo.InvariantCulture))));

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
