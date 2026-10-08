using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using Model.Exceptions;
using Model.Monitoring;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for MIGR-TI/IA Phase 7 monitoring — key risk indicators, their readings and links, the mandatory
/// reassessment triggers and the methodology's metrics panel (Stage 9.8, S49 §7).
///
/// Every call goes through the error-reporting client, as the tail-risk client does: a refusal (a retired KRI, a KRI of
/// another entity, an incident that already has its event) answers with a sentence written for the person, and the
/// reliable client would otherwise throw on the status before the body could be read.
/// </summary>
public class MonitoringRestService(IRestService restService) : RestServiceBase(restService), IMonitoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public async Task<List<KriDto>> GetKrisAsync(bool includeRetired = false) =>
        Read<List<KriDto>>(includeRetired
            ? await ExecuteAsync("/Monitoring/Kris", Method.Get, null, ("includeRetired", "true"))
            : await ExecuteAsync("/Monitoring/Kris", Method.Get));

    public async Task<KriDetailDto> GetKriAsync(int kriId) =>
        Read<KriDetailDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}", Method.Get));

    public async Task<KriDto> CreateKriAsync(KriRequest request) =>
        Read<KriDto>(await ExecuteAsync("/Monitoring/Kris", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<KriDto> UpdateKriAsync(int kriId, KriRequest request) =>
        Read<KriDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<KriDto> RetireKriAsync(int kriId) =>
        Read<KriDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}/Retire", Method.Post));

    public async Task<KriDetailDto> RecordReadingAsync(int kriId, KriReadingRequest request) =>
        Read<KriDetailDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}/Readings", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<KriDetailDto> VoidReadingAsync(int kriId, int readingId, KriReadingVoidRequest request) =>
        Read<KriDetailDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}/Readings/{readingId}/Void", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<KriDetailDto> LinkRiskAsync(int kriId, int riskId) =>
        Read<KriDetailDto>(await ExecuteAsync($"/Monitoring/Kris/{kriId}/Risks/{riskId}", Method.Put));

    public async Task UnlinkRiskAsync(int kriId, int riskId) =>
        await ExecuteAsync($"/Monitoring/Kris/{kriId}/Risks/{riskId}", Method.Delete);

    public async Task<List<ReassessmentEventDto>> GetEventsAsync(ReassessmentTriggerType? type = null, int? limit = null)
    {
        var query = new List<(string, string)>();
        if (type is { } t) query.Add(("type", ((int)t).ToString(CultureInfo.InvariantCulture)));
        if (limit is { } l) query.Add(("limit", l.ToString(CultureInfo.InvariantCulture)));

        return Read<List<ReassessmentEventDto>>(
            await ExecuteAsync("/Monitoring/Reassessment/Events", Method.Get, null, query.ToArray()));
    }

    public async Task<ReassessmentEventDto> DeclareEventAsync(ReassessmentEventRequest request) =>
        Read<ReassessmentEventDto>(await ExecuteAsync("/Monitoring/Reassessment/Events", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ReassessmentEventDto> AddEventRisksAsync(int eventId, ReassessmentRisksRequest request) =>
        Read<ReassessmentEventDto>(await ExecuteAsync($"/Monitoring/Reassessment/Events/{eventId}/Risks", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<List<ReassessmentTriggerDto>> GetTriggersAsync(int? riskId = null, bool pendingOnly = false)
    {
        var query = new List<(string, string)>();
        if (riskId is { } id) query.Add(("riskId", id.ToString(CultureInfo.InvariantCulture)));
        if (pendingOnly) query.Add(("pendingOnly", "true"));

        return Read<List<ReassessmentTriggerDto>>(
            await ExecuteAsync("/Monitoring/Reassessment/Triggers", Method.Get, null, query.ToArray()));
    }

    public async Task<MethodologyMetricsDto> GetMetricsAsync() =>
        Read<MethodologyMetricsDto>(await ExecuteAsync("/Monitoring/Metrics", Method.Get));

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
