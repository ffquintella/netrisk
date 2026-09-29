using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using NSubstitute;
using Serilog;
using ServerServices.Integrations.IssueTrackers.Jira;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// The Data Center half of the Assets client, against payloads shaped as the Assets REST reference
/// documents them (docs.atlassian.com/assets/REST/10.7.0 and 9.1.16, JSM DC 10 OpenAPI spec).
/// </summary>
[TestSubject(typeof(JiraAssetsClient))]
public class JiraAssetsDataCenterClientTest
{
    private readonly FakeOutboundHttpClient _http = new();
    private readonly JiraAssetsClient _client;

    private static readonly IssueTrackerConnection Pat = new()
    {
        Id = 7, Name = "Jira-hml", Provider = IssueTrackerProviderKind.JiraDataCenter,
        BaseUrl = "https://hml-jira.acme.br/", ProjectKey = "SDESI", AuthUser = null
    };

    public JiraAssetsDataCenterClientTest()
    {
        _client = new JiraAssetsClient(Substitute.For<ILogger>(), _http);
    }

    // Documented shape: ObjectSchemaListEntry {"objectschemas":[ObjectSchemaEntry]}.
    private const string SchemaListJson = """
        {
          "objectschemas": [
            { "id": 1, "name": "IT Assets", "objectSchemaKey": "ITSM", "status": "Ok",
              "created": "2019-11-26T08:06:02.891Z", "updated": "2019-11-26T08:06:02.891Z",
              "objectCount": 95, "objectTypeCount": 34 },
            { "id": 2, "name": "HR", "objectSchemaKey": "HR", "status": "Ok",
              "objectCount": 0, "objectTypeCount": 3 }
          ]
        }
        """;

    // The documented example of GET objectschema/{id}/objecttypes/flat, icons removed.
    private const string FlatTypesJson = """
        [
          { "id": 2, "name": "Computer", "type": 0, "position": 0,
            "created": "2019-11-27T11:13:22.889Z", "updated": "2019-11-27T11:13:22.889Z",
            "objectCount": 0, "parentObjectTypeId": 1, "objectSchemaId": 1, "inherited": false,
            "abstractObjectType": false, "parentObjectTypeInherited": false },
          { "id": 1, "name": "ObjectType", "type": 0, "position": 0,
            "created": "2019-11-26T08:06:02.891Z", "updated": "2019-11-26T08:06:02.891Z",
            "objectCount": 0, "objectSchemaId": 1, "inherited": false, "abstractObjectType": false,
            "parentObjectTypeInherited": false }
        ]
        """;

    // The documented example of GET objecttype/{id}/attributes, trimmed, plus a reference attribute.
    private const string AttributesJson = """
        [
          { "id": 1, "name": "Key", "label": false, "type": 0,
            "defaultType": { "id": 0, "name": "Text" }, "editable": false, "system": true,
            "minimumCardinality": 1, "maximumCardinality": 1, "position": 0 },
          { "id": 2, "name": "Name", "label": true, "type": 0, "description": "The name of the object",
            "defaultType": { "id": 0, "name": "Text" }, "editable": true, "system": false,
            "minimumCardinality": 1, "maximumCardinality": 1, "position": 1 },
          { "id": 7, "name": "Owner", "label": false, "type": 1, "referenceObjectTypeId": 9,
            "position": 2 }
        ]
        """;

    // The documented ObjectListResultEntry of GET aql/objects: one entry, avatar removed.
    private const string SearchJson = """
        {
          "objectEntries": [
            {
              "id": 1, "label": "Example Object", "objectKey": "TEST-1",
              "objectType": { "id": 1, "name": "ObjectType", "type": 0, "objectSchemaId": 1 },
              "created": "2019-11-26T08:06:45.478Z", "updated": "2019-11-26T08:07:08.063Z",
              "hasAvatar": false, "timestamp": 1574755628063,
              "attributes": [
                { "id": 6, "objectTypeAttributeId": 2, "objectId": 1,
                  "objectAttributeValues": [ { "value": "Example Object", "searchValue": "Example Object",
                    "displayValue": "Example Object", "referencedType": false } ] },
                { "id": 9, "objectTypeAttributeId": 7, "objectId": 1,
                  "objectAttributeValues": [ { "referencedObject": { "id": 44, "label": "Platform Team" },
                    "displayValue": "Platform Team", "referencedType": true } ] }
              ],
              "_links": { "self": "http://jira/secure/ShowObject.jspa?id=1" },
              "name": "Example Object"
            }
          ],
          "objectTypeId": 0, "totalFilterCount": 201, "startIndex": 1, "toIndex": 2,
          "pageObjectSize": 100, "pageNumber": 1, "orderWay": "ascending", "qlQuery": "",
          "qlQuerySearchResult": true, "pageSize": 3
        }
        """;

    [Fact]
    public async Task TheSchemaListIsReadFromTheInstanceUnderTheAssetsRootWithABearerPat()
    {
        _http.RuleFor("/rest/assets/1.0/objectschema/list", SchemaListJson);

        var schemas = await _client.GetSchemasAsync(Pat, "pat", null);

        var request = Assert.Single(_http.Requests);
        // The instance's own host, no workspace, and none of Cloud's paging parameters.
        Assert.Equal("https://hml-jira.acme.br/rest/assets/1.0/objectschema/list", request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Equal("Bearer pat", request.Headers["Authorization"]);

        Assert.Equal(2, schemas.Count);
        Assert.Equal("IT Assets", schemas[0].Name);
        Assert.Equal("ITSM", schemas[0].ObjectSchemaKey);
        Assert.Equal(95, schemas[0].ObjectCount);
    }

    [Fact]
    public async Task AUsernameAndPasswordConnectionSendsBasicAuth()
    {
        var basic = new IssueTrackerConnection
        {
            Id = 8, Name = "Jira-hml basic", Provider = IssueTrackerProviderKind.JiraDataCenter,
            BaseUrl = "https://hml-jira.acme.br", ProjectKey = "SDESI", AuthUser = "svc-netrisk"
        };
        _http.RuleFor("/objectschema/list", SchemaListJson);

        await _client.GetSchemasAsync(basic, "secret", null);

        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("svc-netrisk:secret")),
            Assert.Single(_http.Requests).Headers["Authorization"]);
    }

    /// <summary>Insight (Assets 9.x, JSM 4) has no /rest/assets; a 404 there, and only a 404, is retried.</summary>
    [Fact]
    public async Task AnInstanceWithoutTheAssetsRootFallsBackToInsight()
    {
        _http.RuleFor("/rest/assets/1.0/", "", 404);
        _http.RuleFor("/rest/insight/1.0/objectschema/list", SchemaListJson);

        var schemas = await _client.GetSchemasAsync(Pat, "pat", null);

        Assert.Equal(2, schemas.Count);
        Assert.Equal(
        [
            "https://hml-jira.acme.br/rest/assets/1.0/objectschema/list",
            "https://hml-jira.acme.br/rest/insight/1.0/objectschema/list"
        ], _http.Requests.Select(r => r.Url));
    }

    /// <summary>A refused credential is the answer; retrying it on a second path only feeds Seraph's CAPTCHA.</summary>
    [Fact]
    public async Task ARefusedCredentialIsNotRetriedUnderInsight()
    {
        _http.RuleFor("/rest/assets/1.0/", "{\"message\":\"no\"}", 401);

        var ex = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => _client.GetSchemasAsync(Pat, "pat", null));

        Assert.Contains("401", ex.Message);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task WhenNeitherRootAnswersTheErrorSaysWhereToLook()
    {
        _http.DefaultResponse = new OutboundHttpResponse { StatusCode = 404, Body = "" };

        var ex = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => _client.GetSchemasAsync(Pat, "pat", null));

        Assert.Contains("/rest/insight/1.0", ex.Message);
        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task TheFlatObjectTypeListParsesTheDocumentedArray()
    {
        _http.RuleFor("/rest/assets/1.0/objectschema/1/objecttypes/flat", FlatTypesJson);

        var types = await _client.GetObjectTypesAsync(Pat, "pat", null, 1);

        Assert.Equal("https://hml-jira.acme.br/rest/assets/1.0/objectschema/1/objecttypes/flat",
            Assert.Single(_http.Requests).Url);
        Assert.Equal(2, types.Count);
        Assert.Equal("Computer", types[0].Name);
        Assert.Equal(1, types[0].ParentObjectTypeId);
        Assert.Null(types[1].ParentObjectTypeId);
    }

    [Fact]
    public async Task AttributesCarryTheirLabelFlagAndATypeAnOperatorCanRead()
    {
        _http.RuleFor("/rest/assets/1.0/objecttype/1/attributes", AttributesJson);

        var attributes = await _client.GetAttributesAsync(Pat, "pat", null, 1);

        Assert.Equal("https://hml-jira.acme.br/rest/assets/1.0/objecttype/1/attributes",
            Assert.Single(_http.Requests).Url);
        Assert.Equal(3, attributes.Count);
        Assert.True(attributes.Single(a => a.Name == "Name").IsLabel);
        Assert.Equal("Text", attributes.Single(a => a.Name == "Key").Type);
        Assert.Equal("Object reference", attributes.Single(a => a.Name == "Owner").Type);
    }

    /// <summary>A GET with the AQL escaped into qlQuery and a 1-based page, not Cloud's offset POST.</summary>
    [Fact]
    public async Task TheSearchIsAPagedGetWhoseEntriesAndTotalAreRead()
    {
        _http.RuleFor("/rest/assets/1.0/aql/objects", SearchJson);
        const string aql = "objectType = \"Server\" AND Name = \"a&b\"";

        var page = await _client.SearchAsync(Pat, "pat", null, aql, startAt: 100, maxResults: 100);

        var request = Assert.Single(_http.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Body);
        Assert.Equal(
            "https://hml-jira.acme.br/rest/assets/1.0/aql/objects?qlQuery=" + Uri.EscapeDataString(aql)
            + "&page=2&resultPerPage=100&includeAttributes=true", request.Url);

        Assert.Equal(201, page.Total);
        var entry = Assert.Single(page.Objects);
        Assert.Equal("1", entry.ObjectId);
        Assert.Equal("TEST-1", entry.ObjectKey);
        Assert.Equal("ObjectType", entry.ObjectTypeName);
        Assert.Equal(["Example Object"], entry.Attributes[2]);
        Assert.Equal(["Platform Team"], entry.Attributes[7]);
        Assert.NotNull(entry.CreatedAt);
        // 100 + 1 of 201: the total says more pages follow, although this one was short.
        Assert.False(page.IsLast);
    }

    [Fact]
    public async Task TheLastPageIsTheOneThatReachesTheTotal()
    {
        _http.RuleFor("/aql/objects",
            SearchJson.Replace("\"totalFilterCount\": 201", "\"totalFilterCount\": 101"));

        var page = await _client.SearchAsync(Pat, "pat", null, "objectType = \"Server\"", 100, 100);

        Assert.True(page.IsLast);
    }

    [Fact]
    public async Task InsightIsSearchedThroughIqlObjects()
    {
        _http.RuleFor("/rest/assets/1.0/", "", 404);
        _http.RuleFor("/rest/insight/1.0/iql/objects", SearchJson);

        var page = await _client.SearchAsync(Pat, "pat", null, "objectType = \"Server\"", 0, 100);

        Assert.Single(page.Objects);
        Assert.Equal(
            "https://hml-jira.acme.br/rest/insight/1.0/iql/objects?iql="
            + Uri.EscapeDataString("objectType = \"Server\"")
            + "&page=1&resultPerPage=100&includeAttributes=true", _http.Requests[^1].Url);
    }

    [Fact]
    public async Task TheConnectionTestNamesTheRootThatAnswered()
    {
        _http.RuleFor("/rest/assets/1.0/objectschema/list", SchemaListJson);

        var result = await _client.TestAsync(Pat, "pat", null);

        Assert.True(result.Success);
        Assert.Contains("2 object schema(s)", result.Message);
        Assert.Equal("/rest/assets/1.0", result.Details["Assets API"]);
    }

    /// <summary>Cloud is unchanged: api.atlassian.com, keyed by the workspace, and it needs one.</summary>
    [Fact]
    public async Task ACloudConnectionStillTargetsTheWorkspaceAndRefusesToGoWithoutOne()
    {
        var cloud = new IssueTrackerConnection
        {
            Id = 1, Name = "Cloud", Provider = IssueTrackerProviderKind.Jira,
            BaseUrl = "https://acme.atlassian.net", ProjectKey = "SD", AuthUser = "bot@acme.com"
        };
        _http.RuleFor("/objectschema/list", """{ "values": [ { "id": 5, "name": "IT" } ] }""");

        var schemas = await _client.GetSchemasAsync(cloud, "api-token", "ws-1");

        Assert.Equal("IT", Assert.Single(schemas).Name);
        Assert.StartsWith("https://api.atlassian.com/jsm/assets/workspace/ws-1/v1/objectschema/list",
            _http.Requests[^1].Url);

        await Assert.ThrowsAsync<IntegrationRequestException>(
            () => _client.GetSchemasAsync(cloud, "api-token", null));
    }
}
