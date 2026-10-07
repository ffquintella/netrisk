using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.TailRisk;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for tail statistics and portfolio — the tail of a risk, its loss components, the declared correlations,
/// the portfolio aggregation and the appetite's monetary tolerances (Stage 9.7, S48 §7).
///
/// Every call goes through the error-reporting client, as the treatment economics client does: a correlation matrix that
/// is not positive semidefinite or a validation error answers with a sentence written for the person, and the reliable
/// client would otherwise throw on the status before the body could be read.
/// </summary>
public class TailRiskRestService(IRestService restService) : RestServiceBase(restService), ITailRiskService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public async Task<RiskTailDto> GetRiskAsync(int riskId) =>
        Read<RiskTailDto>(await ExecuteAsync($"/TailRisk/Risks/{riskId}", Method.Get));

    public async Task<RiskTailDto> SaveLossComponentsAsync(int riskId, LossComponentsRequest request) =>
        Read<RiskTailDto>(await ExecuteAsync($"/TailRisk/Risks/{riskId}/LossComponents", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskTailDto> DeleteLossComponentsAsync(int riskId) =>
        Read<RiskTailDto>(await ExecuteAsync($"/TailRisk/Risks/{riskId}/LossComponents", Method.Delete));

    public async Task<List<RiskCorrelationDto>> GetCorrelationsAsync(int? riskId = null) =>
        Read<List<RiskCorrelationDto>>(riskId is { } id
            ? await ExecuteAsync("/TailRisk/Correlations", Method.Get, null,
                ("riskId", id.ToString(CultureInfo.InvariantCulture)))
            : await ExecuteAsync("/TailRisk/Correlations", Method.Get));

    public async Task<RiskCorrelationDto> SaveCorrelationAsync(RiskCorrelationRequest request) =>
        Read<RiskCorrelationDto>(await ExecuteAsync("/TailRisk/Correlations", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteCorrelationAsync(int correlationId) =>
        await ExecuteAsync($"/TailRisk/Correlations/{correlationId}", Method.Delete);

    public async Task<PortfolioTailDto> AggregatePortfolioAsync(PortfolioTailRequest request) =>
        Read<PortfolioTailDto>(await ExecuteAsync("/TailRisk/Portfolio", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskAppetiteTailLimitsDto> GetAppetiteLimitsAsync(int appetiteId) =>
        Read<RiskAppetiteTailLimitsDto>(await ExecuteAsync($"/RiskAppetites/{appetiteId}/TailLimits", Method.Get));

    public async Task<RiskAppetiteTailLimitsDto> SaveAppetiteLimitsAsync(int appetiteId,
        RiskAppetiteTailLimitsRequest request) =>
        Read<RiskAppetiteTailLimitsDto>(await ExecuteAsync($"/RiskAppetites/{appetiteId}/TailLimits", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteAppetiteLimitsAsync(int appetiteId) =>
        await ExecuteAsync($"/RiskAppetites/{appetiteId}/TailLimits", Method.Delete);

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
