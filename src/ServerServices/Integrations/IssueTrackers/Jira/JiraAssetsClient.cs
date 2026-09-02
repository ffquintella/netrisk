using System.Text.Json;
using DAL.Entities;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Jira Assets (the Service Management CMDB, formerly Insight) — Cloud only
/// (Track 4 milestone 4.6).
///
/// Assets is not served from the Jira site. It lives at
/// <c>api.atlassian.com/jsm/assets/workspace/{workspaceId}/v1</c>, behind a workspace id that is
/// neither the site host nor the Jira cloud id and has to be discovered from the site first
/// (<see cref="IJiraServiceManagementClient.GetAssetsWorkspaceIdAsync"/>). That is the single fact
/// most likely to be got wrong here, so every URL in this class is built by
/// <see cref="JiraHttp.AssetsUrl"/> and none of them by string concatenation on the base URL.
///
/// The calls still go through <see cref="IOutboundHttpClient"/>, so <c>OutboundUrlPolicy</c>
/// evaluates the new host like any other; the policy is a deny-list (cloud metadata always, private
/// ranges optionally), so <c>api.atlassian.com</c> needs no configuration to be reachable.
///
/// Data Center is deliberately unimplemented: its equivalent is the Insight API at
/// <c>/rest/insight/1.0/</c> on the site, with a different object model. A Data Center connection is
/// refused where it is configured rather than half-served from here.
/// </summary>
public class JiraAssetsClient(ILogger logger, IOutboundHttpClient http) : IJiraAssetsClient
{
    /// <summary>Assets' AQL page size. Larger pages are accepted and then clamped by Assets.</summary>
    internal const int PageSize = 100;

    public async Task<List<JiraObjectSchemaView>> GetSchemasAsync(IssueTrackerConnection connection,
        string? token, string workspaceId, CancellationToken ct = default)
    {
        var url = JiraHttp.AssetsUrl(workspaceId, "/objectschema/list?maxResults=200&includeCounts=true");

        using var document = await GetJsonAsync(connection, token, url, "the Assets schema list", ct);

        var schemas = new List<JiraObjectSchemaView>();

        if (document == null
            || !document.RootElement.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array) return schemas;

        foreach (var schema in values.EnumerateArray())
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
        string? token, string workspaceId, int schemaId, CancellationToken ct = default)
    {
        // The flat variant, not the hierarchical one: the mapping editor needs a pickable list, and a
        // nested tree would have to be flattened for the picker anyway. It also lives in the
        // *objectschema* group — there is no object-type listing under /objecttype, which is the
        // wrong guess to make here.
        var url = JiraHttp.AssetsUrl(workspaceId, $"/objectschema/{schemaId}/objecttypes/flat");

        using var document = await GetJsonAsync(connection, token, url,
            $"the object types of schema {schemaId}", ct);

        var types = new List<JiraObjectTypeView>();

        if (document == null) return types;

        // This endpoint answers with a bare array rather than a paginated envelope. Both are handled,
        // because getting it wrong shows up as "no object types" and reads like a permissions problem.
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("values", out var values) ? values : default;

        if (items.ValueKind != JsonValueKind.Array) return types;

        foreach (var type in items.EnumerateArray())
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
        IssueTrackerConnection connection, string? token, string workspaceId, int objectTypeId,
        CancellationToken ct = default)
    {
        var url = JiraHttp.AssetsUrl(workspaceId, $"/objecttype/{objectTypeId}/attributes");

        using var document = await GetJsonAsync(connection, token, url,
            $"the attributes of object type {objectTypeId}", ct);

        var attributes = new List<JiraObjectTypeAttributeView>();

        if (document == null) return attributes;

        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("values", out var values) ? values : default;

        if (items.ValueKind != JsonValueKind.Array) return attributes;

        foreach (var attribute in items.EnumerateArray())
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
        string workspaceId, string aql, int startAt, int maxResults, CancellationToken ct = default)
    {
        var url = JiraHttp.AssetsUrl(workspaceId,
            $"/object/aql?startAt={startAt}&maxResults={Math.Clamp(maxResults, 1, PageSize)}"
            + "&includeAttributes=true");

        // The query travels as JSON in the body, not in the URL. It is the customer's AQL over their
        // own schema, sent to Jira and never interpolated into SQL; serialising it through a JSON
        // writer rather than a format string is what keeps a quote in an object name from producing
        // an unparseable request.
        var body = JsonSerializer.Serialize(new { qlQuery = aql });

        var response = await JiraHttp.SendAsync(http, connection, token, "POST", url, body, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                $"Assets refused the query (HTTP {response.StatusCode}): {JiraHttp.Excerpt(response.Body)}"
                + (response.StatusCode == 400
                    ? $" — check the AQL: {aql}"
                    : string.Empty));

        using var document = JiraHttp.TryParse(response.Body);

        if (document == null)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                "The Assets search response was not JSON.");

        var page = new AssetSearchPage
        {
            Total = JiraHttp.Int(document.RootElement, "total"),
            IsLast = true
        };

        if (!document.RootElement.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array) return page;

        foreach (var value in values.EnumerateArray())
            page.Objects.Add(ParseObject(value));

        // isLast when Assets says so; otherwise inferred from a short page. Assets has changed which
        // of the two it sends between versions of this endpoint, so neither is trusted alone.
        page.IsLast = document.RootElement.TryGetProperty("isLast", out var isLast)
                      && isLast.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? isLast.ValueKind == JsonValueKind.True
            : page.Objects.Count < maxResults;

        return page;
    }

    public async Task<ConnectionTestResult> TestAsync(IssueTrackerConnection connection, string? token,
        string workspaceId, CancellationToken ct = default)
    {
        // The schema list rather than a lighter probe, because it proves the three things that can
        // each be independently wrong: the credential is accepted by api.atlassian.com, the workspace
        // id resolves, and the site's plan actually includes Assets.
        var url = JiraHttp.AssetsUrl(workspaceId, "/objectschema/list?maxResults=1");

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
            new Dictionary<string, string> { ["Assets workspace"] = workspaceId });
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
        string url, string what, CancellationToken ct)
    {
        var response = await JiraHttp.SendAsync(http, connection, token, "GET", url, null, ct);

        if (!response.IsSuccess)
            throw new Model.Exceptions.IntegrationRequestException("Jira Assets",
                $"Could not read {what} (HTTP {response.StatusCode}): {JiraHttp.Excerpt(response.Body)}");

        var document = JiraHttp.TryParse(response.Body);

        if (document == null)
            logger.Warning("The Assets response for {What} was not JSON", what);

        return document;
    }
}
