using System.Text.Json;
using DAL.Entities;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// The Jira platform metadata the mapping editors need (Track 4 milestone 4.6): the site's fields,
/// its priorities, and a project's statuses.
///
/// This is what turns the configuration screen from three text boxes into three pickers. Milestone
/// 4.2 shipped a priority mapping and a status mapping that the operator had to type from memory,
/// including custom-field ids like <c>customfield_10012</c> — a value nobody knows and everybody
/// mistypes. Reading the real vocabulary from the site is the difference between a mapping that is
/// configurable and one that is merely editable.
/// </summary>
public class JiraMetadataClient(IOutboundHttpClient http) : IJiraMetadataClient
{
    public async Task<List<JiraFieldView>> GetFieldsAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection, "/rest/api/3/field");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Could not read the site's fields (HTTP {response.StatusCode}).");

        using var document = JiraHttp.TryParse(response.Body);

        var fields = new List<JiraFieldView>();

        if (document == null || document.RootElement.ValueKind != JsonValueKind.Array) return fields;

        foreach (var field in document.RootElement.EnumerateArray())
        {
            var id = JiraHttp.Str(field, "id");
            if (id == null) continue;

            fields.Add(new JiraFieldView
            {
                Id = id,
                Name = JiraHttp.Str(field, "name") ?? id,
                Type = field.TryGetProperty("schema", out var schema)
                    ? JiraHttp.Str(schema, "type")
                    : null,
                IsCustom = JiraHttp.Bool(field, "custom")
            });
        }

        // Custom fields last, then by name: the native fields are the ones an operator reaches for
        // first, and a site can have three hundred custom fields to scroll past otherwise.
        return fields.OrderBy(f => f.IsCustom).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<string>> GetPrioritiesAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection, "/rest/api/3/priority");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Could not read the site's priorities (HTTP {response.StatusCode}).");

        using var document = JiraHttp.TryParse(response.Body);

        var priorities = new List<string>();

        if (document == null) return priorities;

        // A bare array on most sites, a paginated envelope on some. Both, for the same reason as the
        // Assets object types: the failure is an empty picker that looks like a permissions problem.
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("values", out var values) ? values : default;

        if (items.ValueKind != JsonValueKind.Array) return priorities;

        foreach (var priority in items.EnumerateArray())
            if (JiraHttp.Str(priority, "name") is { Length: > 0 } name) priorities.Add(name);

        return priorities;
    }

    public async Task<List<string>> GetProjectStatusesAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection,
            $"/rest/api/3/project/{Uri.EscapeDataString(connection.ProjectKey)}/statuses");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira",
                $"Could not read the statuses of project '{connection.ProjectKey}' "
                + $"(HTTP {response.StatusCode}).");

        using var document = JiraHttp.TryParse(response.Body);

        // The response is per issue type, and one status usually appears under several of them.
        // Deduplicated case-insensitively, because the status mapping is keyed on the name and offering
        // "Done" four times in a picker invites four mapping rows the schema then rejects.
        var statuses = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (document == null || document.RootElement.ValueKind != JsonValueKind.Array)
            return statuses.ToList();

        foreach (var issueType in document.RootElement.EnumerateArray())
        {
            if (!issueType.TryGetProperty("statuses", out var list)
                || list.ValueKind != JsonValueKind.Array) continue;

            foreach (var status in list.EnumerateArray())
                if (JiraHttp.Str(status, "name") is { Length: > 0 } name) statuses.Add(name);
        }

        return statuses.ToList();
    }
}
