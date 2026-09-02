using System.Text.Json;
using DAL.Entities;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Jira Service Management Cloud, <c>/rest/servicedeskapi/…</c> (Track 4 milestone 4.6).
///
/// Read-only by construction: there is no method here that writes to a service desk. NetRisk creates
/// and transitions Jira *issues* through <see cref="JiraIssueTrackerProvider"/>, which already exists
/// and already has the operator's status mapping; a second write path through the Service Desk API
/// would be a second place for that policy to live.
///
/// Two shapes of this API are worth knowing. Every collection is paginated with
/// <c>start</c>/<c>limit</c> and reports <c>isLastPage</c> rather than a total, so pagination is a
/// loop and not arithmetic. And SLA arrives as zero-or-more <c>completedCycles</c> plus zero-or-one
/// <c>ongoingCycle</c> per metric, which is why the mirror stores a row per cycle: a reopened request
/// starts a second cycle of the same metric, and collapsing them would erase the first breach.
/// </summary>
public class JiraServiceManagementClient(ILogger logger, IOutboundHttpClient http)
    : IJiraServiceManagementClient
{
    /// <summary>Jira's own ceiling for these endpoints; asking for more is silently clamped anyway.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// A hard stop on pagination. A service desk with a runaway queue must not turn one sync into an
    /// unbounded walk that holds a job and somebody else's rate limit for an hour.
    /// </summary>
    private const int MaxPages = 200;

    public async Task<List<JiraServiceDeskView>> GetServiceDesksAsync(IssueTrackerConnection connection,
        string? token, CancellationToken ct = default)
    {
        var desks = new List<JiraServiceDeskView>();

        await foreach (var desk in PageAsync(connection, token, "/rest/servicedeskapi/servicedesk", ct))
        {
            var id = JiraHttp.Int(desk, "id");
            if (id == null) continue;

            desks.Add(new JiraServiceDeskView
            {
                Id = id.Value,
                ProjectKey = JiraHttp.Str(desk, "projectKey") ?? string.Empty,
                ProjectName = JiraHttp.Str(desk, "projectName") ?? string.Empty
            });
        }

        return desks;
    }

    public async Task<List<JiraRequestTypeView>> GetRequestTypesAsync(IssueTrackerConnection connection,
        string? token, int serviceDeskId, CancellationToken ct = default)
    {
        var types = new List<JiraRequestTypeView>();

        await foreach (var type in PageAsync(connection, token,
                           $"/rest/servicedeskapi/servicedesk/{serviceDeskId}/requesttype", ct))
        {
            var id = JiraHttp.Str(type, "id");
            if (id == null) continue;

            types.Add(new JiraRequestTypeView
            {
                Id = id,
                Name = JiraHttp.Str(type, "name") ?? id,
                Description = JiraHttp.Str(type, "description")
            });
        }

        return types;
    }

    public async Task<List<JiraQueueView>> GetQueuesAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, CancellationToken ct = default)
    {
        var queues = new List<JiraQueueView>();

        // includeCount asks Jira to run each queue's JQL for a count. Worth one extra round trip: the
        // operator picking queues to import needs to know which of them has fifty issues and which
        // has fifty thousand *before* choosing, not after the first sync.
        await foreach (var queue in PageAsync(connection, token,
                           $"/rest/servicedeskapi/servicedesk/{serviceDeskId}/queue?includeCount=true", ct))
        {
            var id = JiraHttp.Int(queue, "id");
            if (id == null) continue;

            queues.Add(new JiraQueueView
            {
                Id = id.Value,
                Name = JiraHttp.Str(queue, "name") ?? id.Value.ToString(),
                Jql = JiraHttp.Str(queue, "jql"),
                IssueCount = JiraHttp.Int(queue, "issueCount")
            });
        }

        return queues;
    }

    public async Task<List<string>> GetQueueIssueKeysAsync(IssueTrackerConnection connection, string? token,
        int serviceDeskId, int queueId, int max, CancellationToken ct = default)
    {
        var keys = new List<string>();

        await foreach (var issue in PageAsync(connection, token,
                           $"/rest/servicedeskapi/servicedesk/{serviceDeskId}/queue/{queueId}/issue", ct))
        {
            if (JiraHttp.Str(issue, "key") is { Length: > 0 } key) keys.Add(key);

            if (keys.Count >= max) break;
        }

        return keys;
    }

    public async Task<JsmRequest?> GetRequestAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection,
            $"/rest/servicedeskapi/request/{Uri.EscapeDataString(issueKey)}"
            + "?expand=requestType,serviceDesk,status,sla");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        // 404 is "not a customer request", which is normal: a Jira Software issue on the same site is
        // not visible through the Service Desk API, and treating that as an error would fill the log
        // with failures for issues NetRisk was never going to mirror.
        if (response.StatusCode == 404) return null;

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira Service Management",
                $"Could not read request {issueKey} (HTTP {response.StatusCode}): "
                + JiraHttp.Excerpt(response.Body));

        using var document = JiraHttp.TryParse(response.Body);

        if (document == null)
            throw new Model.Exceptions.IntegrationRequestException("Jira Service Management",
                $"The response for {issueKey} was not JSON. Check that the base URL is the Jira site "
                + "and not a proxy or a sign-in page.");

        return ParseRequest(connection, document.RootElement);
    }

    public async Task<List<JsmSlaCycle>> GetSlaAsync(IssueTrackerConnection connection, string? token,
        string issueKey, CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection,
            $"/rest/servicedeskapi/request/{Uri.EscapeDataString(issueKey)}/sla");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (response.StatusCode == 404) return [];

        if (!response.IsSuccess)
        {
            // A warning rather than a throw: SLA is an enrichment. A request that mirrors without its
            // cycles is still worth having, and failing the whole sync over one metric read would
            // lose the status changes that came with it.
            logger.Warning("Could not read the SLA of {Issue} (HTTP {Status})", issueKey,
                response.StatusCode);
            return [];
        }

        using var document = JiraHttp.TryParse(response.Body);

        if (document == null) return [];

        var cycles = new List<JsmSlaCycle>();

        if (document.RootElement.TryGetProperty("values", out var values)
            && values.ValueKind == JsonValueKind.Array)
            foreach (var metric in values.EnumerateArray())
                cycles.AddRange(ParseSlaMetric(metric));

        return cycles;
    }

    public async Task<string?> GetAssetsWorkspaceIdAsync(IssueTrackerConnection connection, string? token,
        CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection, "/rest/servicedeskapi/assets/workspace");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess)
        {
            logger.Warning("Could not discover the Assets workspace id (HTTP {Status})",
                response.StatusCode);
            return null;
        }

        using var document = JiraHttp.TryParse(response.Body);

        if (document == null
            || !document.RootElement.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array) return null;

        // The endpoint is paginated and returns a list, but a Jira site has one Assets workspace. The
        // first is taken rather than the list surfaced, because a UI asking an operator to choose
        // between one option is a UI asking a question with no answer.
        foreach (var workspace in values.EnumerateArray())
            if (JiraHttp.Str(workspace, "workspaceId") is { Length: > 0 } id) return id;

        return null;
    }

    public async Task<ConnectionTestResult> TestServiceDeskAsync(IssueTrackerConnection connection,
        string? token, int serviceDeskId, CancellationToken ct = default)
    {
        var url = JiraHttp.SiteUrl(connection, $"/rest/servicedeskapi/servicedesk/{serviceDeskId}");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess) return JiraHttp.Describe(response, "Jira Service Management");

        using var document = JiraHttp.TryParse(response.Body);

        var name = document == null ? null : JiraHttp.Str(document.RootElement, "projectName");

        return ConnectionTestResult.Ok(
            $"Read service desk {serviceDeskId}{(name == null ? "" : $" ('{name}')")}.",
            name == null ? null : new Dictionary<string, string> { ["Service desk"] = name });
    }

    // --- parsing ----------------------------------------------------------------------------

    internal static JsmRequest ParseRequest(IssueTrackerConnection connection, JsonElement request)
    {
        var result = new JsmRequest
        {
            IssueKey = JiraHttp.Str(request, "issueKey") ?? string.Empty,
            IssueId = JiraHttp.Str(request, "issueId"),
            RequestUrl = request.TryGetProperty("_links", out var links)
                ? JiraHttp.Str(links, "web") ?? JiraHttp.Str(links, "self")
                : null
        };

        if (request.TryGetProperty("requestType", out var type) && type.ValueKind == JsonValueKind.Object)
        {
            result.RequestTypeId = JiraHttp.Str(type, "id");
            result.RequestTypeName = JiraHttp.Str(type, "name");
            result.ServiceDeskId = JiraHttp.Int(type, "serviceDeskId");
        }

        if (request.TryGetProperty("serviceDesk", out var desk) && desk.ValueKind == JsonValueKind.Object)
            result.ServiceDeskId ??= JiraHttp.Int(desk, "id");

        if (request.TryGetProperty("currentStatus", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            result.StatusName = JiraHttp.Str(status, "status");
            // The Service Desk API reports a *portal* status category, whose vocabulary is
            // "DONE"/"IN_PROGRESS"/"NEW" rather than the platform API's lower-case keys. Normalised
            // here so the mirror holds one vocabulary and the closed test is one comparison.
            result.StatusCategory = JiraHttp.Str(status, "statusCategory")?.ToLowerInvariant()
                .Replace('_', '-');
        }

        if (request.TryGetProperty("reporter", out var reporter) && reporter.ValueKind == JsonValueKind.Object)
        {
            result.ReporterAccountId = JiraHttp.Str(reporter, "accountId");
            result.ReporterDisplayName = JiraHttp.Str(reporter, "displayName");
        }

        // Field values arrive as a list of {fieldId, label, value} rather than an object, so summary,
        // priority and assignee are looked up by field id instead of read as properties.
        if (request.TryGetProperty("requestFieldValues", out var fields)
            && fields.ValueKind == JsonValueKind.Array)
            foreach (var field in fields.EnumerateArray())
            {
                var id = JiraHttp.Str(field, "fieldId");

                if (id == "summary") result.Summary ??= JiraHttp.Str(field, "value");
            }

        result.CreatedAt = JiraHttp.JsmDate(request, "createdDate");
        result.UpdatedAt = JiraHttp.JsmDate(request, "updatedDate");
        result.IsClosed = string.Equals(result.StatusCategory, "done", StringComparison.OrdinalIgnoreCase);

        if (request.TryGetProperty("sla", out var sla))
        {
            // expand=sla nests the cycles under sla.values; a bare list is accepted too, because
            // Data Center and older Cloud builds differ here and the difference is not worth a
            // second parser.
            var values = sla.ValueKind == JsonValueKind.Object
                         && sla.TryGetProperty("values", out var nested)
                ? nested
                : sla;

            if (values.ValueKind == JsonValueKind.Array)
                foreach (var metric in values.EnumerateArray())
                    result.Slas.AddRange(ParseSlaMetric(metric));
        }

        if (string.IsNullOrWhiteSpace(result.RequestUrl) && result.IssueKey.Length > 0)
            result.RequestUrl = $"{connection.BaseUrl.TrimEnd('/')}/browse/{result.IssueKey}";

        return result;
    }

    /// <summary>
    /// One metric's cycles. The ongoing cycle is emitted last so that, when two cycles of one metric
    /// land in the same pass, the ongoing one is the row a "latest state" read finds.
    /// </summary>
    internal static List<JsmSlaCycle> ParseSlaMetric(JsonElement metric)
    {
        var cycles = new List<JsmSlaCycle>();

        var id = JiraHttp.Str(metric, "id");
        var name = JiraHttp.Str(metric, "name") ?? id ?? "SLA";

        if (metric.TryGetProperty("completedCycles", out var completed)
            && completed.ValueKind == JsonValueKind.Array)
            foreach (var cycle in completed.EnumerateArray())
                cycles.Add(ParseCycle(cycle, id, name, ongoing: false));

        if (metric.TryGetProperty("ongoingCycle", out var ongoing)
            && ongoing.ValueKind == JsonValueKind.Object)
            cycles.Add(ParseCycle(ongoing, id, name, ongoing: true));

        return cycles;
    }

    private static JsmSlaCycle ParseCycle(JsonElement cycle, string? metricId, string metricName,
        bool ongoing)
    {
        return new JsmSlaCycle
        {
            MetricId = metricId,
            MetricName = metricName,
            IsOngoing = ongoing,
            Breached = JiraHttp.Bool(cycle, "breached"),
            Paused = JiraHttp.Bool(cycle, "paused"),
            GoalDurationMs = Duration(cycle, "goalDuration"),
            ElapsedMs = Duration(cycle, "elapsedTime"),
            RemainingMs = Duration(cycle, "remainingTime"),
            CycleStartAt = JiraHttp.JsmDate(cycle, "startTime"),
            CycleStopAt = JiraHttp.JsmDate(cycle, "stopTime")
        };
    }

    /// <summary>
    /// A duration, which Jira wraps as <c>{"millis":…,"friendly":"…"}</c>. The millis are read and the
    /// friendly form discarded: it is localised to the *instance's* locale, so storing it would put
    /// Portuguese in one customer's mirror and English in another's for the same number.
    /// </summary>
    private static long? Duration(JsonElement cycle, string property)
    {
        if (cycle.ValueKind != JsonValueKind.Object
            || !cycle.TryGetProperty(property, out var node)) return null;

        if (node.ValueKind == JsonValueKind.Number) return JiraHttp.Long(cycle, property);

        return node.ValueKind == JsonValueKind.Object ? JiraHttp.Long(node, "millis") : null;
    }

    /// <summary>
    /// Walks a <c>start</c>/<c>limit</c> paginated Service Desk collection.
    ///
    /// Driven by <c>isLastPage</c> rather than by comparing counts: these endpoints do not report a
    /// total, so "did I get a full page" is the only other signal available and it is wrong exactly
    /// when the collection size is a multiple of the page size.
    /// </summary>
    private async IAsyncEnumerable<JsonElement> PageAsync(IssueTrackerConnection connection, string? token,
        string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var separator = path.Contains('?') ? "&" : "?";
        var start = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = JiraHttp.SiteUrl(connection, $"{path}{separator}start={start}&limit={PageSize}");

            var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

            if (!response.IsSuccess)
                throw new Model.Exceptions.IntegrationRequestException("Jira Service Management",
                    $"Jira answered HTTP {response.StatusCode} for {path}: "
                    + JiraHttp.Excerpt(response.Body));

            using var document = JiraHttp.TryParse(response.Body);

            if (document == null
                || !document.RootElement.TryGetProperty("values", out var values)
                || values.ValueKind != JsonValueKind.Array) yield break;

            var count = 0;

            foreach (var value in values.EnumerateArray())
            {
                // Cloned because the JsonDocument is disposed when this iteration ends, and a caller
                // holding an element into a disposed document reads freed memory.
                yield return value.Clone();
                count++;
            }

            if (count == 0 || JiraHttp.Bool(document.RootElement, "isLastPage")) yield break;

            start += PageSize;
        }

        logger.Warning("Stopped paginating {Path} after {Pages} pages", path, MaxPages);
    }
}
