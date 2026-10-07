using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Continuity;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for business impact analysis and continuity (Stage 9.3, S43 §7).
///
/// Every call goes through the error-reporting client rather than the reliable one, as the chain client
/// does: the writes answer 400/403/409/422 with a sentence meant for the user ("The RTO cannot exceed the
/// MTPD/MAO…", "global_scope"), and the reliable client throws on any non-2xx before the body can be read.
/// </summary>
public class ContinuityRestService(IRestService restService)
    : RestServiceBase(restService), IContinuityService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<ContinuitySubjectDto>> GetSubjectsAsync() =>
        Read<List<ContinuitySubjectDto>>(await ExecuteAsync("/Continuity/Subjects", Method.Get));

    public async Task<ContinuityProfileDto> GetProfileAsync(int entityId) =>
        Read<ContinuityProfileDto>(await ExecuteAsync($"/Continuity/Subjects/{entityId}", Method.Get));

    public async Task<BusinessImpactAnalysisDto> SaveBiaAsync(int entityId, BusinessImpactAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Read<BusinessImpactAnalysisDto>(
            await ExecuteAsync($"/Continuity/Subjects/{entityId}/Bia", Method.Put, request));
    }

    public async Task DeleteBiaAsync(int entityId) =>
        await ExecuteAsync($"/Continuity/Subjects/{entityId}/Bia", Method.Delete);

    public async Task<BiaDependencyDto> AddDependencyAsync(int entityId, BiaDependencyCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Read<BiaDependencyDto>(
            await ExecuteAsync($"/Continuity/Subjects/{entityId}/Dependencies", Method.Post, request));
    }

    public async Task DeleteDependencyAsync(int entityId, int dependencyId) =>
        await ExecuteAsync($"/Continuity/Subjects/{entityId}/Dependencies/{dependencyId}", Method.Delete);

    public async Task<List<RestorationTestDto>> GetRestorationTestsAsync(int entityId) =>
        Read<List<RestorationTestDto>>(
            await ExecuteAsync($"/Continuity/Subjects/{entityId}/RestorationTests", Method.Get));

    public async Task<RestorationTestDto> RecordRestorationTestAsync(int entityId, RestorationTestCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Read<RestorationTestDto>(
            await ExecuteAsync($"/Continuity/Subjects/{entityId}/RestorationTests", Method.Post, request));
    }

    public async Task<RestorationTestDto> VoidRestorationTestAsync(int entityId, int testId, string reason) =>
        Read<RestorationTestDto>(await ExecuteAsync(
            $"/Continuity/Subjects/{entityId}/RestorationTests/{testId}/Void", Method.Post,
            new RestorationTestVoidRequest { Reason = reason }));

    public async Task<RestorationVerificationMetricDto> GetRestorationVerificationMetricAsync() =>
        Read<RestorationVerificationMetricDto>(
            await ExecuteAsync("/Continuity/Metrics/RestorationVerification", Method.Get));

    public async Task<ContinuitySettingsDto> GetSettingsAsync() =>
        Read<ContinuitySettingsDto>(await ExecuteAsync("/Continuity/Settings", Method.Get));

    public async Task<ContinuitySettingsDto> SaveSettingsAsync(ContinuitySettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Read<ContinuitySettingsDto>(await ExecuteAsync("/Continuity/Settings", Method.Put, request));
    }

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
