using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using Model.RiskIndex;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the index's configuration API (S40 §8.4, §8.9; M55). Like <see cref="RiskIndexRestService"/> it uses the
/// error-reporting client: every refusal carries a stable <c>error</c> code the screens translate, and a write must never be
/// retried behind the user's back (a retried <c>Approve</c> or <c>Submit</c> would answer <c>invalid_transition</c>).
/// </summary>
public class RiskIndexConfigRestService(IRestService restService, IAuthenticationService authenticationService)
    : RestServiceBase(restService), IRiskIndexConfigService
{
    private const string Profiles = "/RiskIndex/Profiles";

    /// <summary>The API's MVC defaults: camelCase names, enums as numbers.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RiskIndexParameterSchemaDto> GetSchemaAsync(CancellationToken ct = default) =>
        Read<RiskIndexParameterSchemaDto>(await SendAsync(Method.Get, $"{Profiles}/Schema", ct));

    public async Task<IReadOnlyList<RiskIndexPresetDto>> GetPresetsAsync(CancellationToken ct = default) =>
        Read<List<RiskIndexPresetDto>>(await SendAsync(Method.Get, $"{Profiles}/Presets", ct));

    public async Task<RiskIndexProfileDto?> GetActiveAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(Method.Get, $"{Profiles}/Active", ct);
        return response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(response.Content)
            ? null
            : Read<RiskIndexProfileDto>(response);
    }

    public async Task<RiskIndexProfileDto> CreateDraftAsync(RiskIndexProfileCreateRequest request, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Post, Profiles, ct, request));

    public async Task<RiskIndexProfileDto> UpdateDraftAsync(int id, int revision, RiskIndexProfileUpdateRequest request,
        CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Put, $"{Profiles}/{Number(id)}", ct, request,
            ("If-Match", "\"" + Number(revision) + "\"")));

    public async Task DeleteDraftAsync(int id, CancellationToken ct = default) =>
        await SendAsync(Method.Delete, $"{Profiles}/{Number(id)}", ct);

    public async Task<RiskIndexValidationResultDto> ValidateAsync(int id, CancellationToken ct = default) =>
        Read<RiskIndexValidationResultDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Validate", ct));

    public async Task<RiskIndexPreviewStatusDto> RequestPreviewAsync(int id, CancellationToken ct = default) =>
        Read<RiskIndexPreviewStatusDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Preview", ct));

    public async Task<RiskIndexPreviewDto> GetPreviewAsync(int id, CancellationToken ct = default) =>
        Read<RiskIndexPreviewDto>(await SendAsync(Method.Get, $"{Profiles}/{Number(id)}/Preview", ct));

    public async Task<RiskIndexProfileDto> SubmitAsync(int id, RiskIndexSubmitRequest request, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Submit", ct, request));

    public async Task<RiskIndexApprovalEligibilityDto> GetApprovalEligibilityAsync(int id, CancellationToken ct = default) =>
        Read<RiskIndexApprovalEligibilityDto>(await SendAsync(Method.Get, $"{Profiles}/{Number(id)}/ApprovalEligibility", ct));

    public async Task<RiskIndexProfileDto> ApproveAsync(int id, RiskIndexApproveRequest request, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Approve", ct, request));

    public async Task<RiskIndexProfileDto> RejectAsync(int id, RiskIndexRejectRequest request, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Reject", ct, request));

    public async Task<RiskIndexProfileDto> RetireAsync(int id, RiskIndexRetireRequest request, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Retire", ct, request));

    public async Task AttachEvidenceAsync(int id, int fileId, CancellationToken ct = default) =>
        await SendAsync(Method.Post, $"{Profiles}/{Number(id)}/Attachments", ct, new RiskIndexAttachRequest(fileId));

    public async Task<IReadOnlyList<FileListing>> GetEvidenceAsync(int id, CancellationToken ct = default) =>
        Read<List<FileListing>>(await SendAsync(Method.Get, $"{Profiles}/{Number(id)}/Attachments", ct));

    public async Task<RiskIndexBridgeDto?> GetBridgeAsync(int id, CancellationToken ct = default)
    {
        try
        {
            return Read<RiskIndexBridgeDto>(await SendAsync(Method.Get, $"{Profiles}/{Number(id)}/Bridge", ct));
        }
        catch (InvalidHttpRequestException ex) when (ex is { StatusCode: HttpStatusCode.NotFound, ErrorCode: "bridge_not_found" })
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AuditLog>> GetHistoryAsync(int id, int limit = 500, CancellationToken ct = default) =>
        Read<List<AuditLog>>(await SendAsync(Method.Get, $"{Profiles}/{Number(id)}/History", ct, null, query: ("limit", Number(limit))));

    public async Task<RiskIndexPublicationDto> SetPublicationAsync(RiskIndexPublicationState state, CancellationToken ct = default) =>
        Read<RiskIndexPublicationDto>(await SendAsync(Method.Put, "/RiskIndex/Publication", ct,
            new RiskIndexPublicationRequest(state)));

    // --- transport --------------------------------------------------------------------------

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static T Read<T>(RestResponse response)
    {
        var route = response.Request?.Resource ?? "";
        var method = response.Request?.Method.ToString().ToUpperInvariant() ?? "GET";
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException("The server answered with an empty body.", route, method)
                { StatusCode = response.StatusCode };

        try
        {
            return JsonSerializer.Deserialize<T>(response.Content, JsonOptions)
                   ?? throw new InvalidHttpRequestException("The server answered with an unreadable body.", route, method)
                       { StatusCode = response.StatusCode };
        }
        catch (JsonException ex)
        {
            throw new InvalidHttpRequestException($"The server answered {route} with an unreadable body: {ex.Message}",
                route, method) { StatusCode = response.StatusCode };
        }
    }

    /// <summary>One call: an optional JSON body (serialized as the API reads it), extra headers and query parameters.</summary>
    private async Task<RestResponse> SendAsync(Method method, string route, CancellationToken ct, object? body = null,
        (string Name, string Value)? header = null, (string Name, string Value)? query = null)
    {
        using var client = RestService.GetClient(reportErrorResponses: true);
        var request = new RestRequest(route) { Method = method };
        if (body is not null) request.AddStringBody(JsonSerializer.Serialize(body, body.GetType(), JsonOptions), ContentType.Json);
        if (header is { } h) request.AddHeader(h.Name, h.Value);
        if (query is { } q) request.AddQueryParameter(q.Name, q.Value);

        RestResponse response;
        try
        {
            response = await client.ExecuteAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Method} {Route}: {Message}", method, route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }

        if (response.StatusCode == 0)
        {
            Logger.Error("{Method} {Route} did not reach the server: {Message}", method, route, response.ErrorMessage);
            throw new RestComunicationException($"Error calling {route}",
                response.ErrorException ?? new HttpRequestException(response.ErrorMessage));
        }

        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        if (status == HttpStatusCode.Unauthorized) authenticationService.DiscardAuthenticationToken();

        var (code, message) = RiskIndexRestService.ReadError(response.Content);
        Logger.Warning("{Method} {Route} refused with {Status} {Code}", method, route, (int)status, code ?? "-");

        if (status == HttpStatusCode.NotFound && code is null)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        var verb = method.ToString().ToUpperInvariant();
        throw new InvalidHttpRequestException(string.IsNullOrWhiteSpace(message) ? $"Error calling {route}" : message, route,
            verb) { StatusCode = status, ErrorCode = code };
    }
}
