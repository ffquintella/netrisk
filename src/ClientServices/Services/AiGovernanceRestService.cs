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
using Model.AiGovernance;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for AI governance (Stage 9.12, S53 §7).
///
/// Every call goes through the error-reporting client, as the catalogue and third-party clients do: a refusal (a write
/// outside the model's scope, a retired model, a typed override rate, a text carrying a personal value) answers with a
/// sentence written for the person, and the reliable client would otherwise throw on the status before the body could be
/// read.
/// </summary>
public class AiGovernanceRestService(IRestService restService) : RestServiceBase(restService), IAiGovernanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private const string Root = "/AiModels";

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    // --- the inventory ----------------------------------------------------------------------

    public async Task<List<AiModelSummaryDto>> GetModelsAsync(AiModelStatus? status = null, bool includeRetired = false,
        bool withFindingsOnly = false)
    {
        var query = new List<(string, string)>();
        if (status is { } s) query.Add(("status", Id((int)s)));
        if (includeRetired) query.Add(("includeRetired", "true"));
        if (withFindingsOnly) query.Add(("withFindings", "true"));

        return Read<List<AiModelSummaryDto>>(await ExecuteAsync(Root, Method.Get, null, query.ToArray()));
    }

    public async Task<AiModelDto> GetModelAsync(int modelId) =>
        Read<AiModelDto>(await ExecuteAsync($"{Root}/{Id(modelId)}", Method.Get));

    public async Task<List<DAL.Entities.AuditLog>> GetHistoryAsync(int modelId, int limit = 500) =>
        Read<List<DAL.Entities.AuditLog>>(await ExecuteAsync($"{Root}/{Id(modelId)}/History", Method.Get, null,
            ("limit", Id(limit))));

    public async Task<AiModelDto> CreateModelAsync(AiModelRequest request) =>
        Read<AiModelDto>(await ExecuteAsync(Root, Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<AiModelDto> UpdateModelAsync(int modelId, AiModelRequest request) =>
        Read<AiModelDto>(await ExecuteAsync($"{Root}/{Id(modelId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<AiModelDto> RetireModelAsync(int modelId, AiGovernanceReasonRequest request) =>
        Read<AiModelDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Retire", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<AiModelDto> SetDataAsync(int modelId, AiModelDataRequest request) =>
        Read<AiModelDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Data", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- metrics and overrides --------------------------------------------------------------

    public async Task<List<AiModelReadingDto>> GetReadingsAsync(int modelId, AiModelMetric? metric = null,
        bool includeVoided = false)
    {
        var query = new List<(string, string)>();
        if (metric is { } m) query.Add(("metric", Id((int)m)));
        if (includeVoided) query.Add(("includeVoided", "true"));

        return Read<List<AiModelReadingDto>>(await ExecuteAsync($"{Root}/{Id(modelId)}/Readings", Method.Get, null,
            query.ToArray()));
    }

    public async Task<AiModelReadingDto> RecordReadingAsync(int modelId, AiModelReadingRequest request) =>
        Read<AiModelReadingDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Readings", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<AiModelReadingDto> VoidReadingAsync(int modelId, int readingId, AiGovernanceReasonRequest request) =>
        Read<AiModelReadingDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Readings/{Id(readingId)}/Void", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<List<AiModelOverrideDto>> GetOverridesAsync(int modelId, bool includeVoided = false) =>
        Read<List<AiModelOverrideDto>>(await ExecuteAsync($"{Root}/{Id(modelId)}/Overrides", Method.Get, null,
            includeVoided ? new[] { ("includeVoided", "true") } : []));

    public async Task<AiModelOverrideDto> RecordOverrideAsync(int modelId, AiModelOverrideRequest request) =>
        Read<AiModelOverrideDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Overrides", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<AiModelOverrideDto> VoidOverrideAsync(int modelId, int overrideId, AiGovernanceReasonRequest request) =>
        Read<AiModelOverrideDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Overrides/{Id(overrideId)}/Void", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- the register's risks ---------------------------------------------------------------

    public async Task<RiskAiModelsDto> GetRiskModelsAsync(int riskId) =>
        Read<RiskAiModelsDto>(await ExecuteAsync($"{Root}/Risks/{Id(riskId)}", Method.Get));

    public async Task<RiskAiModelsDto> LinkRiskAsync(int modelId, int riskId, AiModelRiskLinkRequest? request = null) =>
        Read<RiskAiModelsDto>(await ExecuteAsync($"{Root}/{Id(modelId)}/Risks/{Id(riskId)}", Method.Put,
            request ?? new AiModelRiskLinkRequest()));

    public async Task UnlinkRiskAsync(int modelId, int riskId) =>
        await ExecuteAsync($"{Root}/{Id(modelId)}/Risks/{Id(riskId)}", Method.Delete);

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
    /// 404 is a not-found; 400, 403, 409 and 422 carry a message written for a person and are passed through; anything
    /// else that is not a success is a generic failure.
    /// </summary>
    private void Reject(string route, Method method, HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NotFound)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.Forbidden)
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
