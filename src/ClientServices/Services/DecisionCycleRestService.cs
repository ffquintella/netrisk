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
using Model.DecisionCycle;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for the archive, incident backtesting and the risk committee (Stage 9.9, S50 §7).
///
/// Every call goes through the error-reporting client, as the monitoring client does: a refusal (a risk already closed, a
/// condition already met, a decision already closed, a vote already cast, a member recused) answers with a sentence
/// written for the person, and the reliable client would otherwise throw on the status before the body could be read.
/// </summary>
public class DecisionCycleRestService(IRestService restService) : RestServiceBase(restService), IDecisionCycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    // --- archive ----------------------------------------------------------------------------

    public async Task<List<RiskArchiveDto>> GetArchivesAsync(bool dueOnly = false, bool includeEnded = false)
    {
        var query = new List<(string, string)>();
        if (dueOnly) query.Add(("dueOnly", "true"));
        if (includeEnded) query.Add(("includeEnded", "true"));

        return Read<List<RiskArchiveDto>>(await ExecuteAsync("/RiskArchive/Risks", Method.Get, null, query.ToArray()));
    }

    public async Task<List<RiskArchiveDto>> GetRiskArchivesAsync(int riskId) =>
        Read<List<RiskArchiveDto>>(await ExecuteAsync($"/RiskArchive/Risks/{Id(riskId)}", Method.Get));

    public async Task<RiskArchiveDto> ArchiveAsync(int riskId, RiskArchiveRequest request) =>
        Read<RiskArchiveDto>(await ExecuteAsync($"/RiskArchive/Risks/{Id(riskId)}", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskArchiveDto> ReopenArchiveAsync(int riskId, RiskArchiveReopenRequest request) =>
        Read<RiskArchiveDto>(await ExecuteAsync($"/RiskArchive/Risks/{Id(riskId)}/Reopen", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskArchiveDto> ReviewArchiveAsync(int riskId, RiskArchiveReviewRequest request) =>
        Read<RiskArchiveDto>(await ExecuteAsync($"/RiskArchive/Risks/{Id(riskId)}/Reviews", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- backtesting ------------------------------------------------------------------------

    public async Task<BacktestReportDto> GetBacktestingReportAsync(DateTime? from = null, DateTime? to = null,
        int? entityId = null)
    {
        var query = new List<(string, string)>();
        if (from is { } f) query.Add(("from", f.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
        if (to is { } t) query.Add(("to", t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
        if (entityId is { } e) query.Add(("entityId", Id(e)));

        return Read<BacktestReportDto>(await ExecuteAsync("/Backtesting", Method.Get, null, query.ToArray()));
    }

    public async Task<BacktestIncidentDto> GetIncidentBacktestAsync(int incidentId) =>
        Read<BacktestIncidentDto>(await ExecuteAsync($"/Backtesting/Incidents/{Id(incidentId)}", Method.Get));

    public async Task<BacktestIncidentDto> AssessIncidentAsync(int incidentId, BacktestAssessmentRequest request) =>
        Read<BacktestIncidentDto>(await ExecuteAsync($"/Backtesting/Incidents/{Id(incidentId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    // --- committees -------------------------------------------------------------------------

    public async Task<List<RiskCommitteeDto>> GetCommitteesAsync(bool includeRetired = false) =>
        Read<List<RiskCommitteeDto>>(includeRetired
            ? await ExecuteAsync("/RiskCommittees", Method.Get, null, ("includeRetired", "true"))
            : await ExecuteAsync("/RiskCommittees", Method.Get));

    public async Task<RiskCommitteeDto> GetCommitteeAsync(int committeeId) =>
        Read<RiskCommitteeDto>(await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}", Method.Get));

    public async Task<RiskCommitteeDto> CreateCommitteeAsync(RiskCommitteeRequest request) =>
        Read<RiskCommitteeDto>(await ExecuteAsync("/RiskCommittees", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskCommitteeDto> UpdateCommitteeAsync(int committeeId, RiskCommitteeRequest request) =>
        Read<RiskCommitteeDto>(await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}", Method.Put,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskCommitteeDto> RetireCommitteeAsync(int committeeId) =>
        Read<RiskCommitteeDto>(await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}/Retire", Method.Post));

    public async Task<RiskCommitteeDto> AddCommitteeMemberAsync(int committeeId, int userId) =>
        Read<RiskCommitteeDto>(await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}/Members/{Id(userId)}", Method.Put));

    public async Task RemoveCommitteeMemberAsync(int committeeId, int userId) =>
        await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}/Members/{Id(userId)}", Method.Delete);

    public async Task<List<RiskCommitteeDecisionDto>> GetCommitteeDecisionsAsync(int? committeeId = null,
        int? riskId = null, bool openOnly = false)
    {
        var query = new List<(string, string)>();
        if (committeeId is { } c) query.Add(("committeeId", Id(c)));
        if (riskId is { } r) query.Add(("riskId", Id(r)));
        if (openOnly) query.Add(("openOnly", "true"));

        return Read<List<RiskCommitteeDecisionDto>>(
            await ExecuteAsync("/RiskCommittees/Decisions", Method.Get, null, query.ToArray()));
    }

    public async Task<RiskCommitteeDecisionDto> GetCommitteeDecisionAsync(int decisionId) =>
        Read<RiskCommitteeDecisionDto>(await ExecuteAsync($"/RiskCommittees/Decisions/{Id(decisionId)}", Method.Get));

    public async Task<RiskCommitteeDecisionDto> OpenCommitteeDecisionAsync(int committeeId,
        RiskCommitteeDecisionRequest request) =>
        Read<RiskCommitteeDecisionDto>(await ExecuteAsync($"/RiskCommittees/{Id(committeeId)}/Decisions", Method.Post,
            request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskCommitteeDecisionDto> VoteAsync(int decisionId, RiskCommitteeVoteRequest request) =>
        Read<RiskCommitteeDecisionDto>(await ExecuteAsync($"/RiskCommittees/Decisions/{Id(decisionId)}/Votes",
            Method.Post, request ?? throw new ArgumentNullException(nameof(request))));

    public async Task<RiskCommitteeDecisionDto> WithdrawCommitteeDecisionAsync(int decisionId,
        RiskCommitteeWithdrawRequest request) =>
        Read<RiskCommitteeDecisionDto>(await ExecuteAsync($"/RiskCommittees/Decisions/{Id(decisionId)}/Withdraw",
            Method.Post, request ?? throw new ArgumentNullException(nameof(request))));

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
