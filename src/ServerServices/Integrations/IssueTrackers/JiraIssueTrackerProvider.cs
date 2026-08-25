using System.Text;
using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// Jira Cloud REST v3 (Track 4 milestone 4.2.2).
///
/// Two things about v3 shape this provider. Descriptions and comments are Atlassian Document Format,
/// not text — posting a plain string to v3 is rejected — so Markdown is converted to a minimal ADF
/// document here. And a status change is a *transition*, addressed by transition id, so moving an
/// issue means reading <c>/transitions</c> first and matching by name; there is no "set status" field.
///
/// Auth is HTTP basic with <c>email:api-token</c>, which is what Atlassian issues for Cloud.
/// </summary>
public class JiraIssueTrackerProvider(ILogger logger, IOutboundHttpClient http) : IIssueTrackerProvider
{
    public IssueTrackerProviderKind Kind => IssueTrackerProviderKind.Jira;

    public string Name => "Jira";

    public IssueTrackerCapabilities Capabilities => new()
    {
        SupportsWebhooks = true,
        SupportsComments = true,
        SupportsTransitions = true,
        SupportsLabels = true,
        SupportsPriority = true,
        SetupHint = "Base URL is your site (https://acme.atlassian.net). Authenticate with the "
                    + "Atlassian account email as the user and an API token as the credential. "
                    + "Jira webhooks carry no signature, so the receiver URL must include the "
                    + "connection's webhook secret as the ?secret= query parameter."
    };

    public async Task<ConnectionTestResult> TestConnectionAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var me = await SendAsync(connection, token, "GET", "/rest/api/3/myself", null, ct);

        if (!me.IsSuccess) return Describe(me, "Jira");

        // The credential being valid is not the question an operator is asking; whether it can see
        // the project is.
        var project = await SendAsync(connection, token,
            "GET", $"/rest/api/3/project/{Uri.EscapeDataString(connection.ProjectKey)}", null, ct);

        if (!project.IsSuccess)
            return ConnectionTestResult.Fail(
                $"Authenticated, but project '{connection.ProjectKey}' was not readable "
                + $"(HTTP {project.StatusCode}). Check the project key and the account's permissions.");

        var details = new Dictionary<string, string>();
        TryAdd(details, "Account", me.Body, "displayName");
        TryAdd(details, "Project", project.Body, "name");

        return ConnectionTestResult.Ok($"Connected to Jira and read project '{connection.ProjectKey}'.", details);
    }

    public async Task<ExternalIssue> CreateIssueAsync(IssueTrackerConnection connection, string? token,
        IssueDraft draft, CancellationToken ct = default)
    {
        var body = new MemoryStream();
        using (var json = new Utf8JsonWriter(body))
        {
            json.WriteStartObject();
            json.WriteStartObject("fields");

            json.WriteStartObject("project");
            json.WriteString("key", connection.ProjectKey);
            json.WriteEndObject();

            // Jira rejects a summary over 255 characters outright, and a finding title can exceed it.
            json.WriteString("summary", Truncate(draft.Title, 255));

            json.WritePropertyName("description");
            WriteAdf(json, draft.Description);

            json.WriteStartObject("issuetype");
            json.WriteString("name", string.IsNullOrWhiteSpace(draft.IssueType) ? "Task" : draft.IssueType);
            json.WriteEndObject();

            if (!string.IsNullOrWhiteSpace(draft.Priority))
            {
                json.WriteStartObject("priority");
                json.WriteString("name", draft.Priority);
                json.WriteEndObject();
            }

            if (draft.Labels.Count > 0)
            {
                json.WriteStartArray("labels");
                // Jira labels cannot contain whitespace; it rejects the whole request if one does.
                foreach (var label in draft.Labels.Select(l => l.Replace(' ', '-')).Distinct())
                    json.WriteStringValue(label);
                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        var payload = Encoding.UTF8.GetString(body.ToArray());
        var response = await SendAsync(connection, token, "POST", "/rest/api/3/issue", payload, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Jira refused to create the issue (HTTP {response.StatusCode}): {Excerpt(response.Body)}");

        using var document = JsonDocument.Parse(response.Body!);
        var key = document.RootElement.GetProperty("key").GetString()!;
        var id = document.RootElement.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;

        return new ExternalIssue
        {
            Key = key,
            Id = id,
            Url = $"{connection.BaseUrl.TrimEnd('/')}/browse/{key}",
            Title = draft.Title,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public async Task<ExternalIssue> UpdateIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string? comment, string? transitionTo, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            var body = new MemoryStream();
            using (var json = new Utf8JsonWriter(body))
            {
                json.WriteStartObject();
                json.WritePropertyName("body");
                WriteAdf(json, comment);
                json.WriteEndObject();
            }

            var commentResponse = await SendAsync(connection, token, "POST",
                $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/comment",
                Encoding.UTF8.GetString(body.ToArray()), ct);

            if (!commentResponse.IsSuccess)
                throw new Model.Exceptions.IntegrationRequestException("Jira",
                    $"Could not comment on {issueKey} (HTTP {commentResponse.StatusCode}): "
                    + Excerpt(commentResponse.Body));
        }

        if (!string.IsNullOrWhiteSpace(transitionTo))
            await TransitionAsync(connection, token, issueKey, transitionTo, ct);

        return await GetIssueAsync(connection, token, issueKey, ct)
               ?? new ExternalIssue { Key = issueKey };
    }

    /// <summary>
    /// Resolves the named transition against the issue's *available* transitions and executes it.
    ///
    /// Available rather than all: Jira's workflow decides which transitions apply from the current
    /// state, so "Done" may simply not be reachable, and reporting that clearly is more useful than a
    /// 400 from Jira.
    /// </summary>
    private async Task TransitionAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string transitionTo, CancellationToken ct)
    {
        var available = await SendAsync(connection, token, "GET",
            $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions", null, ct);

        if (!available.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Could not read the transitions for {issueKey} (HTTP {available.StatusCode}).");

        using var document = JsonDocument.Parse(available.Body!);

        string? transitionId = null;
        var names = new List<string>();

        foreach (var transition in document.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var name = transition.TryGetProperty("name", out var n) ? n.GetString() : null;
            var target = transition.TryGetProperty("to", out var to) && to.TryGetProperty("name", out var tn)
                ? tn.GetString()
                : null;

            if (name != null) names.Add(name);

            // Matched against the transition name or the state it leads to: operators configure
            // whichever of the two they see in their Jira, and the two are often different words.
            if (string.Equals(name, transitionTo, StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, transitionTo, StringComparison.OrdinalIgnoreCase))
            {
                transitionId = transition.GetProperty("id").GetString();
                break;
            }
        }

        if (transitionId == null)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"'{transitionTo}' is not an available transition for {issueKey}. "
                + $"Available now: {string.Join(", ", names)}.");

        var payload = $"{{\"transition\":{{\"id\":\"{transitionId}\"}}}}";

        var executed = await SendAsync(connection, token, "POST",
            $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions", payload, ct);

        if (!executed.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Jira refused the transition of {issueKey} (HTTP {executed.StatusCode}): "
                + Excerpt(executed.Body));
    }

    public async Task<ExternalIssue?> GetIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var response = await SendAsync(connection, token, "GET",
            $"/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}?fields=summary,status,updated", null, ct);

        if (response.StatusCode == 404) return null;

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Could not read {issueKey} (HTTP {response.StatusCode}).");

        return ParseIssue(connection, JsonDocument.Parse(response.Body!).RootElement);
    }

    public ExternalIssue? ParseWebhook(IssueTrackerConnection connection, string? webhookSecret,
        string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        // Jira Cloud webhooks are unsigned. The shared secret therefore has to travel in the URL,
        // and the receiving controller compares it before calling this — which is why there is no
        // signature check here and the setup hint says so out loud.
        try
        {
            using var document = JsonDocument.Parse(rawBody);

            if (!document.RootElement.TryGetProperty("issue", out var issue)) return null;

            return ParseIssue(connection, issue);
        }
        catch (JsonException ex)
        {
            logger.Warning("Unparseable Jira webhook body: {Message}", ex.Message);
            return null;
        }
    }

    private static ExternalIssue ParseIssue(IssueTrackerConnection connection, JsonElement issue)
    {
        var key = issue.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty;
        var fields = issue.TryGetProperty("fields", out var f) ? f : default;

        string? status = null;
        var closed = false;
        DateTime? updated = null;
        string? title = null;

        if (fields.ValueKind == JsonValueKind.Object)
        {
            if (fields.TryGetProperty("status", out var statusField))
            {
                status = statusField.TryGetProperty("name", out var n) ? n.GetString() : null;

                // statusCategory.key is "done" for every terminal status regardless of what the
                // workflow named it, which is the only reliable "is this closed" signal Jira gives.
                if (statusField.TryGetProperty("statusCategory", out var category)
                    && category.TryGetProperty("key", out var categoryKey))
                    closed = string.Equals(categoryKey.GetString(), "done", StringComparison.OrdinalIgnoreCase);
            }

            if (fields.TryGetProperty("summary", out var summary)) title = summary.GetString();

            if (fields.TryGetProperty("updated", out var updatedField)
                && DateTime.TryParse(updatedField.GetString(), out var parsed))
                updated = parsed.ToUniversalTime();
        }

        return new ExternalIssue
        {
            Key = key,
            Id = issue.TryGetProperty("id", out var id) ? id.GetString() : null,
            Url = string.IsNullOrEmpty(key) ? null : $"{connection.BaseUrl.TrimEnd('/')}/browse/{key}",
            Title = title,
            Status = status,
            IsClosed = closed,
            UpdatedAt = updated
        };
    }

    /// <summary>
    /// Writes a minimal Atlassian Document Format document: one paragraph per line of the Markdown.
    ///
    /// Deliberately not a Markdown-to-ADF translator. ADF has no table-from-pipes conversion and
    /// writing one would be a project; a paragraph-per-line document renders the field list and the
    /// links correctly, which is what the ticket is for. The Markdown link syntax survives because
    /// Jira's own renderer picks it up.
    /// </summary>
    internal static void WriteAdf(Utf8JsonWriter json, string markdown)
    {
        json.WriteStartObject();
        json.WriteString("type", "doc");
        json.WriteNumber("version", 1);
        json.WriteStartArray("content");

        foreach (var line in (markdown ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            json.WriteStartObject();
            json.WriteString("type", "paragraph");
            json.WriteStartArray("content");

            // An ADF paragraph may not contain an empty text node, so a blank line becomes an empty
            // paragraph with no content rather than a text node with "".
            if (line.Length > 0)
            {
                json.WriteStartObject();
                json.WriteString("type", "text");
                json.WriteString("text", line);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private Task<OutboundHttpResponse> SendAsync(IssueTrackerConnection connection, string? token,
        string method, string path, string? body, CancellationToken ct)
    {
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{connection.AuthUser}:{token}"));

        return http.SendAsync(new OutboundHttpRequest
        {
            Method = method,
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Body = body,
            Headers =
            {
                ["Authorization"] = "Basic " + basic,
                ["Accept"] = "application/json"
            }
        }, ct);
    }

    private static ConnectionTestResult Describe(OutboundHttpResponse response, string provider) =>
        response.StatusCode switch
        {
            0 => ConnectionTestResult.Fail($"{provider} could not be reached: {response.TransportError}"),
            401 => ConnectionTestResult.Fail($"{provider} rejected the credentials (401). "
                                             + "For Jira Cloud the user is the account email and the credential is an API token."),
            403 => ConnectionTestResult.Fail($"{provider} accepted the credentials but refused the request (403)."),
            404 => ConnectionTestResult.Fail($"{provider} returned 404. Check the base URL."),
            _ => ConnectionTestResult.Fail($"{provider} answered HTTP {response.StatusCode}.")
        };

    private static void TryAdd(Dictionary<string, string> details, string label, string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(property, out var value) && value.GetString() is { } text)
                details[label] = text;
        }
        catch (JsonException)
        {
            // A test result missing one cosmetic detail is not worth failing the test over.
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(no response body)"
            : body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
