using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using Model.Governance;
using RiskPortal.Models;

namespace RiskPortal.Services;

/// <summary>
/// The portal's typed client over the NetRisk REST API.
///
/// Two things it does that a naive wrapper would not:
///
/// <list type="bullet">
/// <item>It keeps the server's refusal message. A 422 from the governance endpoints carries a sentence
/// written for a person — "Residual 9.10 is above the acceptance ceiling of 6.00" — and the whole point
/// of the portal is that a business reviewer can act on it without asking a security analyst what
/// happened.</item>
/// <item>It reads the whole review screen in two calls. The campaign's risks, their scores, the
/// appetite verdict and the treatment tasks come back from one campaign sub-resource — which is both
/// faster than one request per risk and the only shape a business reviewer can read at all, since the
/// register-wide score and appetite endpoints are correctly closed to them.</item>
/// </list>
/// </summary>
public class PortalApiClient(
    HttpClient httpClient,
    IPortalRegistration registration,
    ILogger<PortalApiClient> logger) : IPortalApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<PortalRegistrationState> GetRegistrationStateAsync(CancellationToken ct = default)
    {
        var clientId = registration.ClientId;

        try
        {
            using var request = NewRequest(HttpMethod.Get, "/Registration/IsAccepted");
            using var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Not registered at all yet — register now, so the administrator has something to
                // approve without anybody having to run a command.
                await RegisterAsync(ct);
                return new PortalRegistrationState { ClientId = clientId, Approved = false };
            }

            if (!response.IsSuccessStatusCode)
                return new PortalRegistrationState
                {
                    ClientId = clientId,
                    Problem = $"The NetRisk API answered {(int)response.StatusCode} when asked whether " +
                              "this portal is approved."
                };

            var body = await response.Content.ReadAsStringAsync(ct);
            var approved = bool.TryParse(body.Trim().Trim('"'), out var value) && value;

            return new PortalRegistrationState { ClientId = clientId, Approved = approved };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach the NetRisk API to check the portal registration");

            return new PortalRegistrationState
            {
                ClientId = clientId,
                Problem = "The NetRisk API could not be reached. Check that it is running and that " +
                          "Server:Url points at it."
            };
        }
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        try
        {
            using var request = NewRequest(HttpMethod.Post, "/Registration");
            request.Content = JsonContent.Create(new
            {
                id = registration.ClientId,
                hostname = registration.Hostname,
                loggedAccount = "risk-portal"
            });

            using var response = await httpClient.SendAsync(request, ct);

            // 412 means "already exists", which is the normal answer on every restart.
            if (response.IsSuccessStatusCode)
                logger.LogInformation(
                    "Registered this portal with the NetRisk API as client {ClientId}; an administrator " +
                    "has to approve it before anybody can sign in", registration.ClientId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not register the portal with the NetRisk API");
        }
    }

    public async Task<string?> SignInAsync(string login, string password, CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Get, "/Authentication/GetToken");

        // Basic, for exactly this one call. The password is not stored anywhere afterwards — the
        // session carries the returned JWT instead, which is what makes a stolen portal cookie a
        // time-limited problem rather than a permanent one.
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}")));

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Sign-in refused for {Login}: {Status}", Redact(login),
                response.StatusCode);
            return null;
        }

        var token = (await response.Content.ReadAsStringAsync(ct)).Trim().Trim('"');

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public async Task SignOutAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var request = NewRequest(HttpMethod.Post, "/Sessions/Logout", token);
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("The API answered {Status} when revoking a portal session",
                    response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Dropping the cookie already ends the session from the browser's point of view; failing
            // to revoke server-side is worth a log line and not worth blocking a sign-out.
            logger.LogWarning(ex, "Could not revoke the portal session token");
        }
    }

    public async Task<List<CampaignSummary>> GetMyCampaignsAsync(string token, bool openOnly,
        CancellationToken ct = default)
    {
        var campaigns = await GetAsync<List<RiskReviewCampaign>>(token,
            $"/RiskReviewCampaigns/Mine?openOnly={(openOnly ? "true" : "false")}", ct) ?? [];

        return campaigns.Select(ToSummary).OrderBy(c => c.DueDate).ToList();
    }

    public async Task<CampaignDetail?> GetCampaignAsync(string token, int campaignId,
        CancellationToken ct = default)
    {
        var campaign = await GetAsync<RiskReviewCampaign>(token, $"/RiskReviewCampaigns/{campaignId}", ct);
        if (campaign is null) return null;

        var detail = new CampaignDetail { Campaign = ToSummary(campaign) };

        // One call for the whole screen. The scores and the appetite verdict are not readable through
        // the register-wide endpoints by a business reviewer — they hold `business_risk_review` and
        // deliberately not `riskmanagement` — so the API assembles them behind the campaign's own
        // permission instead. That also turns 1 + 3N requests into two.
        var items = await GetAsync<List<CampaignReviewItem>>(token,
            $"/RiskReviewCampaigns/{campaignId}/Items", ct) ?? [];

        detail.Items = items.Select(item => new ReviewItem
        {
            ItemId = item.ItemId,
            RiskId = item.RiskId,
            Rank = item.Rank,
            Subject = item.Subject,
            ReferenceId = item.ReferenceId,
            Notes = item.Notes,
            Status = item.Status,
            Inherent = item.Inherent,
            Residual = item.Residual,
            Decision = item.Decision,
            DecisionNotes = item.DecisionNotes,
            DecidedAt = item.DecidedAt,
            Appetite = item.Appetite,
            AcceptedUntil = item.AcceptedUntil,
            Tasks = item.Tasks
        }).ToList();

        return detail;
    }

    public Task<PortalResult> SaveRankingAsync(string token, int campaignId, List<int> orderedItemIds,
        CancellationToken ct = default) =>
        WriteAsync(token, HttpMethod.Put, $"/RiskReviewCampaigns/{campaignId}/Ranking",
            new { orderedItemIds }, ct);

    public Task<PortalResult> DecideAsync(string token, int campaignId, int itemId,
        CampaignDecisionRequest request, CancellationToken ct = default) =>
        WriteAsync(token, HttpMethod.Post,
            $"/RiskReviewCampaigns/{campaignId}/Items/{itemId}/Decision", request, ct);

    // --- transport --------------------------------------------------------------------------

    private HttpRequestMessage NewRequest(HttpMethod method, string route, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);

        // Every credential presentation the API accepts, Basic or Bearer, is checked against an
        // approved client registration — so the header goes on every request, not just the
        // authenticated ones.
        request.Headers.Add("ClientId", registration.ClientId);

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    private async Task<T?> GetAsync<T>(string token, string route, CancellationToken ct) where T : class
    {
        try
        {
            using var request = NewRequest(HttpMethod.Get, route, token);
            using var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return null;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GET {Route} answered {Status}", route, response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;

            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "GET {Route} failed", route);
            return null;
        }
    }

    private async Task<PortalResult> WriteAsync(string token, HttpMethod method, string route,
        object body, CancellationToken ct)
    {
        try
        {
            using var request = NewRequest(method, route, token);
            request.Content = JsonContent.Create(body);

            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode) return PortalResult.Ok();

            var content = await response.Content.ReadAsStringAsync(ct);

            logger.LogInformation("{Method} {Route} answered {Status}", method, route,
                response.StatusCode);

            return PortalResult.Fail(Explain(response.StatusCode, content));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "{Method} {Route} failed", method, route);

            return PortalResult.Fail("The NetRisk API could not be reached. Nothing was saved — try " +
                                     "again in a moment.");
        }
    }

    /// <summary>
    /// Turns a refusal into a sentence for a business reviewer.
    ///
    /// The API's error bodies carry a <c>message</c> field written for exactly this, so it is preferred
    /// over anything invented here. The fallbacks matter for the statuses that carry no body: a
    /// reviewer told "403" learns nothing, and one told "you are not an appointed reviewer for this
    /// entity" knows who to ask.
    /// </summary>
    public static string Explain(HttpStatusCode status, string? content)
    {
        var fromServer = ReadMessage(content);
        if (!string.IsNullOrWhiteSpace(fromServer)) return fromServer;

        return status switch
        {
            HttpStatusCode.Forbidden =>
                "You are not an appointed risk reviewer for this business entity, so this campaign is " +
                "not yours to decide.",
            HttpStatusCode.Unauthorized =>
                "Your session has expired. Sign in again and your decisions so far are unaffected.",
            HttpStatusCode.Conflict => "This risk already has a live acceptance.",
            HttpStatusCode.NotFound => "That campaign or risk no longer exists.",
            _ => "The decision could not be recorded. Nothing was saved."
        };
    }

    private static string? ReadMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var document = JsonDocument.Parse(content);

            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in new[] { "message", "Message" })
                if (document.RootElement.TryGetProperty(name, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                    return property.GetString();

            return null;
        }
        catch (JsonException)
        {
            // A plain-text body is still better than a generic message.
            return content.Length > 400 ? null : content;
        }
    }

    private static CampaignSummary ToSummary(RiskReviewCampaign campaign) => new()
    {
        Id = campaign.Id,
        Name = campaign.Name,
        EntityId = campaign.EntityId,
        PeriodStart = campaign.PeriodStart,
        PeriodEnd = campaign.PeriodEnd,
        DueDate = campaign.DueDate,
        Status = campaign.Status,
        TotalItems = campaign.Items.Count,
        DecidedItems = campaign.Items.Count(i => i.Decision != RiskReviewDecision.Pending)
    };

    /// <summary>
    /// A login is not a secret but it is personal data, and this line ends up in a log that is often
    /// shipped off-host. Two characters are enough for an operator correlating a failed sign-in.
    /// </summary>
    private static string Redact(string login) =>
        login.Length <= 2 ? login + "…" : login[..2] + "…";
}
