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
using Model.ThirdParties;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the third-party register (Stage 9.10, S51 §7).
///
/// Every call goes through the error-reporting client, as the decision-cycle client does: a refusal (a supplier in use,
/// a terminated relationship, a voided assessment, a malformed SBOM) answers with a sentence written for the person, and
/// the reliable client would otherwise throw on the status before the body could be read.
/// </summary>
public class ThirdPartiesRestService(IRestService restService) : RestServiceBase(restService), IThirdPartiesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Path(int thirdPartyId) => $"/ThirdParties/{Id(thirdPartyId)}";

    // --- reads ------------------------------------------------------------------------------

    public async Task<List<ThirdPartySummaryDto>> GetThirdPartiesAsync(ThirdPartyStatus? status = null,
        bool includeTerminated = false)
    {
        var query = new List<(string, string)>();
        if (status is { } s) query.Add(("status", Id((int)s)));
        if (includeTerminated) query.Add(("includeTerminated", "true"));

        return Read<List<ThirdPartySummaryDto>>(await ExecuteAsync("/ThirdParties", Method.Get, null, query.ToArray()));
    }

    public async Task<ThirdPartyDto> GetThirdPartyAsync(int thirdPartyId) =>
        Read<ThirdPartyDto>(await ExecuteAsync(Path(thirdPartyId), Method.Get));

    public async Task<ThirdPartyConcentrationReportDto> GetConcentrationAsync() =>
        Read<ThirdPartyConcentrationReportDto>(await ExecuteAsync("/ThirdParties/Concentration", Method.Get));

    public async Task<List<EntityThirdPartyDto>> GetByEntityAsync(int entityId) =>
        Read<List<EntityThirdPartyDto>>(await ExecuteAsync($"/ThirdParties/ByEntity/{Id(entityId)}", Method.Get));

    public async Task<ThirdPartyAssessmentDto> GetAssessmentAsync(int thirdPartyId, int assessmentId) =>
        Read<ThirdPartyAssessmentDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Assessments/{Id(assessmentId)}", Method.Get));

    public async Task<ThirdPartySbomDto> GetSbomAsync(int thirdPartyId, int sbomId) =>
        Read<ThirdPartySbomDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Sboms/{Id(sbomId)}", Method.Get));

    public async Task<List<DAL.Entities.AuditLog>> GetHistoryAsync(int thirdPartyId, int limit = 500) =>
        Read<List<DAL.Entities.AuditLog>>(await ExecuteAsync($"{Path(thirdPartyId)}/History", Method.Get, null,
            ("limit", Id(limit))));

    // --- writes -----------------------------------------------------------------------------

    public async Task<ThirdPartyDto> CreateAsync(ThirdPartyRequest request) =>
        Read<ThirdPartyDto>(await ExecuteAsync("/ThirdParties", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartyDto> UpdateAsync(int thirdPartyId, ThirdPartyRequest request) =>
        Read<ThirdPartyDto>(await ExecuteAsync(Path(thirdPartyId), Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteAsync(int thirdPartyId) =>
        await ExecuteAsync(Path(thirdPartyId), Method.Delete);

    public async Task<ThirdPartyDto> LinkAsync(int thirdPartyId, int entityId, ThirdPartyLinkRequest? request = null) =>
        Read<ThirdPartyDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Links/{Id(entityId)}", Method.Put,
            request ?? new ThirdPartyLinkRequest()));

    public async Task UnlinkAsync(int thirdPartyId, int entityId) =>
        await ExecuteAsync($"{Path(thirdPartyId)}/Links/{Id(entityId)}", Method.Delete);

    public async Task<ThirdPartyDto> SetSubprocessorsAsync(int thirdPartyId, ThirdPartySubprocessorsRequest request) =>
        Read<ThirdPartyDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Subprocessors", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartyDto> SetDataLocationsAsync(int thirdPartyId, ThirdPartyDataLocationsRequest request) =>
        Read<ThirdPartyDto>(await ExecuteAsync($"{Path(thirdPartyId)}/DataLocations", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartyAssessmentDto> RecordAssessmentAsync(int thirdPartyId, ThirdPartyAssessmentRequest request) =>
        Read<ThirdPartyAssessmentDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Assessments", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartyAssessmentDto> ReplaceAnswersAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentAnswersRequest request) =>
        Read<ThirdPartyAssessmentDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Assessments/{Id(assessmentId)}/Answers",
            Method.Put, request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartyAssessmentDto> VoidAssessmentAsync(int thirdPartyId, int assessmentId,
        ThirdPartyAssessmentVoidRequest request) =>
        Read<ThirdPartyAssessmentDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Assessments/{Id(assessmentId)}/Void",
            Method.Post, request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<ThirdPartySbomDto> ImportSbomAsync(int thirdPartyId, ThirdPartySbomRequest request) =>
        Read<ThirdPartySbomDto>(await ExecuteAsync($"{Path(thirdPartyId)}/Sboms", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteSbomAsync(int thirdPartyId, int sbomId) =>
        await ExecuteAsync($"{Path(thirdPartyId)}/Sboms/{Id(sbomId)}", Method.Delete);

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
    /// 404 is a not-found; 400, 403, 409, 413 and 422 carry a message written for a person (or say the SBOM is too
    /// large) and are passed through; anything else that is not a success is a generic failure.
    /// </summary>
    private void Reject(string route, Method method, HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NotFound)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge
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
