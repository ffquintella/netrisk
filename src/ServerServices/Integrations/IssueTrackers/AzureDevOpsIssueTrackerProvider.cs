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
/// Azure DevOps Work Items (Track 4 milestone 4.2.2).
///
/// Two ADO-specific shapes drive this provider. Creating and updating a work item is a JSON Patch
/// document (<c>application/json-patch+json</c>) of <c>/fields/...</c> operations, not a JSON object —
/// posting an object gets a 400 that says nothing useful. And the work-item type is part of the create
/// URL (<c>/wit/workitems/$Bug</c>), so it cannot be omitted.
///
/// Auth is HTTP basic with an empty user and the PAT as the password, which is what Microsoft
/// documents. Service-hook deliveries carry no signature, so the receiver URL carries the shared
/// secret in the same way as Jira's.
/// </summary>
public class AzureDevOpsIssueTrackerProvider(ILogger logger, IOutboundHttpClient http) : IIssueTrackerProvider
{
    /// <summary>Pinned API version. ADO requires one on every request and defaults to nothing.</summary>
    private const string ApiVersion = "7.1";

    public IssueTrackerProviderKind Kind => IssueTrackerProviderKind.AzureDevOps;

    public string Name => "Azure DevOps Work Items";

    public IssueTrackerCapabilities Capabilities => new()
    {
        SupportsWebhooks = true,
        SupportsComments = true,
        SupportsTransitions = true,
        SupportsLabels = true,
        SupportsPriority = true,
        SetupHint = "Base URL is https://dev.azure.com/<organization>. Project is the project name. "
                    + "Leave the user blank and use a PAT with Work Items (read, write) as the "
                    + "credential. Work item type must be a type the project's process defines "
                    + "(Bug, Task, Issue). Service hooks are unsigned, so the receiver URL must carry "
                    + "the connection's webhook secret as ?secret=."
    };

    public async Task<ConnectionTestResult> TestConnectionAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var project = await SendAsync(connection, token, "GET",
            $"/_apis/projects/{Uri.EscapeDataString(connection.ProjectKey)}?api-version={ApiVersion}",
            null, null, ct);

        if (!project.IsSuccess)
            return project.StatusCode switch
            {
                0 => ConnectionTestResult.Fail($"Azure DevOps could not be reached: {project.TransportError}"),
                // ADO answers an invalid PAT with a 203 and a sign-in page rather than a 401, which is
                // the single most confusing thing about this API — so it is named explicitly.
                203 => ConnectionTestResult.Fail("Azure DevOps returned a sign-in redirect, which is what "
                                                 + "it does for an invalid or expired PAT."),
                401 => ConnectionTestResult.Fail("Azure DevOps rejected the PAT (401)."),
                404 => ConnectionTestResult.Fail($"Project '{connection.ProjectKey}' was not found. Check the "
                                                 + "organization in the base URL and the project name."),
                _ => ConnectionTestResult.Fail($"Azure DevOps answered HTTP {project.StatusCode}.")
            };

        // A 200 whose body is HTML is the other shape of the same authentication failure.
        if (project.Body?.TrimStart().StartsWith('<') == true)
            return ConnectionTestResult.Fail("Azure DevOps returned a sign-in page instead of JSON, which "
                                             + "means the PAT was not accepted.");

        var details = new Dictionary<string, string>();

        try
        {
            using var document = JsonDocument.Parse(project.Body!);
            if (document.RootElement.TryGetProperty("name", out var name))
                details["Project"] = name.GetString() ?? connection.ProjectKey;
        }
        catch (JsonException)
        {
            // Cosmetic.
        }

        return ConnectionTestResult.Ok($"Connected to Azure DevOps project '{connection.ProjectKey}'.", details);
    }

    public async Task<ExternalIssue> CreateIssueAsync(IssueTrackerConnection connection, string? token,
        IssueDraft draft, CancellationToken ct = default)
    {
        var type = string.IsNullOrWhiteSpace(draft.IssueType) ? "Bug" : draft.IssueType;

        var operations = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = Truncate(draft.Title, 255) },
            // ADO renders the description field as HTML, so newlines have to be <br/> or the whole
            // ticket arrives as one paragraph.
            new { op = "add", path = "/fields/System.Description", value = Html(draft.Description) }
        };

        if (draft.Labels.Count > 0)
            operations.Add(new
            {
                op = "add",
                path = "/fields/System.Tags",
                // ADO tags are semicolon-separated in one string.
                value = string.Join("; ", draft.Labels.Distinct())
            });

        if (!string.IsNullOrWhiteSpace(draft.Priority) && int.TryParse(draft.Priority, out var priority))
            operations.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = priority });

        var payload = JsonSerializer.Serialize(operations);

        var response = await SendAsync(connection, token, "POST",
            $"/{Uri.EscapeDataString(connection.ProjectKey)}/_apis/wit/workitems/${Uri.EscapeDataString(type)}"
            + $"?api-version={ApiVersion}",
            payload, "application/json-patch+json", ct);

        if (!response.IsSuccess)
            throw new IntegrationRequestException("Azure DevOps",
                $"Azure DevOps refused to create the work item (HTTP {response.StatusCode}): "
                + Excerpt(response.Body));

        return ParseWorkItem(connection, JsonDocument.Parse(response.Body!).RootElement);
    }

    public async Task<ExternalIssue> UpdateIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, string? comment, string? transitionTo, CancellationToken ct = default)
    {
        var id = Id(issueKey);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var payload = JsonSerializer.Serialize(new { text = Html(comment) });

            var commented = await SendAsync(connection, token, "POST",
                $"/{Uri.EscapeDataString(connection.ProjectKey)}/_apis/wit/workItems/{id}/comments"
                + "?api-version=7.1-preview.3",
                payload, "application/json", ct);

            if (!commented.IsSuccess)
                throw new IntegrationRequestException("Azure DevOps",
                    $"Could not comment on work item {id} (HTTP {commented.StatusCode}): "
                    + Excerpt(commented.Body));
        }

        if (!string.IsNullOrWhiteSpace(transitionTo))
        {
            var operations = new List<object>
            {
                new { op = "add", path = "/fields/System.State", value = transitionTo }
            };

            var patched = await SendAsync(connection, token, "PATCH",
                $"/{Uri.EscapeDataString(connection.ProjectKey)}/_apis/wit/workitems/{id}"
                + $"?api-version={ApiVersion}",
                JsonSerializer.Serialize(operations), "application/json-patch+json", ct);

            if (!patched.IsSuccess)
                throw new IntegrationRequestException("Azure DevOps",
                    $"Could not set work item {id} to '{transitionTo}' (HTTP {patched.StatusCode}): "
                    + Excerpt(patched.Body));

            return ParseWorkItem(connection, JsonDocument.Parse(patched.Body!).RootElement);
        }

        return await GetIssueAsync(connection, token, issueKey, ct) ?? new ExternalIssue { Key = issueKey };
    }

    public async Task<ExternalIssue?> GetIssueAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var response = await SendAsync(connection, token, "GET",
            $"/{Uri.EscapeDataString(connection.ProjectKey)}/_apis/wit/workitems/{Id(issueKey)}"
            + $"?api-version={ApiVersion}",
            null, null, ct);

        if (response.StatusCode == 404) return null;

        if (!response.IsSuccess)
            throw new IntegrationRequestException("Azure DevOps",
                $"Could not read work item {issueKey} (HTTP {response.StatusCode}).");

        return ParseWorkItem(connection, JsonDocument.Parse(response.Body!).RootElement);
    }

    public ExternalIssue? ParseWebhook(IssueTrackerConnection connection, string? webhookSecret,
        string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        // ADO service hooks offer basic auth on the subscription rather than a body signature, so the
        // shared secret arrives in the URL and is checked by the controller before this is called.
        try
        {
            using var document = JsonDocument.Parse(rawBody);

            if (!document.RootElement.TryGetProperty("resource", out var resource)) return null;

            // A "workitem.updated" hook nests the current state under resource.revision; a
            // "workitem.created" hook has the fields directly on resource.
            var element = resource.TryGetProperty("revision", out var revision) ? revision : resource;

            return ParseWorkItem(connection, element);
        }
        catch (JsonException ex)
        {
            logger.Warning("Unparseable Azure DevOps webhook body: {Message}", ex.Message);
            return null;
        }
    }

    private static ExternalIssue ParseWorkItem(IssueTrackerConnection connection, JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idValue)
            ? idValue.ValueKind == JsonValueKind.Number ? idValue.GetInt64().ToString() : idValue.GetString()
            : null;

        string? state = null;
        string? title = null;
        DateTime? updated = null;

        if (item.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            if (fields.TryGetProperty("System.State", out var stateField)) state = stateField.GetString();
            if (fields.TryGetProperty("System.Title", out var titleField)) title = titleField.GetString();
            if (fields.TryGetProperty("System.ChangedDate", out var changed)
                && DateTime.TryParse(changed.GetString(), out var parsed))
                updated = parsed.ToUniversalTime();
        }

        return new ExternalIssue
        {
            Key = id ?? string.Empty,
            Id = id,
            Url = id == null
                ? null
                : $"{connection.BaseUrl.TrimEnd('/')}/{connection.ProjectKey}/_workitems/edit/{id}",
            Title = title,
            Status = state,
            // ADO state names are process-defined, so this is the set the out-of-box processes use for
            // terminal states. A project with custom states configures the status mapping instead of
            // relying on this flag.
            IsClosed = state != null && ClosedStates.Contains(state),
            UpdatedAt = updated
        };
    }

    private static readonly HashSet<string> ClosedStates =
        new(StringComparer.OrdinalIgnoreCase) { "Closed", "Done", "Resolved", "Removed", "Completed" };

    internal static string Id(string issueKey)
    {
        var trimmed = (issueKey ?? string.Empty).Trim().TrimStart('#');

        var slash = trimmed.LastIndexOf('/');
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];

        return trimmed;
    }

    /// <summary>
    /// Minimal Markdown-ish to HTML: escape, then turn newlines into breaks. ADO's description field
    /// is HTML, so an unescaped finding title would be an HTML-injection into someone's work item.
    /// </summary>
    internal static string Html(string text) =>
        System.Net.WebUtility.HtmlEncode(text ?? string.Empty)
            .Replace("\r\n", "<br/>")
            .Replace("\n", "<br/>");

    private Task<OutboundHttpResponse> SendAsync(IssueTrackerConnection connection, string? token,
        string method, string path, string? body, string? contentType, CancellationToken ct)
    {
        // Empty user, PAT as password: what Microsoft documents for PAT basic auth.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.AuthUser}:{token}"));

        return http.SendAsync(new OutboundHttpRequest
        {
            Method = method,
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Body = body,
            ContentType = contentType ?? "application/json",
            Headers =
            {
                ["Authorization"] = "Basic " + basic,
                ["Accept"] = "application/json"
            }
        }, ct);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Excerpt(string? body) =>
        string.IsNullOrWhiteSpace(body) ? "(no response body)"
            : body.Length <= 400 ? body.Trim() : body[..400].Trim() + "…";
}
