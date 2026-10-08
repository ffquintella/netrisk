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
using Model.DataCatalogue;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the LGPD data catalogue (Stage 9.11, S52 §7).
///
/// Every call goes through the error-reporting client, as the third-party client does: a refusal (a write without global
/// scope, a requirement in use, a frozen RIPD, a text carrying a personal value) answers with a sentence written for the
/// person, and the reliable client would otherwise throw on the status before the body could be read.
/// </summary>
public class DataCatalogueRestService(IRestService restService) : RestServiceBase(restService), IDataCatalogueService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private const string Root = "/DataCatalogue";

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    // --- data records -----------------------------------------------------------------------

    public async Task<List<DataRecordSummaryDto>> GetRecordsAsync(bool withFindingsOnly = false) =>
        Read<List<DataRecordSummaryDto>>(await ExecuteAsync($"{Root}/Records", Method.Get, null,
            withFindingsOnly ? new[] { ("withFindings", "true") } : []));

    public async Task<DataRecordDto> GetRecordAsync(int entityId) =>
        Read<DataRecordDto>(await ExecuteAsync($"{Root}/Records/{Id(entityId)}", Method.Get));

    public async Task<List<DAL.Entities.AuditLog>> GetRecordHistoryAsync(int entityId, int limit = 500) =>
        Read<List<DAL.Entities.AuditLog>>(await ExecuteAsync($"{Root}/Records/{Id(entityId)}/History", Method.Get, null,
            ("limit", Id(limit))));

    public async Task<DataRecordDto> SaveRecordAsync(int entityId, DataCatalogueEntryRequest request) =>
        Read<DataRecordDto>(await ExecuteAsync($"{Root}/Records/{Id(entityId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- legal requirements -----------------------------------------------------------------

    public async Task<List<LegalRequirementDto>> GetRequirementsAsync() =>
        Read<List<LegalRequirementDto>>(await ExecuteAsync($"{Root}/Requirements", Method.Get));

    public async Task<List<DAL.Entities.AuditLog>> GetRequirementHistoryAsync(int requirementId, int limit = 500) =>
        Read<List<DAL.Entities.AuditLog>>(await ExecuteAsync($"{Root}/Requirements/{Id(requirementId)}/History",
            Method.Get, null, ("limit", Id(limit))));

    public async Task<LegalRequirementDto> CreateRequirementAsync(LegalRequirementRequest request) =>
        Read<LegalRequirementDto>(await ExecuteAsync($"{Root}/Requirements", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<LegalRequirementDto> UpdateRequirementAsync(int requirementId, LegalRequirementRequest request) =>
        Read<LegalRequirementDto>(await ExecuteAsync($"{Root}/Requirements/{Id(requirementId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task DeleteRequirementAsync(int requirementId) =>
        await ExecuteAsync($"{Root}/Requirements/{Id(requirementId)}", Method.Delete);

    // --- RIPD / DPIA ------------------------------------------------------------------------

    public async Task<List<DpiaSummaryDto>> GetDpiasAsync(DpiaStatus? status = null) =>
        Read<List<DpiaSummaryDto>>(await ExecuteAsync($"{Root}/Dpias", Method.Get, null,
            status is { } s ? new[] { ("status", Id((int)s)) } : []));

    public async Task<DpiaDto> GetDpiaAsync(int dpiaId) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}", Method.Get));

    public async Task<List<DAL.Entities.AuditLog>> GetDpiaHistoryAsync(int dpiaId, int limit = 500) =>
        Read<List<DAL.Entities.AuditLog>>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}/History", Method.Get, null,
            ("limit", Id(limit))));

    public async Task<DpiaDto> CreateDpiaAsync(DpiaRequest request) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<DpiaDto> UpdateDpiaAsync(int dpiaId, DpiaRequest request) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<DpiaDto> LinkDpiaAsync(int dpiaId, int entityId) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}/Links/{Id(entityId)}", Method.Put));

    public async Task UnlinkDpiaAsync(int dpiaId, int entityId) =>
        await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}/Links/{Id(entityId)}", Method.Delete);

    public async Task<DpiaDto> ApproveDpiaAsync(int dpiaId) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}/Approve", Method.Post));

    public async Task<DpiaDto> RetireDpiaAsync(int dpiaId, DpiaRetireRequest request) =>
        Read<DpiaDto>(await ExecuteAsync($"{Root}/Dpias/{Id(dpiaId)}/Retire", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- the requirements of a risk ---------------------------------------------------------

    public async Task<RiskComplianceDto> GetRiskComplianceAsync(int riskId) =>
        Read<RiskComplianceDto>(await ExecuteAsync($"{Root}/Risks/{Id(riskId)}", Method.Get));

    public async Task<RiskComplianceDto> LinkRiskRequirementAsync(int riskId, int requirementId,
        RiskLegalRequirementRequest? request = null) =>
        Read<RiskComplianceDto>(await ExecuteAsync($"{Root}/Risks/{Id(riskId)}/Requirements/{Id(requirementId)}",
            Method.Put, request ?? new RiskLegalRequirementRequest()));

    public async Task UnlinkRiskRequirementAsync(int riskId, int requirementId) =>
        await ExecuteAsync($"{Root}/Risks/{Id(riskId)}/Requirements/{Id(requirementId)}", Method.Delete);

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
