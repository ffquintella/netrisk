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
/// GitHub Issues (Track 4 milestone 4.2.2).
///
/// GitHub has no priority field and no workflow states — an issue is open or closed — so priority is
/// expressed as a label and the only transition available is <c>closed</c>/<c>open</c>. Saying that
/// in <see cref="Capabilities"/> rather than silently dropping the priority is what stops an operator
/// from configuring a mapping that quietly does nothing.
///
/// Webhooks are signed: <c>X-Hub-Signature-256</c> is HMAC-SHA256 of the raw body under the webhook
/// secret, and it is verified before the payload is looked at.
/// </summary>
public class GitHubIssueTrackerProvider(ILogger logger, IOutboundHttpClient http) : IIssueTrackerProvider
{
    /// <summary>GitHub's signature header. Value is <c>sha256=&lt;hex&gt;</c>.</summary>
    public const string SignatureHeader = "x-hub-signature-256";

    public IssueTrackerProviderKind Kind => IssueTrackerProviderKind.GitHub;

    public string Name => "GitHub Issues";

    public IssueTrackerCapabilities Capabilities => new()
    {
        SupportsWebhooks = true,
        SupportsComments = true,
        // Open/closed only; there is no named-state workflow to transition through.
        SupportsTransitions = true,
        SupportsLabels = true,
        SupportsPriority = false,
        SetupHint = "Base URL is https://api.github.com (or your Enterprise Server API root). Project "
                    + "is owner/repo. The token needs the 'issues: write' permission. Webhook "
                    + "deliveries are verified against X-Hub-Signature-256, so set the same secret in "
                    + "GitHub and on this connection."
    };

    public async Task<ConnectionTestResult> TestConnectionAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var repo = await SendAsync(connection, token, "GET", $"/repos/{connection.ProjectKey}", null, ct);

        if (repo.StatusCode == 404)
            return ConnectionTestResult.Fail(
                $"GitHub returned 404 for '{connection.ProjectKey}'. Check the owner/repo and that the "
                + "token can see it — a private repository the token cannot read also answers 404.");

        if (!repo.IsSuccess)
            return repo.StatusCode switch
            {
                0 => ConnectionTestResult.Fail($"GitHub could not be reached: {repo.TransportError}"),
                401 => ConnectionTestResult.Fail("GitHub rejected the token (401)."),
                403 => ConnectionTestResult.Fail("GitHub refused the request (403). The token may lack "
                                                 + "the issues permission, or the rate limit is exhausted."),
                _ => ConnectionTestResult.Fail($"GitHub answered HTTP {repo.StatusCode}.")
            };

        var details = new Dictionary<string, string>();

        try
        {
            using var document = JsonDocument.Parse(repo.Body!);
            if (document.RootElement.TryGetProperty("full_name", out var fullName))
                details["Repository"] = fullName.GetString() ?? connection.ProjectKey;

            // A repository with issues disabled accepts the test and then rejects every create, so it
            // is checked here where it can be reported.
            if (document.RootElement.TryGetProperty("has_issues", out var hasIssues)
                && !hasIssues.GetBoolean())
                return ConnectionTestResult.Fail(
                    $"Repository '{connection.ProjectKey}' has Issues disabled, so NetRisk cannot create any.");
        }
        catch (JsonException)
        {
            // Cosmetic.
        }

        return ConnectionTestResult.Ok($"Connected to GitHub repository '{connection.ProjectKey}'.", details);
    }

    public async Task<ExternalIssue> CreateIssueAsync(IssueTrackerConnection connection, string? token,
        IssueDraft draft, CancellationToken ct = default)
    {
        var labels = draft.Labels.ToList();

        // GitHub has no priority field, so the mapped priority becomes a label. Prefixed so it is
        // recognizable in a repository that has its own label taxonomy.
        if (!string.IsNullOrWhiteSpace(draft.Priority)) labels.Add($"priority:{draft.Priority}");

        var body = new MemoryStream();
        using (var json = new Utf8JsonWriter(body))
        {
            json.WriteStartObject();
            json.WriteString("title", draft.Title);
            json.WriteString("body", draft.Description);

            if (labels.Count > 0)
            {
                json.WriteStartArray("labels");
                foreach (var label in labels.Distinct()) json.WriteStringValue(label);
                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        var response = await SendAsync(connection, token, "POST",
            $"/repos/{connection.ProjectKey}/issues", Encoding.UTF8.GetString(body.ToArray()), ct);

        if (!response.IsSuccess)
            throw new IntegrationRequestException("GitHub",
                $"GitHub refused to create the issue (HTTP {response.StatusCode}): {Excerpt(response.Body)}");

        return ParseIssue(JsonDocument.Parse(response.Body!).RootElement);
    }

    public async Task<ExternalIssue> UpdateIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string? comment, string? transitionTo, CancellationToken ct = default)
    {
        var number = Number(issueKey);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var payload = JsonSerializer.Serialize(new { body = comment });

            var commented = await SendAsync(connection, token, "POST",
                $"/repos/{connection.ProjectKey}/issues/{number}/comments", payload, ct);

            if (!commented.IsSuccess)
                throw new IntegrationRequestException("GitHub",
                    $"Could not comment on issue #{number} (HTTP {commented.StatusCode}): "
                    + Excerpt(commented.Body));
        }

        if (!string.IsNullOrWhiteSpace(transitionTo))
        {
            var state = NormalizeState(transitionTo);

            if (state == null)
                throw new IntegrationRequestException("GitHub",
                    $"GitHub issues have no state '{transitionTo}'. Use 'closed' or 'open'.");

            var payload = JsonSerializer.Serialize(new { state });

            var patched = await SendAsync(connection, token, "PATCH",
                $"/repos/{connection.ProjectKey}/issues/{number}", payload, ct);

            if (!patched.IsSuccess)
                throw new IntegrationRequestException("GitHub",
                    $"Could not set issue #{number} to {state} (HTTP {patched.StatusCode}): "
                    + Excerpt(patched.Body));

            return ParseIssue(JsonDocument.Parse(patched.Body!).RootElement);
        }

        return await GetIssueAsync(connection, token, issueKey, ct) ?? new ExternalIssue { Key = issueKey };
    }

    public async Task<ExternalIssue?> GetIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var response = await SendAsync(connection, token, "GET",
            $"/repos/{connection.ProjectKey}/issues/{Number(issueKey)}", null, ct);

        if (response.StatusCode == 404) return null;

        if (!response.IsSuccess)
            throw new IntegrationRequestException("GitHub",
                $"Could not read issue {issueKey} (HTTP {response.StatusCode}).");

        return ParseIssue(JsonDocument.Parse(response.Body!).RootElement);
    }

    public ExternalIssue? ParseWebhook(IssueTrackerConnection connection, string? webhookSecret,
        string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        // No secret configured means no way to tell a GitHub delivery from a forged POST, so the
        // payload is refused rather than trusted. An unauthenticated caller must not be able to close
        // findings.
        if (string.IsNullOrEmpty(webhookSecret))
        {
            logger.Warning("A GitHub webhook for connection {Connection} was refused: no webhook secret is set",
                connection.Name);
            return null;
        }

        var presented = headers.TryGetValue(SignatureHeader, out var value) ? value : null;

        if (!VerifySignature(rawBody, webhookSecret, presented))
        {
            logger.Warning("A GitHub webhook for connection {Connection} failed signature verification",
                connection.Name);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);

            if (!document.RootElement.TryGetProperty("issue", out var issue)) return null;

            return ParseIssue(issue);
        }
        catch (JsonException ex)
        {
            logger.Warning("Unparseable GitHub webhook body: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// <c>X-Hub-Signature-256</c> verification: HMAC-SHA256 of the raw body, hex, prefixed
    /// <c>sha256=</c>. Fixed-time comparison, because a leaky comparison here lets an attacker forge
    /// a valid signature byte by byte.
    /// </summary>
    internal static bool VerifySignature(string rawBody, string secret, string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;

        var expected = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented.Trim()));
    }

    private static ExternalIssue ParseIssue(JsonElement issue)
    {
        var number = issue.TryGetProperty("number", out var n) ? n.GetInt64().ToString() : string.Empty;
        var state = issue.TryGetProperty("state", out var s) ? s.GetString() : null;

        DateTime? updated = null;
        if (issue.TryGetProperty("updated_at", out var updatedAt)
            && DateTime.TryParse(updatedAt.GetString(), out var parsed))
            updated = parsed.ToUniversalTime();

        return new ExternalIssue
        {
            Key = number,
            Id = issue.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : null,
            Url = issue.TryGetProperty("html_url", out var url) ? url.GetString() : null,
            Title = issue.TryGetProperty("title", out var title) ? title.GetString() : null,
            Status = state,
            IsClosed = string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase),
            UpdatedAt = updated
        };
    }

    /// <summary>
    /// Accepts <c>88</c>, <c>#88</c>, or a full issue URL, because all three are what a person pastes
    /// into "link existing issue".
    /// </summary>
    internal static string Number(string issueKey)
    {
        var trimmed = (issueKey ?? string.Empty).Trim().TrimStart('#');

        var slash = trimmed.LastIndexOf('/');
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];

        return trimmed;
    }

    private static string? NormalizeState(string transitionTo) =>
        transitionTo.Trim().ToLowerInvariant() switch
        {
            "closed" or "close" or "done" or "resolved" or "fixed" => "closed",
            "open" or "reopen" or "reopened" => "open",
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
                ["Authorization"] = "Bearer " + token,
                ["Accept"] = "application/vnd.github+json",
                // Pinning the API version is what stops a GitHub-side default change from altering
                // the response shape under a running deployment.
                ["X-GitHub-Api-Version"] = "2022-11-28"
            }
        }, ct);

    private static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(no response body)"
            : body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
