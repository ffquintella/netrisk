using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.TreatmentEconomics;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for treatment economics — the treatment option and monetary cost of a mitigation, Gate C, the target
/// risk level and Gate D (Stage 9.6, S47 §7).
///
/// Every call goes through the error-reporting client, as the flags client does: a Gate A refusal of "accept", a
/// dependency cycle or a validation error answers with a sentence written for the person, and the reliable client
/// would otherwise throw on the status before the body could be read.
/// </summary>
public class TreatmentEconomicsRestService(IRestService restService)
    : RestServiceBase(restService), ITreatmentEconomicsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public async Task<MitigationEconomicsDto> GetMitigationAsync(int mitigationId) =>
        Read<MitigationEconomicsDto>(await ExecuteAsync($"/TreatmentEconomics/Mitigations/{mitigationId}", Method.Get));

    public async Task<MitigationEconomicsDto> SaveMitigationAsync(int mitigationId, MitigationEconomicsRequest request) =>
        Read<MitigationEconomicsDto>(await ExecuteAsync($"/TreatmentEconomics/Mitigations/{mitigationId}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskTreatmentEconomicsDto> GetRiskAsync(int riskId) =>
        Read<RiskTreatmentEconomicsDto>(await ExecuteAsync($"/TreatmentEconomics/Risks/{riskId}", Method.Get));

    public async Task<RiskTargetDto> SaveTargetAsync(int riskId, RiskTargetRequest request) =>
        Read<RiskTargetDto>(await ExecuteAsync($"/TreatmentEconomics/Risks/{riskId}/Target", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteTargetAsync(int riskId) =>
        await ExecuteAsync($"/TreatmentEconomics/Risks/{riskId}/Target", Method.Delete);

    public async Task<PortfolioSelectionDto> SelectPortfolioAsync(PortfolioSelectionRequest request) =>
        Read<PortfolioSelectionDto>(await ExecuteAsync("/TreatmentEconomics/Portfolio", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

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
