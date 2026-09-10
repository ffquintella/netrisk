using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Parsing of Vision One's ASRM payloads (Track 4 milestone 4.4).
///
/// The parsing is the fragile part of this integration: Vision One has used more than one spelling for
/// several attributes between preview and GA, expresses asset criticality as both a word and a number
/// on two different scales, nests vulnerabilities under the device in more than one shape, and pages
/// with an opaque <c>nextLink</c>. Each of those is asserted here against a captured payload rather
/// than discovered against a customer's tenant.
/// </summary>
[TestSubject(typeof(TrendMicroClient))]
public class TrendMicroClientTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static TrendMicroConnection Connection() => new()
    {
        Id = 1, Name = "acme", Region = "eu", BaseUrl = "https://api.eu.xdr.trendmicro.com", Enabled = true
    };

    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // --- device parsing ---------------------------------------------------------------------

    [Fact]
    public void ADeviceIsParsedFromTheGaFieldNames()
    {
        var device = TrendMicroClient.ParseDevice(Element("""
            {
              "id": "agent-1",
              "name": "db-prod-01",
              "fqdn": "db-prod-01.acme.local",
              "ip": ["10.0.0.5", "fe80::1"],
              "mac": ["00:11:22:33:44:55"],
              "osName": "Windows Server 2022",
              "osVersion": "10.0.20348",
              "riskScore": 87,
              "riskLevel": "high",
              "assetCriticality": "critical"
            }
            """));

        Assert.NotNull(device);
        Assert.Equal("agent-1", device!.Id);
        Assert.Equal("db-prod-01", device.Name);
        // The first IPv4, not just the first address — an IPv6 primary would not match anything in the
        // existing inventory.
        Assert.Equal("10.0.0.5", device.PrimaryIp);
        Assert.Equal("00:11:22:33:44:55", device.PrimaryMac);
        Assert.Equal(87, device.RiskScore);
        Assert.Equal(5, device.Criticality);
    }

    [Fact]
    public void ADeviceIsAlsoParsedFromTheOlderPreviewNames()
    {
        var device = TrendMicroClient.ParseDevice(Element("""
            {
              "agentGuid": "agent-2",
              "endpointName": "web-01",
              "ipAddresses": ["10.0.0.9"],
              "macAddresses": ["aa:bb:cc:dd:ee:ff"],
              "operatingSystem": "Ubuntu 22.04",
              "assetRiskScore": "63"
            }
            """));

        Assert.NotNull(device);
        Assert.Equal("agent-2", device!.Id);
        Assert.Equal("web-01", device.Name);
        Assert.Equal("10.0.0.9", device.PrimaryIp);
        // A number arriving as a string is common enough in this API to be worth handling.
        Assert.Equal(63, device.RiskScore);
    }

    [Fact]
    public void ADeviceWithNoIdIsDroppedRatherThanCreatingAHostTheNextSyncDuplicates()
    {
        Assert.Null(TrendMicroClient.ParseDevice(Element("""{"name":"nameless"}""")));
    }

    [Fact]
    public void PropertyLookupIsCaseInsensitive()
    {
        var device = TrendMicroClient.ParseDevice(Element("""{"Id":"agent-3","Name":"x"}"""));

        Assert.Equal("agent-3", device!.Id);
    }

    [Theory]
    [InlineData("""{"id":"a","assetCriticality":"critical"}""", 5)]
    [InlineData("""{"id":"a","assetCriticality":"high"}""", 4)]
    [InlineData("""{"id":"a","assetCriticality":"low"}""", 2)]
    [InlineData("""{"id":"a","assetCriticality":3}""", 3)]
    [InlineData("""{"id":"a","assetCriticality":80}""", 4)]
    [InlineData("""{"id":"a","assetCriticality":100}""", 5)]
    public void CriticalityIsNormalisedToOneToFiveFromEveryShapeVisionOneUses(string json, int expected)
    {
        // A 0–100 value is banded rather than truncated: truncating 80 and 20 alike to 5 would flatten
        // the distinction the customer configured.
        Assert.Equal(expected, TrendMicroClient.ParseDevice(Element(json))!.Criticality);
    }

    // --- vulnerability parsing --------------------------------------------------------------

    /// <summary>
    /// The shape <c>/v3.0/asrm/vulnerableDevices</c> actually returns, field for field from Vision
    /// One's OpenAPI specification.
    ///
    /// Nothing here matched before this fix. The array is <c>cveRecords</c>, not <c>vulnerabilities</c>;
    /// the CVE id is <c>id</c>; the severity is <c>eventRiskLevel</c>; the rule ids are nested under
    /// <c>protectionRules</c>. 2.19.6 read this data off <c>attackSurfaceDevices</c>, which carries a
    /// <c>cveCount</c> and no CVEs at all, so a tenant with 16,000 devices imported zero findings.
    /// </summary>
    [Fact]
    public void TheRealVulnerableDevicesPayloadBecomesOneFindingPerCve()
    {
        var findings = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {
              "id": "9c94bd33-c589-48c4-9431-dace397b0067",
              "deviceName": "SASE-PC1",
              "criticality": "high",
              "ip": ["10.0.0.5"],
              "lastScannedDateTime": "2026-09-10T06:08:33Z",
              "cveRecords": [
                {"id":"CVE-2019-0808","eventRiskLevel":"high","cvssScore":7.8,
                 "globalExploitActivityLevel":"high","mitigationStatus":"new",
                 "exploitAttemptCount":12,
                 "affectedComponentDetails":[{"name":"7-Zip","filePath":"C:\\Program Files\\7-Zip\\"}],
                 "protectionRules":[{"id":"34777","product":"TP","name":"HTTP: Win32k EoP"}],
                 "publishedDateTime":"2026-05-21T06:08:33Z"},
                {"id":"CVE-2026-2222","eventRiskLevel":"medium","cvssScore":5.4,
                 "mitigationStatus":"inProgress"}
              ]
            }
            """));

        Assert.Equal(2, findings.Count);

        // One finding per device listing thirty CVEs cannot be triaged or given an SLA.
        var exploited = findings.Single(f => f.CveId == "CVE-2019-0808");

        Assert.Equal("9c94bd33-c589-48c4-9431-dace397b0067", exploited.DeviceId);
        Assert.Equal("SASE-PC1", exploited.DeviceName);
        Assert.Equal(7.8, exploited.CvssScore);
        Assert.Equal("high", exploited.Severity);
        Assert.Equal("new", exploited.MitigationStatus);
        Assert.True(exploited.ExploitAvailable);
        Assert.Equal("34777", exploited.VirtualPatchRuleId);
        // The endpoint dates the scan, not the CVE, so the device timestamp is the only "last seen"
        // available — and a finding with no last-seen date at all ages wrongly.
        Assert.Equal(new DateTime(2026, 9, 10, 6, 8, 33, DateTimeKind.Utc), exploited.LastDetected);
        // vulnerableDevices carries no description field, so the affected software is the description:
        // without it the finding reads as a bare CVE id and says nothing a triager can act on.
        Assert.Contains("7-Zip", exploited.Description);
        Assert.Contains(@"C:\Program Files\7-Zip\", exploited.Description);

        Assert.Equal("medium", findings.Single(f => f.CveId == "CVE-2026-2222").Severity);
    }

    [Fact]
    public void ABareCveStringListIsAlsoAccepted()
    {
        var findings = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {"id":"agent-1","cveList":["CVE-2026-3333","CVE-2026-4444"]}
            """));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("agent-1", f.DeviceId));
    }

    /// <summary>
    /// A prevention rule existing for a CVE is not a virtual patch enforced on the device.
    ///
    /// Vision One documents <c>protectionRules</c> as "the list of prevention rules associated with
    /// the CVE" — the catalogue, not the deployment. Inferring an applied patch from it means that with
    /// <c>VirtualPatchClosesFinding</c> on, every CVE Trend Micro happens to ship an IPS rule for gets
    /// mitigated in NetRisk while the device sits unprotected. The applied state is
    /// <c>mitigationStatus</c>, and only that.
    /// </summary>
    [Fact]
    public void AProtectionRuleAloneIsNotAVirtualPatch()
    {
        var finding = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {"id":"a","cveRecords":[{"id":"CVE-2026-5555","mitigationStatus":"new",
                                     "protectionRules":[{"id":"1009999","product":"TP"}]}]}
            """)).Single();

        Assert.False(finding.VirtualPatchApplied);
        // Still recorded: which rule would cover it is useful, it just is not evidence that it does.
        Assert.Equal("1009999", finding.VirtualPatchRuleId);
    }

    [Fact]
    public void AMitigatedCveIsReportedAsVirtuallyPatched()
    {
        var finding = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {"id":"a","cveRecords":[{"id":"CVE-2026-5555","mitigationStatus":"mitigated",
                                     "protectionRules":[{"id":"1009999"}]}]}
            """)).Single();

        Assert.True(finding.VirtualPatchApplied);
        Assert.Equal("1009999", finding.VirtualPatchRuleId);
    }

    /// <summary>
    /// <c>closed</c> is the console's "Remediated" and <c>dismissed</c> is a CVE an analyst discarded.
    /// Importing either would create a NetRisk finding nothing ever closes, because this import is
    /// deliberately not a full scan and so the lifecycle service never resolves it.
    /// </summary>
    [Theory]
    [InlineData("closed")]
    [InlineData("dismissed")]
    public void ARemediatedOrDismissedCveIsNotImported(string status)
    {
        Assert.Empty(TrendMicroClient.ParseDeviceVulnerabilities(Element(
            $$"""{"id":"a","cveRecords":[{"id":"CVE-2026-5555","mitigationStatus":"{{status}}"}]}""")));
    }

    /// <summary>
    /// An acceptance recorded in Vision One is not an acceptance recorded in NetRisk, which has its own
    /// approval workflow — so the CVE still becomes a finding, carrying Trend's opinion with it.
    /// </summary>
    [Fact]
    public void ACveAcceptedInVisionOneIsStillImported()
    {
        var finding = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {"id":"a","cveRecords":[{"id":"CVE-2026-5555","mitigationStatus":"accepted"}]}
            """)).Single();

        Assert.Equal("accepted", finding.MitigationStatus);
    }

    /// <summary>
    /// Vision One has no exploit-available boolean. <c>globalExploitActivityLevel: high</c> is what the
    /// console shows as "Actively exploited", and <c>exploitAttemptCount</c> counts attempts seen in
    /// this tenant; reading neither leaves every Vision One finding claiming no exploit exists.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"C","globalExploitActivityLevel":"high"}""", true)]
    [InlineData("""{"id":"C","exploitAttemptCount":3}""", true)]
    [InlineData("""{"id":"C","globalExploitActivityLevel":"low"}""", false)]
    [InlineData("""{"id":"C","exploitAvailable":false,"exploitAttemptCount":3}""", false)]
    public void ExploitActivityIsReadFromTheFieldsVisionOneActuallySends(string record, bool expected)
    {
        var finding = TrendMicroClient.ParseDeviceVulnerabilities(
            Element($$"""{"id":"a","cveRecords":[{{record}}]}""")).Single();

        Assert.Equal(expected, finding.ExploitAvailable);
    }

    [Fact]
    public void ADeviceWithNoVulnerabilityArrayYieldsNothing()
    {
        Assert.Empty(TrendMicroClient.ParseDeviceVulnerabilities(Element("""{"id":"a"}""")));
    }

    // --- paging and transport ---------------------------------------------------------------

    [Fact]
    public async Task PagingFollowsNextLinkVerbatim()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""
                {"items":[{"id":"a1","name":"one"}],
                 "nextLink":"https://api.eu.xdr.trendmicro.com/v3.0/asrm/attackSurfaceDevices?token=abc"}
                """)
            .EnqueueJson("""{"items":[{"id":"a2","name":"two"}]}""");

        var client = new TrendMicroClient(Log, http);

        var devices = await client.GetDevicesAsync(Connection(), "key");

        Assert.Equal(2, devices.Count);
        // Rebuilding the query instead of using the link verbatim is how a paged sync silently re-reads
        // page one forever.
        Assert.Contains("token=abc", http.Requests[1].Url);
    }

    [Fact]
    public async Task TheApiKeyTravelsAsABearerToken()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"items":[]}""");

        await new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key");

        Assert.Equal("Bearer key", http.Requests[0].Headers["Authorization"]);
        Assert.StartsWith("https://api.eu.xdr.trendmicro.com/v3.0/asrm/", http.Requests[0].Url);
    }

    [Fact]
    public async Task AFailedPageIsAnIntegrationFailureNotASilentEmptyResult()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(500);

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key"));

        Assert.Equal("Trend Micro Vision One", thrown.Provider);
    }

    [Fact]
    public async Task ANonJsonBodyIsReportedClearly()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("<html>gateway</html>");

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key"));

        Assert.Contains("not JSON", thrown.Message);
    }

    // --- test connection --------------------------------------------------------------------

    [Fact]
    public async Task TestReadsOneRowOfTheEndpointTheSyncActuallyUses()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"items":[],"totalCount":42}""");

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        Assert.True(result.Success);
        Assert.Equal("42", result.Details["Devices visible"]);
        // A /whoami-style probe would pass with a token that lacks the ASRM permission.
        Assert.Contains("asrm/attackSurfaceDevices", http.Requests[0].Url);
    }

    [Fact]
    public async Task TestWithNoApiKeySaysSoWithoutCallingAnything()
    {
        var http = new FakeOutboundHttpClient();

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), null);

        Assert.False(result.Success);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A401NamesTheRegionBecauseKeysAreRegionBound()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(401);

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        Assert.False(result.Success);
        Assert.Contains("'eu'", result.Message);
    }

    [Fact]
    public async Task A403NamesTheMissingPermission()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(403);

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        Assert.Contains("Attack Surface Risk Management", result.Message);
    }

    [Fact]
    public async Task AnUnreachableRegionIsReported()
    {
        var http = new FakeOutboundHttpClient().EnqueueTransportError("Name or service not known");

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        Assert.False(result.Success);
        Assert.Contains("Name or service not known", result.Message);
    }

    // --- write-back -------------------------------------------------------------------------

    [Fact]
    public async Task AnExemptionWriteBackPostsTheUpdateOperationArray()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("{}");

        var updated = await new TrendMicroClient(Log, http).UpdateDeviceAsync(Connection(), "key",
            "agent-1", 5, "Accepted in NetRisk");

        Assert.True(updated);

        var request = Assert.Single(http.Requests);

        Assert.EndsWith("/v3.0/asrm/attackSurfaceDevices/update", request.Url);
        Assert.Contains("agent-1", request.Body!);
    }

    [Fact]
    public async Task ARefusedWriteBackReturnsFalseRatherThanThrowing()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(403);

        // A refused write-back must not fail the sync that triggered it.
        Assert.False(await new TrendMicroClient(Log, http)
            .UpdateDeviceAsync(Connection(), "key", "agent-1", null, null));
    }

    // --- failure reporting ------------------------------------------------------------------
    //
    // A 403 from the ASRM endpoints has three different causes — a role without the Attack Surface Risk
    // Management permission, a role whose data scope excludes the assets, and a tenant without the
    // entitlement — and the status code tells them apart not at all. Vision One puts the answer in the
    // body's error code, which the paged reads used to discard, so a real failed sync logged "HTTP 403"
    // and nothing else. These assert that the body reaches the message.

    [Fact]
    public async Task A403FromAPagedReadCarriesVisionOnesOwnErrorCode()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(403, """
            {"error":{"code":"AccessDenied","message":"The user does not have permission to access ASRM."}}
            """);

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key"));

        Assert.Contains("AccessDenied", thrown.Message);
        Assert.Contains("does not have permission", thrown.Message);
        // And still says which endpoint and what to change.
        Assert.Contains("/v3.0/asrm/attackSurfaceDevices", thrown.Message);
        Assert.Contains("Attack Surface Risk Management", thrown.Message);
    }

    [Fact]
    public async Task A403FromAPagedReadNamesTheThreeCausesOperatorsHaveToCheck()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(403);

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key"));

        Assert.Contains("Attack Surface Risk Management", thrown.Message);
        Assert.Contains("data and app objects", thrown.Message);
        Assert.Contains("entitlement", thrown.Message);
    }

    [Fact]
    public async Task TheVulnerabilityReadReportsTheSameDetailAsTheInventoryRead()
    {
        var body = """{"error":{"code":"NotEntitled","message":"CREM is not enabled for this tenant."}}""";

        var vulnerabilities = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, new FakeOutboundHttpClient().EnqueueFailure(403, body))
                .GetVulnerableDevicesAsync(Connection(), "key"));

        Assert.Contains("NotEntitled", vulnerabilities.Message);
        Assert.Contains("/v3.0/asrm/vulnerableDevices", vulnerabilities.Message);
        // The permission this endpoint needs is not the one the inventory needs, and saying "grant the
        // ASRM permission" to an operator who granted it and watched hosts import is a dead end.
        Assert.Contains("Dashboards & Reports", vulnerabilities.Message);
        Assert.Contains("Flex credits", vulnerabilities.Message);
    }

    /// <summary>
    /// A connection that syncs CVEs is only healthy if the CVE endpoint answers too.
    ///
    /// The two endpoints need different role permissions, so a key with the ASRM permission and not
    /// the Reports one passes an inventory-only probe and then imports every host and no findings —
    /// success on the Test Connection button, silence for 48 minutes, `0 finding(s) created`. That is
    /// the failure this integration actually shipped, and the test button said the connection was fine
    /// throughout.
    /// </summary>
    [Fact]
    public async Task TheConnectionTestFailsWhenTheKeyCannotReadCves()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/asrm/vulnerableDevices", """{"error":{"code":"AccessDenied"}}""", 403)
            .RuleFor("/asrm/attackSurfaceDevices", """{"items":[],"totalCount":42}""");

        var result = await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        Assert.False(result.Success);
        Assert.Contains("hosts and no findings", result.Message);
        Assert.Contains("Dashboards & Reports", result.Message);
    }

    [Fact]
    public async Task TheConnectionTestSkipsTheCveProbeWhenCvesAreNotSynced()
    {
        var connection = Connection();
        connection.SyncVulnerabilities = false;

        var http = new FakeOutboundHttpClient()
            .RuleFor("/asrm/vulnerableDevices", "{}", 403)
            .RuleFor("/asrm/attackSurfaceDevices", """{"items":[],"totalCount":42}""");

        var result = await new TrendMicroClient(Log, http).TestAsync(connection, "key");

        Assert.True(result.Success);
        Assert.DoesNotContain(http.Requests, r => r.Url.Contains("vulnerableDevices"));
    }

    /// <summary>
    /// The CVE pass must not read the inventory endpoint. This is the defect itself: the inventory row
    /// carries <c>cveCount</c> and no CVE identities, so pointing the CVE pass at it produced a sync
    /// that reported "0 finding(s) created" against a tenant full of them, indefinitely and silently.
    /// </summary>
    [Fact]
    public async Task TheCveReadGoesToVulnerableDevicesAndNotTheInventory()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"items":[]}""");

        await new TrendMicroClient(Log, http).GetVulnerableDevicesAsync(Connection(), "key");

        var url = Assert.Single(http.Requests).Url;

        Assert.Contains("/v3.0/asrm/vulnerableDevices", url);
        Assert.DoesNotContain("attackSurfaceDevices", url);
        // Devices with no detected CVEs are a second full crawl of the tenant for rows that are thrown
        // away; "affected" is the default but stating it is what stops that being a silent regression.
        Assert.Contains("cveDetectionStatus=affected", url);
        // vulnerableDevices accepts top only from 10, 50, 100, 200 — 500 and 1000 are a 400 here even
        // though the inventory endpoint takes them.
        Assert.Contains("top=200", url);
    }

    /// <summary>
    /// The risk score is published as <c>latestRiskScore</c> — the name in Vision One's own filter and
    /// orderBy documentation for attackSurfaceDevices. The parser read three other spellings and not
    /// that one, so every device came back with no score and the entity's cyber risk index was computed
    /// from nothing.
    /// </summary>
    [Fact]
    public void TheRiskScoreIsReadFromTheNameVisionOnePublishes()
    {
        var device = TrendMicroClient.ParseDevice(Element("""
            {
              "id": "agent-9",
              "deviceName": "web-01",
              "osPlatform": "Linux",
              "latestRiskScore": 74,
              "criticality": "high"
            }
            """));

        Assert.NotNull(device);
        Assert.Equal(74, device!.RiskScore);
        Assert.Equal("Linux", device.OperatingSystem);
    }

    // --- the paths and parameters Vision One actually accepts -------------------------------

    /// <summary>
    /// Vision One accepts <c>top</c> only from {10, 50, 100, 200, 500, 1000}. The connection test used
    /// to ask for <c>top=1</c>, which is a 400 — a healthy key reported as a broken connection, and a
    /// failure that could not appear until the tenant's ASRM permission was fixed and the 403 that had
    /// been masking it went away.
    /// </summary>
    [Fact]
    public async Task TheConnectionTestAsksForAPageSizeVisionOneAccepts()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"items":[],"totalCount":0}""");

        await new TrendMicroClient(Log, http).TestAsync(Connection(), "key");

        var url = http.Requests[0].Url;

        Assert.Contains("top=10", url);
        Assert.DoesNotContain("top=1&", url);
        Assert.False(url.EndsWith("top=1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every path this client reads must be one Vision One publishes — checked against the paths in
    /// Vision One's own OpenAPI specification (Automation Center → API Reference → v3.0, "Download
    /// OpenAPI specification"), which is the authority this list was previously guessed at instead of.
    ///
    /// <c>/v3.0/asrm/vulnerableDevices</c> is on that list. 2.19.6 removed the call to it on the
    /// strength of Trend's <c>vision-one-mcp-server</c>, whose ASRM coverage is a subset of the API —
    /// and replaced it with a read of <c>attackSurfaceDevices</c>, which publishes no CVEs. An
    /// allowlist assembled from anything other than the specification is how that happened.
    /// </summary>
    [Fact]
    public async Task EveryReadGoesToAPublishedAsrmEndpoint()
    {
        string[] published =
        [
            "/v3.0/asrm/attackSurfaceDevices",
            "/v3.0/asrm/attackSurfaceDevices/update",
            "/v3.0/asrm/vulnerableDevices"
        ];

        var http = new FakeOutboundHttpClient();
        var client = new TrendMicroClient(Log, http);

        await client.TestAsync(Connection(), "key");
        await client.GetDevicesAsync(Connection(), "key");
        await client.GetVulnerableDevicesAsync(Connection(), "key");
        await client.UpdateDeviceAsync(Connection(), "key", "agent-1", 4, null);

        Assert.NotEmpty(http.Requests);

        foreach (var request in http.Requests)
        {
            var path = new Uri(request.Url).AbsolutePath;
            Assert.Contains(path, published);
        }
    }

    [Fact]
    public async Task TheTestButtonAndAFailedSyncGiveTheSameDiagnosis()
    {
        var body = """{"error":{"code":"AccessDenied","message":"No permission."}}""";

        var test = await new TrendMicroClient(Log, new FakeOutboundHttpClient().EnqueueFailure(403, body))
            .TestAsync(Connection(), "key");

        var sync = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, new FakeOutboundHttpClient().EnqueueFailure(403, body))
                .GetDevicesAsync(Connection(), "key"));

        Assert.False(test.Success);
        Assert.Contains("AccessDenied", test.Message);
        Assert.Equal(test.Message, sync.Message);
    }

    [Fact]
    public void AnInnerErrorIsNamedBecauseItIsWhereTheSpecificCodeLives()
    {
        // The outer code is the generic one; innerError.code is what distinguishes a role that lacks the
        // permission from a role whose data scope excludes the assets, so it wins over the service name.
        var detail = TrendMicroClient.DescribeError("""
            {"error":{"code":"AccessDenied","message":"Denied.",
             "innerError":{"service":"asrm","code":"RoleScopeMismatch"}}}
            """);

        Assert.Equal("AccessDenied: Denied. (innerError RoleScopeMismatch)", detail);
    }

    [Fact]
    public void AnInnerErrorWithOnlyAServiceNameStillNamesTheService()
    {
        var detail = TrendMicroClient.DescribeError("""
            {"error":{"code":"AccessDenied","message":"Denied.","innerError":{"service":"asrm"}}}
            """);

        Assert.Equal("AccessDenied: Denied. (innerError asrm)", detail);
    }

    [Theory]
    // The flat shape Vision One answers with on some gateways.
    [InlineData("""{"code":"Forbidden","message":"no"}""", "Forbidden: no")]
    // The array shape.
    [InlineData("""{"errors":[{"code":"A","message":"one"},{"code":"B","message":"two"}]}""",
        "A: one; B: two")]
    // A message with no code at all.
    [InlineData("""{"error":{"message":"just a sentence"}}""", "just a sentence")]
    // A code with no message.
    [InlineData("""{"error":{"code":"AccessDenied"}}""", "AccessDenied")]
    public void EveryErrorShapeVisionOneUsesIsRead(string body, string expected)
    {
        Assert.Equal(expected, TrendMicroClient.DescribeError(body));
    }

    [Fact]
    public void ANonJsonErrorBodyIsPassedThroughOnOneLine()
    {
        var detail = TrendMicroClient.DescribeError("<html>\n  <body>502 Bad Gateway</body>\n</html>");

        Assert.Equal("<html> <body>502 Bad Gateway</body> </html>", detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A successful parse with nothing in it: "Vision One said: {}" is noise, not a diagnosis.
    [InlineData("{}")]
    public void ABodyWithNothingInItAddsNothingToTheMessage(string? body)
    {
        Assert.Null(TrendMicroClient.DescribeError(body));

        var response = new ServerServices.Interfaces.OutboundHttpResponse { StatusCode = 403, Body = body };

        Assert.DoesNotContain("Vision One said", TrendMicroClient.FailureMessage(
            Connection(), response, "/v3.0/asrm/attackSurfaceDevices"));
    }

    [Fact]
    public void AHugeErrorBodyIsShortenedBecauseTheSyncLogColumnIsBounded()
    {
        var body = "{\"error\":{\"message\":\"" + new string('x', 5000) + "\"}}";

        var detail = TrendMicroClient.DescribeError(body);

        Assert.NotNull(detail);
        Assert.True(detail!.Length <= 401, $"detail was {detail.Length} characters");
        Assert.EndsWith("…", detail);
    }

    [Fact]
    public void ATransportFailureStillReadsAsUnreachableRatherThanAStatusCode()
    {
        var response = new ServerServices.Interfaces.OutboundHttpResponse
        {
            StatusCode = 0, TransportError = "Name or service not known"
        };

        var message = TrendMicroClient.FailureMessage(Connection(), response, "/v3.0/asrm/attackSurfaceDevices");

        Assert.Contains("could not be reached", message);
        Assert.Contains("Name or service not known", message);
        Assert.DoesNotContain("HTTP 0", message);
    }

    [Fact]
    public async Task A429StillSaysToRetryAndCarriesTheBody()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueFailure(429, """{"error":{"code":"TooManyRequests","message":"slow down"}}""");

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new TrendMicroClient(Log, http).GetDevicesAsync(Connection(), "key"));

        Assert.Contains("rate-limiting", thrown.Message);
        Assert.Contains("TooManyRequests", thrown.Message);
    }

    // --- regions ----------------------------------------------------------------------------

    [Theory]
    [InlineData("us", "https://api.xdr.trendmicro.com")]
    [InlineData("EU", "https://api.eu.xdr.trendmicro.com")]
    [InlineData("mars", null)]
    public void RegionsResolveToTheirApiRoot(string region, string? expected)
    {
        Assert.Equal(expected, TrendMicroRegions.BaseUrlFor(region));
    }
}
