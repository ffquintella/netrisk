using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using Model.Exceptions;
using Model.RiskIndex;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the Cyber Risk Overview's read API (S40 §8.2, §8.9; M54). Reads go through the error-reporting client
/// rather than the reliable one: a refusal carries a stable <c>error</c> code the screen translates, and the reliable
/// client's undelayed retries would hammer a <c>503 derived_computation_busy</c> the server asked to be retried later.
/// </summary>
public class RiskIndexRestService(IRestService restService, IAuthenticationService authenticationService)
    : RestServiceBase(restService), IRiskIndexService
{
    private const string Base = "/RiskIndex";

    /// <summary>The API's MVC defaults: camelCase names, enums as numbers, <c>DateOnly</c> as <c>yyyy-MM-dd</c>.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RiskIndexLatestDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var response = await GetAsync($"{Base}/Snapshots/Latest", ct);
        return response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(response.Content)
            ? null
            : Read<RiskIndexLatestDto>(response);
    }

    public async Task<IReadOnlyList<DateOnly>> GetSnapshotDatesAsync(DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default) =>
        Read<List<DateOnly>>(await GetAsync($"{Base}/Snapshots/Dates", ct, ("from", Day(from)), ("to", Day(to))));

    public async Task<RiskIndexOverviewDto> GetOverviewAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default) =>
        Read<RiskIndexOverviewDto>(await GetAsync($"{Base}/Overview", ct, ("node", node), ("date", Day(date))));

    public async Task<IReadOnlyList<RiskIndexCategoryDto>> GetCategoriesAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default) =>
        Read<List<RiskIndexCategoryDto>>(await GetAsync($"{Base}/Categories", ct, ("node", node), ("date", Day(date))));

    public async Task<RiskIndexTrendDto> GetTrendAsync(string? node = null, int window = 90,
        RiskIndexCategory series = RiskIndexCategory.Icr, DateOnly? date = null, CancellationToken ct = default) =>
        Read<RiskIndexTrendDto>(await GetAsync($"{Base}/Trend", ct, ("node", node), ("window", Number(window)),
            ("series", Number((int)series)), ("date", Day(date))));

    public async Task<RiskIndexNodeChildrenDto> GetNodesAsync(string? parent = null,
        RiskIndexDimension dimension = RiskIndexDimension.Unit, string? filters = null, DateOnly? date = null,
        CancellationToken ct = default) =>
        Read<RiskIndexNodeChildrenDto>(await GetAsync($"{Base}/Nodes", ct, ("parent", parent),
            ("dimension", Number((int)dimension)), ("filters", filters), ("date", Day(date))));

    public async Task<(IReadOnlyList<RiskIndexObjectRowDto> Rows, int Total)> GetObjectsAsync(string? node = null, DateOnly? date = null, string? filters = null,
        string? sorts = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var response = await GetAsync($"{Base}/Objects", ct, ("node", node), ("date", Day(date)), ("filters", filters),
            ("sorts", sorts), ("page", Number(page)), ("pageSize", Number(pageSize)));
        var rows = Read<List<RiskIndexObjectRowDto>>(response);

        var header = response.Headers?.FirstOrDefault(h =>
            string.Equals(h.Name, "X-Total-Count", StringComparison.OrdinalIgnoreCase))?.Value;
        var total = int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : rows.Count;
        return (rows, total);
    }

    public async Task<RiskIndexObjectDetailDto> GetObjectAsync(RiskIndexObjectKind kind, string reference,
        string? node = null, DateOnly? date = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("An object reference is required.", nameof(reference));
        var route = $"{Base}/Objects/{Number((int)kind)}/{Uri.EscapeDataString(reference)}";
        return Read<RiskIndexObjectDetailDto>(await GetAsync(route, ct, ("date", Day(date)), ("node", node)));
    }

    public async Task<RiskIndexInventoryDto> GetInventoryAsync(string? node = null, RiskIndexTabGroup? tab = null,
        DateOnly? date = null, CancellationToken ct = default) =>
        Read<RiskIndexInventoryDto>(await GetAsync($"{Base}/Inventory", ct, ("node", node),
            ("tab", tab is { } t ? Number((int)t) : null), ("date", Day(date))));

    public async Task<RiskIndexMonthlySummaryDto> GetMonthlySummaryAsync(string? node = null, RiskIndexTabGroup? tab = null,
        int months = 6, DateOnly? date = null, CancellationToken ct = default) =>
        Read<RiskIndexMonthlySummaryDto>(await GetAsync($"{Base}/MonthlySummary", ct, ("node", node),
            ("tab", tab is { } t ? Number((int)t) : null), ("months", Number(months)), ("date", Day(date))));

    public async Task<IReadOnlyList<RiskIndexRiskFactorDto>> GetRiskFactorsAsync(string? node = null, RiskIndexTabGroup? tab = null,
        DateOnly? date = null, CancellationToken ct = default) =>
        Read<List<RiskIndexRiskFactorDto>>(await GetAsync($"{Base}/RiskFactors", ct, ("node", node),
            ("tab", tab is { } t ? Number((int)t) : null), ("date", Day(date))));

    public async Task<RiskIndexDimensionsDto> GetDimensionsAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default) =>
        Read<RiskIndexDimensionsDto>(await GetAsync($"{Base}/Dimensions", ct, ("node", node), ("date", Day(date))));

    public async Task<RiskIndexChangesDto> GetChangesAsync(string? node = null, DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default) =>
        Read<RiskIndexChangesDto>(await GetAsync($"{Base}/Changes", ct, ("node", node), ("to", Day(to)), ("from", Day(from))));

    public async Task<RiskIndexContributionsDto> GetContributionsAsync(string? node = null, DateOnly? date = null,
        RiskIndexCategory? category = null, CancellationToken ct = default) =>
        Read<RiskIndexContributionsDto>(await GetAsync($"{Base}/Contributions", ct, ("node", node), ("date", Day(date)),
            ("category", category is { } c ? Number((int)c) : null)));

    public async Task<RiskIndexGovernanceIndicatorsDto> GetGovernanceIndicatorsAsync(string? node = null,
        DateOnly? date = null, CancellationToken ct = default) =>
        Read<RiskIndexGovernanceIndicatorsDto>(await GetAsync($"{Base}/GovernanceIndicators", ct, ("node", node),
            ("date", Day(date))));

    public async Task<RiskIndexQualityDto> GetQualityAsync(string? node = null, DateOnly? date = null,
        CancellationToken ct = default) =>
        Read<RiskIndexQualityDto>(await GetAsync($"{Base}/Quality", ct, ("node", node), ("date", Day(date))));

    public async Task<RiskIndexPublicationDto> GetPublicationAsync(CancellationToken ct = default) =>
        Read<RiskIndexPublicationDto>(await GetAsync($"{Base}/Publication", ct));

    public async Task<IReadOnlyList<RiskIndexProfileSummaryDto>> GetProfilesAsync(CancellationToken ct = default) =>
        Read<List<RiskIndexProfileSummaryDto>>(await GetAsync($"{Base}/Profiles", ct));

    public async Task<RiskIndexProfileDto> GetProfileAsync(int id, CancellationToken ct = default) =>
        Read<RiskIndexProfileDto>(await GetAsync($"{Base}/Profiles/{Number(id)}", ct));

    public async Task<RiskIndexProfileDiffDto> GetProfileDiffAsync(int a, int b, CancellationToken ct = default) =>
        Read<RiskIndexProfileDiffDto>(await GetAsync($"{Base}/Profiles/Diff", ct, ("a", Number(a)), ("b", Number(b))));

    // --- transport --------------------------------------------------------------------------

    private static string? Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static T Read<T>(RestResponse response)
    {
        var route = response.Request?.Resource ?? "";
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException("The server answered with an empty body.", route, "GET")
                { StatusCode = response.StatusCode };

        try
        {
            return JsonSerializer.Deserialize<T>(response.Content, JsonOptions)
                   ?? throw new InvalidHttpRequestException("The server answered with an unreadable body.", route, "GET")
                       { StatusCode = response.StatusCode };
        }
        catch (JsonException ex)
        {
            throw new InvalidHttpRequestException($"The server answered {route} with an unreadable body: {ex.Message}",
                route, "GET") { StatusCode = response.StatusCode };
        }
    }

    /// <summary>One GET; query parameters with a null value are left out, so the server applies its own default.</summary>
    private async Task<RestResponse> GetAsync(string route, CancellationToken ct, params (string Name, string? Value)[] query)
    {
        using var client = RestService.GetClient(reportErrorResponses: true);
        var request = new RestRequest(route) { Method = Method.Get };
        foreach (var (name, value) in query)
            if (value is not null) request.AddQueryParameter(name, value);

        RestResponse response;
        try
        {
            response = await client.ExecuteAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error calling {Route}: {Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }

        if (response.StatusCode == 0)
        {
            Logger.Error("GET {Route} did not reach the server: {Message}", route, response.ErrorMessage);
            throw new RestComunicationException($"Error calling {route}",
                response.ErrorException ?? new HttpRequestException(response.ErrorMessage));
        }

        if (response.IsSuccessStatusCode) return response;
        throw Refusal(route, response);
    }

    /// <summary>
    /// The refusal as the screen needs it (S40 §8.1, §8.9): the status, the stable <c>error</c> code and the server's sentence.
    /// A 401 discards the token first; a 404 that names no code (a route the server does not have) is a not-found.
    /// </summary>
    private Exception Refusal(string route, RestResponse response)
    {
        var status = response.StatusCode;
        if (status == HttpStatusCode.Unauthorized) authenticationService.DiscardAuthenticationToken();

        var (code, message) = ReadError(response.Content);
        Logger.Warning("GET {Route} refused with {Status} {Code}", route, (int)status, code ?? "-");

        if (status == HttpStatusCode.NotFound && code is null)
            return new DataNotFoundException(route, route, new Exception("Not found"));

        return new InvalidHttpRequestException(string.IsNullOrWhiteSpace(message) ? $"Error calling {route}" : message, route,
            "GET") { StatusCode = status, ErrorCode = code };
    }

    /// <summary>The <c>{ error, message }</c> body of a refusal; both null when the body is empty or not that shape.</summary>
    private static (string? Code, string? Message) ReadError(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            return (Text(document.RootElement, "error"), Text(document.RootElement, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }

        static string? Text(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString())
                    ? property.Value.GetString()
                    : null;
            }

            return null;
        }
    }
}
