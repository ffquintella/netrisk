using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// GitLab Issues (Track 4 milestone 4.2.2).
///
/// GitLab identifies a project by a URL-encoded path (<c>group%2Fproject</c>) or a numeric id, and an
/// issue by its <c>iid</c> — the per-project number, not the global <c>id</c>. Using the global id in
/// the path is the classic GitLab API mistake and produces a 404 against a project that does have the
/// issue.
///
/// Like GitHub, an issue is opened or closed; unlike GitHub, the closing action is a
/// <c>state_event</c> rather than a state field. Webhook authenticity is a shared token in
/// <c>X-Gitlab-Token</c>, compared in fixed time — GitLab does not sign the body.
/// </summary>
public class GitLabIssueTrackerProvider(ILogger logger, IOutboundHttpClient http) : IIssueTrackerProvider
{
    /// <summary>GitLab's shared-secret header. A token, not a signature.</summary>
    public const string TokenHeader = "x-gitlab-token";

    public IssueTrackerProviderKind Kind => IssueTrackerProviderKind.GitLab;

    public string Name => "GitLab Issues";

    public IssueTrackerCapabilities Capabilities => new()
    {
        SupportsWebhooks = true,
        SupportsComments = true,
        SupportsTransitions = true,
        SupportsLabels = true,
        SupportsPriority = false,
        SetupHint = "Base URL is https://gitlab.com or your instance. Project is the full path "
                    + "(group/subgroup/project) or the numeric project id. A project access token with "
                    + "the 'api' scope is enough. GitLab webhooks are authenticated by a shared secret "
                    + "token, not a signature — set the same value here and in the webhook."
    };

    public async Task<ConnectionTestResult> TestConnectionAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var project = await SendAsync(connection, token, "GET", $"/api/v4/projects/{ProjectId(connection)}",
            null, ct);

        if (!project.IsSuccess)
            return project.StatusCode switch
            {
                0 => ConnectionTestResult.Fail($"GitLab could not be reached: {project.TransportError}"),
                401 => ConnectionTestResult.Fail("GitLab rejected the token (401)."),
                403 => ConnectionTestResult.Fail("GitLab refused the request (403). The token may lack the "
                                                 + "'api' scope."),
                404 => ConnectionTestResult.Fail($"GitLab returned 404 for project '{connection.ProjectKey}'. "
                                                 + "Use the full path (group/project) or the numeric id."),
                _ => ConnectionTestResult.Fail($"GitLab answered HTTP {project.StatusCode}.")
            };

        var details = new Dictionary<string, string>();

        try
        {
            using var document = JsonDocument.Parse(project.Body!);

            if (document.RootElement.TryGetProperty("path_with_namespace", out var path))
                details["Project"] = path.GetString() ?? connection.ProjectKey;

            // issues_enabled is GitLab's equivalent of GitHub's has_issues, and the same trap.
            if (document.RootElement.TryGetProperty("issues_enabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.False)
                return ConnectionTestResult.Fail(
                    $"Project '{connection.ProjectKey}' has issues disabled, so NetRisk cannot create any.");
        }
        catch (JsonException)
        {
            // Cosmetic.
        }

        return ConnectionTestResult.Ok($"Connected to GitLab project '{connection.ProjectKey}'.", details);
    }

    public async Task<ExternalIssue> CreateIssueAsync(IssueTrackerConnection connection, string? token,
        IssueDraft draft, CancellationToken ct = default)
    {
        var labels = draft.Labels.ToList();
        if (!string.IsNullOrWhiteSpace(draft.Priority)) labels.Add($"priority::{draft.Priority}");

        var payload = JsonSerializer.Serialize(new
        {
            title = draft.Title,
            description = draft.Description,
            // GitLab takes labels as one comma-separated string, not an array.
            labels = labels.Count == 0 ? null : string.Join(",", labels.Distinct())
        });

        var response = await SendAsync(connection, token, "POST",
            $"/api/v4/projects/{ProjectId(connection)}/issues", payload, ct);

        if (!response.IsSuccess)
            throw new IntegrationRequestException("GitLab",
                $"GitLab refused to create the issue (HTTP {response.StatusCode}): {Excerpt(response.Body)}");

        return ParseIssue(JsonDocument.Parse(response.Body!).RootElement);
    }

    public async Task<ExternalIssue> UpdateIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string? comment, string? transitionTo, CancellationToken ct = default)
    {
        var iid = Iid(issueKey);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var payload = JsonSerializer.Serialize(new { body = comment });

            var noted = await SendAsync(connection, token, "POST",
                $"/api/v4/projects/{ProjectId(connection)}/issues/{iid}/notes", payload, ct);

            if (!noted.IsSuccess)
                throw new IntegrationRequestException("GitLab",
                    $"Could not comment on issue !{iid} (HTTP {noted.StatusCode}): {Excerpt(noted.Body)}");
        }

        if (!string.IsNullOrWhiteSpace(transitionTo))
        {
            var stateEvent = NormalizeStateEvent(transitionTo);

            if (stateEvent == null)
                throw new IntegrationRequestException("GitLab",
                    $"GitLab issues have no state '{transitionTo}'. Use 'close' or 'reopen'.");

            var payload = JsonSerializer.Serialize(new { state_event = stateEvent });

            var updated = await SendAsync(connection, token, "PUT",
                $"/api/v4/projects/{ProjectId(connection)}/issues/{iid}", payload, ct);

            if (!updated.IsSuccess)
                throw new IntegrationRequestException("GitLab",
                    $"Could not {stateEvent} issue !{iid} (HTTP {updated.StatusCode}): {Excerpt(updated.Body)}");

            return ParseIssue(JsonDocument.Parse(updated.Body!).RootElement);
        }

        return await GetIssueAsync(connection, token, issueKey, ct) ?? new ExternalIssue { Key = issueKey };
    }

    public async Task<ExternalIssue?> GetIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var response = await SendAsync(connection, token, "GET",
            $"/api/v4/projects/{ProjectId(connection)}/issues/{Iid(issueKey)}", null, ct);

        if (response.StatusCode == 404) return null;

        if (!response.IsSuccess)
            throw new IntegrationRequestException("GitLab",
                $"Could not read issue {issueKey} (HTTP {response.StatusCode}).");

        return ParseIssue(JsonDocument.Parse(response.Body!).RootElement);
    }

    public ExternalIssue? ParseWebhook(IssueTrackerConnection connection, string? webhookSecret,
        string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(webhookSecret))
        {
            logger.Warning("A GitLab webhook for connection {Connection} was refused: no webhook secret is set",
                connection.Name);
            return null;
        }

        var presented = headers.TryGetValue(TokenHeader, out var value) ? value : null;

        if (presented == null || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(presented)))
        {
            logger.Warning("A GitLab webhook for connection {Connection} presented the wrong token",
                connection.Name);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);

            // GitLab's issue hook nests the issue under object_attributes rather than "issue".
            if (!document.RootElement.TryGetProperty("object_attributes", out var attributes)) return null;

            return ParseIssue(attributes);
        }
        catch (JsonException ex)
        {
            logger.Warning("Unparseable GitLab webhook body: {Message}", ex.Message);
            return null;
        }
    }

    private static ExternalIssue ParseIssue(JsonElement issue)
    {
        // iid is the number in the URL and the one every API path wants; id is global and useless here.
        var iid = issue.TryGetProperty("iid", out var i) ? i.GetInt64().ToString() : string.Empty;
        var state = issue.TryGetProperty("state", out var s) ? s.GetString() : null;

        DateTime? updated = null;
        if (issue.TryGetProperty("updated_at", out var updatedAt)
            && DateTime.TryParse(updatedAt.GetString(), out var parsed))
            updated = parsed.ToUniversalTime();

        return new ExternalIssue
        {
            Key = iid,
            Id = issue.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : null,
            Url = issue.TryGetProperty("web_url", out var url) ? url.GetString() : null,
            Title = issue.TryGetProperty("title", out var title) ? title.GetString() : null,
            Status = state,
            IsClosed = string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase),
            UpdatedAt = updated
        };
    }

    /// <summary>
    /// A numeric project id passes through; a path is URL-encoded, which is how the GitLab API
    /// addresses a project by name.
    /// </summary>
    internal static string ProjectId(IssueTrackerConnection connection)
    {
        var key = (connection.ProjectKey ?? string.Empty).Trim();
        return long.TryParse(key, out _) ? key : Uri.EscapeDataString(key);
    }

    internal static string Iid(string issueKey)
    {
        var trimmed = (issueKey ?? string.Empty).Trim().TrimStart('#', '!');

        var slash = trimmed.LastIndexOf('/');
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];

        return trimmed;
    }

    private static string? NormalizeStateEvent(string transitionTo) =>
        transitionTo.Trim().ToLowerInvariant() switch
        {
            "close" or "closed" or "done" or "resolved" or "fixed" => "close",
            "reopen" or "reopened" or "open" or "opened" => "reopen",
            _ => null
        };

    private Task<OutboundHttpResponse> SendAsync(IssueTrackerConnection connection, string? token,
        string method, string path, string? body, CancellationToken ct) =>
        http.SendAsync(new OutboundHttpRequest
        {
            Method = method,
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Body = body,
            Headers =
            {
                // PRIVATE-TOKEN rather than Bearer: it is the header that works for both personal and
                // project access tokens.
                ["PRIVATE-TOKEN"] = token ?? string.Empty,
                ["Accept"] = "application/json"
            }
        }, ct);

    private static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(no response body)"
            : body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
