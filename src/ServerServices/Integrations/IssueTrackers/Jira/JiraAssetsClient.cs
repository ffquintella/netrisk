using System.Text.Json;
using DAL.Entities;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Jira Assets (the Service Management CMDB, formerly Insight) on Cloud and Data Center
/// (Track 4 milestone 4.6).
///
/// Cloud serves Assets from <c>api.atlassian.com/jsm/assets/workspace/{workspaceId}/v1</c>, behind a
/// workspace id discovered from the site (<see cref="IJiraServiceManagementClient.GetAssetsWorkspaceIdAsync"/>).
/// Data Center serves it from the instance itself, with no workspace: <c>/rest/assets/1.0</c> on
/// Assets 10.x, <c>/rest/insight/1.0</c> on the Insight 9.x it replaced. The schema list is wrapped
/// differently and the object search is a paged GET instead of an offset POST; the rest is the same
/// object model. Every URL is built by <see cref="JiraHttp"/>, never by concatenation here.
///
/// All calls go through <see cref="IOutboundHttpClient"/>, so <c>OutboundUrlPolicy</c> evaluates
/// <c>api.atlassian.com</c> and a Data Center host exactly as it evaluates the issue calls.
/// </summary>
public class JiraAssetsClient(ILogger logger, IOutboundHttpClient http) : IJiraAssetsClient
{
    /// <summary>Assets' AQL page size. Larger pages are accepted and then clamped by Assets.</summary>
    internal const int PageSize = 100;

    public async Task<List<JiraObjectSchemaView>> GetSchemasAsync(IssueTrackerConnection connection,
        string? token, string? workspaceId, CancellationToken ct = default)
    {
        // Data Center's list takes no paging or count parameters; it always returns every schema
        // the account can see, with its object count.
        using var document = await GetJsonAsync(connection, token, workspaceId,
            "/objectschema/list?maxResults=200&includeCounts=true", "/objectschema/list",
            "the Assets schema list", ct);

        var schemas = new List<JiraObjectSchemaView>();

        // Cloud pages them under "values"; Data Center answers {"objectschemas":[...]}.
        foreach (var schema in Items(document, "values", "objectschemas"))
        {
            var id = JiraHttp.Int(schema, "id");
            if (id == null) continue;

            schemas.Add(new JiraObjectSchemaView
            {
                Id = id.Value,
                Name = JiraHttp.Str(schema, "name") ?? id.Value.ToString(),
                ObjectSchemaKey = JiraHttp.Str(schema, "objectSchemaKey"),
                ObjectCount = JiraHttp.Int(schema, "objectCount")
            });
        }

        return schemas;
    }

    public async Task<List<JiraObjectTypeView>> GetObjectTypesAsync(IssueTrackerConnection connection,
        string? token, string? workspaceId, int schemaId, CancellationToken ct = default)
    {
        // The flat variant, not the hierarchical one: the mapping editor needs a pickable list, and a
        // nested tree would have to be flattened for the picker anyway. It also lives in the
        // *objectschema* group — there is no object-type listing under /objecttype, which is the
        // wrong guess to make here. Same path on both deployments.
        var path = $"/objectschema/{schemaId}/objecttypes/flat";

        using var document = await GetJsonAsync(connection, token, workspaceId, path, path,
            $"the object types of schema {schemaId}", ct);

        var types = new List<JiraObjectTypeView>();

        // A bare array rather than a paginated envelope, on both. Both shapes are handled, because
        // getting it wrong shows up as "no object types" and reads like a permissions problem.
        foreach (var type in Items(document, "values"))
        {
            var id = JiraHttp.Int(type, "id");
            if (id == null) continue;

            types.Add(new JiraObjectTypeView
            {
                Id = id.Value,
                Name = JiraHttp.Str(type, "name") ?? id.Value.ToString(),
                ParentObjectTypeId = JiraHttp.Int(type, "parentObjectTypeId"),
                ObjectCount = JiraHttp.Int(type, "objectCount")
            });
        }

        return types;
    }

    public async Task<List<JiraObjectTypeAttributeView>> GetAttributesAsync(
        IssueTrackerConnection connection, string? token, string? workspaceId, int objectTypeId,
        CancellationToken ct = default)
    {
        var path = $"/objecttype/{objectTypeId}/attributes";

        using var document = await GetJsonAsync(connection, token, workspaceId, path, path,
            $"the attributes of object type {objectTypeId}", ct);

        var attributes = new List<JiraObjectTypeAttributeView>();

        foreach (var attribute in Items(document, "values"))
        {
            var id = JiraHttp.Int(attribute, "id");
            if (id == null) continue;

            attributes.Add(new JiraObjectTypeAttributeView
            {
                Id = id.Value,
                Name = JiraHttp.Str(attribute, "name") ?? id.Value.ToString(),
                Type = TypeLabel(attribute),
                IsLabel = JiraHttp.Bool(attribute, "label")
            });
        }

        return attributes;
    }

    public async Task<AssetSearchPage> SearchAsync(IssueTrackerConnection connection, string? token,
        string? workspaceId, string aql, int startAt, int maxResults, CancellationToken ct = default)
    {
        var size = Math.Clamp(maxResults, 1, PageSize);
        var dataCenter = JiraDialect.IsDataCenter(connection.Provider);

        // Cloud takes the query as JSON in the body; Data Center as a query parameter of a GET. Either
        // way it is the customer's AQL over their own schema, sent to Jira and never interpolated
        // into SQL, and it goes through a JSON writer or Uri.EscapeDataString — never a format string
        // — so a quote or an ampersand in an object name cannot produce a different request.
        var response = dataCenter
            ? (await SendDataCenterAsync(connection, token,
                root => DataCenterSearchPath(root, aql, startAt / size + 1, size), ct)).Response
            : await JiraHttp.SendAsync(http, connection, token, "POST",
                JiraHttp.AssetsUrl(RequireWorkspace(workspaceId),
                    $"/object/aql?startAt={startAt}&maxResults={size}&includeAttributes=true"),
                JsonSerializer.Serialize(new { qlQuery = aql }), ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                $"Assets refused the query (HTTP {response.StatusCode}): {JiraHttp.Excerpt(response.Body)}"
                + (response.StatusCode == 400
                    ? $" — check the AQL: {aql}"
                    : string.Empty)
                + (dataCenter && response.StatusCode == 404 ? DataCenterNotFoundHint : string.Empty));

        using var document = JiraHttp.TryParse(response.Body);

        if (document == null)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                "The Assets search response was not JSON.");

        var root = document.RootElement;

        var page = new AssetSearchPage
        {
            Total = JiraHttp.Int(root, dataCenter ? "totalFilterCount" : "total"),
            IsLast = true
        };

        foreach (var value in Items(document, dataCenter ? "objectEntries" : "values"))
            page.Objects.Add(ParseObject(value));

        if (dataCenter)
        {
            // Page-numbered, with the total always present; the total decides, and a short page
            // only when an older Insight omitted it.
            page.IsLast = page.Total is { } total
                ? startAt + page.Objects.Count >= total
                : page.Objects.Count < size;

            return page;
        }

        // isLast when Assets says so; otherwise inferred from a short page. Assets has changed which
        // of the two it sends between versions of this endpoint, so neither is trusted alone.
        page.IsLast = root.TryGetProperty("isLast", out var isLast)
                      && isLast.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? isLast.ValueKind == JsonValueKind.True
            : page.Objects.Count < maxResults;

        return page;
    }

    /// <summary>
    /// The Data Center object search under one root. Insight named the language IQL and the
    /// parameter after it; Assets 10 renamed both. Pages are 1-based.
    /// </summary>
    internal static string DataCenterSearchPath(string root, string aql, int page, int size)
    {
        var query = Uri.EscapeDataString(aql);

        return root == JiraDialect.DataCenterInsightRoot
            ? $"/iql/objects?iql={query}&page={page}&resultPerPage={size}&includeAttributes=true"
            : $"/aql/objects?qlQuery={query}&page={page}&resultPerPage={size}&includeAttributes=true";
    }

    public async Task<ConnectionTestResult> TestAsync(IssueTrackerConnection connection, string? token,
        string? workspaceId, CancellationToken ct = default)
    {
        if (JiraDialect.IsDataCenter(connection.Provider))
        {
            var (dcResponse, root) = await SendDataCenterAsync(connection, token,
                _ => "/objectschema/list", ct);

            if (!dcResponse.IsSuccess) return JiraHttp.Describe(dcResponse, "Jira Assets");

            using var dcDocument = JiraHttp.TryParse(dcResponse.Body);

            return ConnectionTestResult.Ok(
                $"Reached Assets on {connection.BaseUrl} and found "
                + $"{Items(dcDocument, "objectschemas").Count()} object schema(s).",
                new Dictionary<string, string> { ["Assets API"] = root });
        }

        // The schema list rather than a lighter probe, because it proves the three things that can
        // each be independently wrong: the credential is accepted by api.atlassian.com, the workspace
        // id resolves, and the site's plan actually includes Assets.
        var url = JiraHttp.AssetsUrl(RequireWorkspace(workspaceId), "/objectschema/list?maxResults=1");

        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess) return JiraHttp.Describe(response, "Jira Assets");

        using var document = JiraHttp.TryParse(response.Body);

        var count = document != null
                    && document.RootElement.TryGetProperty("total", out var total)
                    && total.TryGetInt32(out var parsed)
            ? parsed
            : (int?)null;

        return ConnectionTestResult.Ok(
            $"Reached Assets workspace {workspaceId}"
            + (count == null ? "." : $" and found {count} object schema(s)."),
            new Dictionary<string, string> { ["Assets workspace"] = workspaceId! });
    }

    // --- parsing ----------------------------------------------------------------------------

    /// <summary>
    /// One Assets object.
    ///
    /// Values are keyed by <c>objectTypeAttributeId</c>, because that is what the payload reliably
    /// carries; the attribute's *name* is only present when Assets chose to inline
    /// <c>objectTypeAttribute</c>, so it is recorded when available and resolved from
    /// <see cref="GetAttributesAsync"/> otherwise. Reading names out of this payload alone is the
    /// mistake that makes a mapping work on one site and silently map nothing on the next.
    /// </summary>
    internal static AssetObjectPayload ParseObject(JsonElement value)
    {
        var payload = new AssetObjectPayload
        {
            ObjectId = JiraHttp.Str(value, "id") ?? string.Empty,
            ObjectKey = JiraHttp.Str(value, "objectKey"),
            Label = JiraHttp.Str(value, "label"),
            CreatedAt = JiraHttp.Utc(value, "created"),
            UpdatedAt = JiraHttp.Utc(value, "updated"),
            RawJson = value.GetRawText()
        };

        if (value.TryGetProperty("objectType", out var type) && type.ValueKind == JsonValueKind.Object)
        {
            payload.ObjectTypeId = JiraHttp.Int(type, "id");
            payload.ObjectTypeName = JiraHttp.Str(type, "name");
        }

        if (!value.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array) return payload;

        foreach (var attribute in attributes.EnumerateArray())
        {
            var attributeId = JiraHttp.Int(attribute, "objectTypeAttributeId");

            string? name = null;

            if (attribute.TryGetProperty("objectTypeAttribute", out var definition)
                && definition.ValueKind == JsonValueKind.Object)
            {
                name = JiraHttp.Str(definition, "name");
                attributeId ??= JiraHttp.Int(definition, "id");
            }

            var values = ReadValues(attribute);

            if (values.Count == 0) continue;

            if (attributeId != null) payload.Attributes[attributeId.Value] = values;
            if (name != null) payload.AttributesByName[name] = values;
        }

        return payload;
    }

    /// <summary>
    /// The values of one attribute.
    ///
    /// <c>displayValue</c> is preferred over <c>value</c> because a reference attribute's raw value is
    /// an internal id and its display value is the referenced object's label — so "who owns this
    /// server" reads as a name rather than as <c>4711</c>. A user attribute keeps its display name for
    /// the same reason.
    /// </summary>
    private static List<string> ReadValues(JsonElement attribute)
    {
        var results = new List<string>();

        if (!attribute.TryGetProperty("objectAttributeValues", out var values)
            || values.ValueKind != JsonValueKind.Array) return results;

        foreach (var value in values.EnumerateArray())
        {
            var text = JiraHttp.Str(value, "displayValue") ?? JiraHttp.Str(value, "value");

            if (string.IsNullOrWhiteSpace(text)
                && value.TryGetProperty("referencedObject", out var referenced))
                text = JiraHttp.Str(referenced, "label");

            if (string.IsNullOrWhiteSpace(text) && value.TryGetProperty("user", out var user))
                text = JiraHttp.Str(user, "displayName") ?? JiraHttp.Str(user, "emailAddress");

            if (!string.IsNullOrWhiteSpace(text)) results.Add(text.Trim());
        }

        return results;
    }

    private static string? TypeLabel(JsonElement attribute)
    {
        if (attribute.TryGetProperty("defaultType", out var defaultType)
            && defaultType.ValueKind == JsonValueKind.Object
            && JiraHttp.Str(defaultType, "name") is { Length: > 0 } name) return name;

        // Assets' numeric attribute types: 0 default, 1 reference to another object, 2 user, 3 confluence,
        // 4 group, 5 version, 6 project, 7 status. Only the ones an operator would map are named; the
        // rest keep their number, which is still more useful in a picker than a blank cell.
        return JiraHttp.Int(attribute, "type") switch
        {
            0 => "Default",
            1 => "Object reference",
            2 => "User",
            4 => "Group",
            7 => "Status",
            var other => other?.ToString()
        };
    }

    private async Task<JsonDocument?> GetJsonAsync(IssueTrackerConnection connection, string? token,
        string? workspaceId, string cloudPath, string dataCenterPath, string what, CancellationToken ct)
    {
        var dataCenter = JiraDialect.IsDataCenter(connection.Provider);

        var response = dataCenter
            ? (await SendDataCenterAsync(connection, token, _ => dataCenterPath, ct)).Response
            : await JiraHttp.SendAsync(http, connection, token, "GET",
                JiraHttp.AssetsUrl(RequireWorkspace(workspaceId), cloudPath), null, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                $"Could not read {what} (HTTP {response.StatusCode}): {JiraHttp.Excerpt(response.Body)}"
                + (dataCenter && response.StatusCode == 404 ? DataCenterNotFoundHint : string.Empty));

        var document = JiraHttp.TryParse(response.Body);

        if (document == null)
            logger.Warning("The Assets response for {What} was not JSON", what);

        return document;
    }

    /// <summary>
    /// One Data Center Assets request: the Assets 10 root first, and the Insight root only when that
    /// one is a 404. Anything else — a 401, a 403 — is the answer, because retrying a refused
    /// credential against a second path is how Seraph's CAPTCHA gets tripped.
    /// </summary>
    private async Task<(OutboundHttpResponse Response, string Root)> SendDataCenterAsync(
        IssueTrackerConnection connection, string? token, Func<string, string> path, CancellationToken ct)
    {
        var response = await JiraHttp.SendAsync(http, connection, token, "GET",
            JiraHttp.DataCenterAssetsUrl(connection, JiraDialect.DataCenterAssetsRoot,
                path(JiraDialect.DataCenterAssetsRoot)), null, ct);

        if (response.StatusCode != 404) return (response, JiraDialect.DataCenterAssetsRoot);

        return (await JiraHttp.SendAsync(http, connection, token, "GET",
            JiraHttp.DataCenterAssetsUrl(connection, JiraDialect.DataCenterInsightRoot,
                path(JiraDialect.DataCenterInsightRoot)), null, ct), JiraDialect.DataCenterInsightRoot);
    }

    private const string DataCenterNotFoundHint =
        " — neither /rest/assets/1.0 nor /rest/insight/1.0 answered on this instance. Check that the "
        + "base URL is the instance root (with any context path) and that Assets is installed.";

    /// <summary>A Cloud call needs the workspace; reaching here without one is a caller bug, said plainly.</summary>
    private static string RequireWorkspace(string? workspaceId) =>
        string.IsNullOrWhiteSpace(workspaceId)
            ? throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                "A Jira Cloud Assets call needs the Assets workspace id, and none was resolved.")
            : workspaceId;

    /// <summary>
    /// The items of a response that is a bare array or an object holding one under the first of
    /// <paramref name="properties"/> present. Empty rather than null, so callers need no branch.
    /// </summary>
    private static IEnumerable<JsonElement> Items(JsonDocument? document, params string[] properties)
    {
        if (document == null) return [];

        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToList();

        if (root.ValueKind != JsonValueKind.Object) return [];

        foreach (var property in properties)
            if (root.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array)
                return items.EnumerateArray().ToList();

        return [];
    }
}
