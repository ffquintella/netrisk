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

    [Fact]
    public void APerDeviceCveListBecomesOneFindingPerCve()
    {
        var findings = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {
              "id": "agent-1",
              "name": "db-prod-01",
              "vulnerabilities": [
                {"cveId":"CVE-2026-1111","severity":"critical","cvssScore":9.8,
                 "virtualPatchApplied":true,"virtualPatchRuleId":"1011234"},
                {"cveId":"CVE-2026-2222","severity":"medium","cvssScore":5.4}
              ]
            }
            """));

        Assert.Equal(2, findings.Count);

        // One finding per device listing thirty CVEs cannot be triaged or given an SLA.
        var patched = findings.Single(f => f.CveId == "CVE-2026-1111");

        Assert.True(patched.VirtualPatchApplied);
        Assert.Equal("1011234", patched.VirtualPatchRuleId);
        Assert.Equal(9.8, patched.CvssScore);

        Assert.False(findings.Single(f => f.CveId == "CVE-2026-2222").VirtualPatchApplied);
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

    [Fact]
    public void AVirtualPatchIsInferredFromTheRuleIdWhenNoFlagIsPresent()
    {
        var finding = TrendMicroClient.ParseDeviceVulnerabilities(Element("""
            {"id":"a","vulnerabilities":[{"cveId":"CVE-2026-5555","ipsRuleId":"1009999"}]}
            """)).Single();

        Assert.True(finding.VirtualPatchApplied);
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
