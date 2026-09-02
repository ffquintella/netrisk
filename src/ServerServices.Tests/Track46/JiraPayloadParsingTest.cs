using System.Linq;
using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations.IssueTrackers.Jira;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// Parsing the two payload shapes this milestone reads (Track 4 milestone 4.6), against fixtures in
/// the form Atlassian actually sends.
///
/// The payloads are the part of an integration nobody can check by reading the code: a field that is
/// a number on one site and a string on another, a duration wrapped in an object, a value whose useful
/// form is <c>displayValue</c> and not <c>value</c>. Each assertion below is one of those.
/// </summary>
[TestSubject(typeof(JiraServiceManagementClient))]
public class JiraPayloadParsingTest
{
    private static readonly IssueTrackerConnection Connection = new()
    {
        Id = 1, Name = "Service desk", Provider = IssueTrackerProviderKind.Jira,
        BaseUrl = "https://acme.atlassian.net", ProjectKey = "SD", AuthUser = "bot@acme.com"
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // --- Service Management -----------------------------------------------------------------

    private const string RequestJson = """
        {
          "issueId": "10501",
          "issueKey": "SD-4711",
          "requestTypeId": "77",
          "requestType": { "id": "77", "name": "Report a system problem", "serviceDeskId": "3" },
          "serviceDesk": { "id": "3", "projectName": "Service desk" },
          "reporter": { "accountId": "5b10a", "displayName": "Alice Silva" },
          "requestFieldValues": [
            { "fieldId": "summary", "label": "Summary", "value": "Payment gateway is timing out" },
            { "fieldId": "description", "label": "Description", "value": "Since 09:00." }
          ],
          "currentStatus": { "status": "Waiting for support", "statusCategory": "IN_PROGRESS" },
          "createdDate": { "iso8601": "2026-08-25T09:12:00+00:00", "epochMillis": 1787648", "friendly": "Today" },
          "_links": { "web": "https://acme.atlassian.net/servicedesk/customer/portal/3/SD-4711" }
        }
        """;

    /// <summary>
    /// A request as the portal API reports it.
    ///
    /// Three things here are easy to get wrong and all three are asserted: the summary is not a
    /// property but an entry in <c>requestFieldValues</c> keyed by field id; the status category uses
    /// the portal's <c>IN_PROGRESS</c> vocabulary rather than the platform's lower-case keys; and the
    /// ids arrive as strings even where they are numbers.
    /// </summary>
    [Fact]
    public void ARequestParsesItsSummaryStatusAndReporter()
    {
        // The malformed epochMillis in the fixture is deliberate: the ISO form is preferred, so a
        // broken sibling must not take the parse down with it.
        var json = RequestJson.Replace("\"epochMillis\": 1787648\", ", "");

        var request = JiraServiceManagementClient.ParseRequest(Connection, Parse(json));

        Assert.Equal("SD-4711", request.IssueKey);
        Assert.Equal("10501", request.IssueId);
        Assert.Equal(3, request.ServiceDeskId);
        Assert.Equal("77", request.RequestTypeId);
        Assert.Equal("Report a system problem", request.RequestTypeName);
        Assert.Equal("Payment gateway is timing out", request.Summary);
        Assert.Equal("Waiting for support", request.StatusName);
        Assert.Equal("Alice Silva", request.ReporterDisplayName);
        Assert.Equal("5b10a", request.ReporterAccountId);
        Assert.Contains("SD-4711", request.RequestUrl);
    }

    /// <summary>
    /// The status category is normalised to one vocabulary.
    ///
    /// The Service Desk API says <c>IN_PROGRESS</c>/<c>DONE</c> where the platform API says
    /// <c>indeterminate</c>/<c>done</c>. Normalising here is what lets the closed test be one
    /// comparison and keeps two spellings out of the mirror.
    /// </summary>
    [Theory]
    [InlineData("DONE", "done", true)]
    [InlineData("IN_PROGRESS", "in-progress", false)]
    [InlineData("NEW", "new", false)]
    public void TheStatusCategoryIsNormalisedAndDecidesClosed(string reported, string expected,
        bool closed)
    {
        var json = RequestJson
            .Replace("\"epochMillis\": 1787648\", ", "")
            .Replace("\"statusCategory\": \"IN_PROGRESS\"", $"\"statusCategory\": \"{reported}\"");

        var request = JiraServiceManagementClient.ParseRequest(Connection, Parse(json));

        Assert.Equal(expected, request.StatusCategory);
        Assert.Equal(closed, request.IsClosed);
    }

    private const string SlaJson = """
        {
          "id": "1",
          "name": "Time to first response",
          "completedCycles": [
            {
              "startTime": { "iso8601": "2026-08-20T09:00:00+00:00" },
              "stopTime":  { "iso8601": "2026-08-20T11:30:00+00:00" },
              "breached": true,
              "goalDuration":  { "millis": 3600000,  "friendly": "1h" },
              "elapsedTime":   { "millis": 9000000,  "friendly": "2h 30m" },
              "remainingTime": { "millis": -5400000, "friendly": "-1h 30m" }
            }
          ],
          "ongoingCycle": {
            "startTime": { "iso8601": "2026-08-25T09:12:00+00:00" },
            "breached": false,
            "paused": true,
            "goalDuration":  { "millis": 14400000 },
            "elapsedTime":   { "millis": 1200000 },
            "remainingTime": { "millis": 13200000 }
          }
        }
        """;

    /// <summary>
    /// A metric's completed and ongoing cycles both become rows.
    ///
    /// This is the reason the mirror keys SLA on the cycle start and not on the metric: a reopened
    /// request has a completed cycle that breached *and* a clean ongoing one, and keying on the metric
    /// alone would overwrite the breach with the clean state.
    /// </summary>
    [Fact]
    public void EveryCycleOfAMetricBecomesItsOwnRow()
    {
        var cycles = JiraServiceManagementClient.ParseSlaMetric(Parse(SlaJson));

        Assert.Equal(2, cycles.Count);

        var completed = cycles.Single(c => !c.IsOngoing);
        Assert.True(completed.Breached);
        Assert.Equal("Time to first response", completed.MetricName);
        Assert.Equal(3_600_000, completed.GoalDurationMs);
        // Negative once the goal is passed, which is how Jira reports it — kept verbatim rather than
        // clamped, because the sign is the information.
        Assert.Equal(-5_400_000, completed.RemainingMs);
        Assert.NotNull(completed.CycleStopAt);

        var ongoing = cycles.Single(c => c.IsOngoing);
        Assert.False(ongoing.Breached);
        Assert.True(ongoing.Paused);
        Assert.Equal(13_200_000, ongoing.RemainingMs);
        Assert.Null(ongoing.CycleStopAt);
    }

    /// <summary>
    /// The friendly duration is discarded on purpose: it is localised to the instance's own locale, so
    /// storing it would put Portuguese in one customer's mirror and English in another's for the same
    /// number of milliseconds.
    /// </summary>
    [Fact]
    public void ADurationIsReadFromMillisAndNotFromTheFriendlyText()
    {
        var cycles = JiraServiceManagementClient.ParseSlaMetric(Parse(SlaJson));

        Assert.All(cycles, c => Assert.NotNull(c.GoalDurationMs));
    }

    [Fact]
    public void AMetricWithNoCyclesYieldsNothingRatherThanAnEmptyRow()
    {
        var cycles = JiraServiceManagementClient.ParseSlaMetric(
            Parse("""{ "id": "2", "name": "Time to resolution" }"""));

        Assert.Empty(cycles);
    }

    // --- Assets -----------------------------------------------------------------------------

    private const string ObjectJson = """
        {
          "id": "1042",
          "objectKey": "ITSM-88",
          "label": "srv-prod-01",
          "created": "2026-01-04T10:00:00.000Z",
          "updated": "2026-08-20T14:31:00.000Z",
          "objectType": { "id": 23, "name": "Server" },
          "attributes": [
            {
              "id": 900, "objectTypeAttributeId": 231,
              "objectTypeAttribute": { "id": 231, "name": "Hostname" },
              "objectAttributeValues": [ { "value": "srv-prod-01", "displayValue": "srv-prod-01" } ]
            },
            {
              "id": 901, "objectTypeAttributeId": 232,
              "objectAttributeValues": [
                { "value": "4711", "displayValue": "", "referencedObject": { "id": 4711, "label": "Platform Team" } }
              ]
            },
            {
              "id": 902, "objectTypeAttributeId": 233,
              "objectAttributeValues": [ { "user": { "displayName": "Alice Silva", "emailAddress": "alice@acme.com" } } ]
            },
            {
              "id": 903, "objectTypeAttributeId": 234,
              "objectAttributeValues": []
            }
          ]
        }
        """;

    [Fact]
    public void AnAssetObjectParsesItsKeyTypeAndTimestamps()
    {
        var payload = JiraAssetsClient.ParseObject(Parse(ObjectJson));

        Assert.Equal("1042", payload.ObjectId);
        Assert.Equal("ITSM-88", payload.ObjectKey);
        Assert.Equal("srv-prod-01", payload.Label);
        Assert.Equal(23, payload.ObjectTypeId);
        Assert.Equal("Server", payload.ObjectTypeName);
        Assert.NotNull(payload.CreatedAt);
        Assert.NotNull(payload.UpdatedAt);
        Assert.False(string.IsNullOrWhiteSpace(payload.RawJson));
    }

    /// <summary>
    /// Values are keyed by <c>objectTypeAttributeId</c>, and only the attributes whose name the
    /// payload inlined are also reachable by name.
    ///
    /// That asymmetry is the reason the importer loads the object type's attributes separately: reading
    /// names out of this payload alone works on a site that inlines them and silently maps nothing on
    /// one that does not.
    /// </summary>
    [Fact]
    public void ValuesAreKeyedByAttributeIdAndByNameOnlyWhenThePayloadCarriesIt()
    {
        var payload = JiraAssetsClient.ParseObject(Parse(ObjectJson));

        Assert.Equal(["srv-prod-01"], payload.Attributes[231]);
        Assert.True(payload.AttributesByName.ContainsKey("Hostname"));
        Assert.False(payload.AttributesByName.ContainsKey("Owner"));
    }

    /// <summary>
    /// A reference attribute's useful value is the referenced object's label, not its internal id, and
    /// a user attribute's is the display name. Without this, "who owns this server" reads as
    /// <c>4711</c>.
    /// </summary>
    [Fact]
    public void AReferenceOrUserValueResolvesToSomethingAPersonCanRead()
    {
        var payload = JiraAssetsClient.ParseObject(Parse(ObjectJson));

        Assert.Equal(["Platform Team"], payload.Attributes[232]);
        Assert.Equal(["Alice Silva"], payload.Attributes[233]);
    }

    [Fact]
    public void AnAttributeWithNoValuesIsAbsentRatherThanEmpty()
    {
        var payload = JiraAssetsClient.ParseObject(Parse(ObjectJson));

        // Absent, so the projector's constant fallback applies. An empty list would read as "the
        // register says empty", which is a different statement.
        Assert.False(payload.Attributes.ContainsKey(234));
    }

    // --- AQL and host identity ---------------------------------------------------------------

    /// <summary>
    /// An object type may legitimately be called <c>Server "Legacy"</c>, and an unescaped name produces
    /// an AQL syntax error that reads as "Assets refused the query" with no clue which type caused it.
    /// </summary>
    [Fact]
    public void TheObjectTypeNameIsQuotedAndItsOwnQuotesDoubled()
    {
        var aql = JiraIntegrationService.BuildAql(new JiraObjectMapping
        {
            ObjectTypeName = "Server \"Legacy\""
        });

        Assert.Equal("objectType = \"Server \"\"Legacy\"\"\"", aql);
    }

    [Fact]
    public void TheOperatorsFilterIsAndedInsideParentheses()
    {
        var aql = JiraIntegrationService.BuildAql(new JiraObjectMapping
        {
            ObjectTypeName = "Server",
            AqlFilter = "Status = \"In Production\" OR Status = \"Staging\""
        });

        // Parenthesised, or the OR would bind loosely and the type filter would stop applying to the
        // second branch — an import that quietly returns the whole schema.
        Assert.Equal(
            "objectType = \"Server\" AND (Status = \"In Production\" OR Status = \"Staging\")", aql);
    }

    /// <summary>
    /// MAC comparison has to be format-insensitive, because the stored values arrive from several
    /// importers with different separators and a raw comparison matches only the ones that happen to
    /// agree with this CMDB's formatting.
    /// </summary>
    [Theory]
    [InlineData("AA-BB-CC-DD-EE-FF", "aa:bb:cc:dd:ee:ff")]
    [InlineData("aabb.ccdd.eeff", "AA:BB:CC:DD:EE:FF")]
    public void MacAddressesCompareAcrossFormats(string left, string right)
    {
        Assert.Equal(JiraIntegrationService.NormaliseMac(left),
            JiraIntegrationService.NormaliseMac(right));
    }

    [Fact]
    public void DifferentMacsStillDiffer()
    {
        Assert.NotEqual(JiraIntegrationService.NormaliseMac("AA:BB:CC:DD:EE:FF"),
            JiraIntegrationService.NormaliseMac("AA:BB:CC:DD:EE:00"));
    }

    [Theory]
    [InlineData("SD-4711", "SD-4711")]
    [InlineData("https://acme.atlassian.net/browse/SD-4711", "SD-4711")]
    [InlineData("https://acme.atlassian.net/browse/SD-4711?filter=10", "SD-4711")]
    [InlineData("https://acme.atlassian.net/browse/SD-4711/", "SD-4711")]
    public void AnIssueKeyIsTakenFromAKeyOrAUrl(string input, string expected)
    {
        Assert.Equal(expected, JiraIntegrationService.ExtractKey(input));
    }
}
