using System;
using System.Linq;
using Contracts.Importers;
using JetBrains.Annotations;
using ServerServices.Integrations.Tenable;
using Xunit;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>
/// M56, T266–T267 (S40 §9.4, §12.2 <c>TenableNormalizerTest</c>): an export record in the documented format becomes a
/// finding keyed by <c>finding_id</c> with VPR (v2 first), CVSS, EPSS, exploit maturity and the 0–4 severity; a FIXED
/// record is marked fixed; an asset carries its identities, canonical tags, ACR and AES — and nothing produces a criticality.
/// </summary>
[TestSubject(typeof(TenableNormalizer))]
public class TenableNormalizerTest
{
    [Fact]
    public void TestAVulnerabilityRecordBecomesAFindingWithItsSignals()
    {
        var record = Assert.Single(TenableNormalizer.ParseVulnerabilities(TenableFixtures.Chunk(TenableFixtures.Vuln("f-1"))));
        var f = record.Finding;

        Assert.Equal(("tenable-vm", "f-1", "19506"), (f.Tool, f.ToolUniqueId, f.RuleId));
        Assert.Equal((NormalizedSeverity.High, 8.9, 9.8), (f.Severity, f.VprScore, f.Cvss3BaseScore));
        Assert.Equal(("FUNCTIONAL", true), (f.ExploitCodeMaturity, f.ExploitAvailable));
        Assert.Equal(["CVE-2024-0001"], f.Cves);
        Assert.Equal(0.42, record.Epss);
        Assert.Equal(("10.0.0.5", "web-01", "443", "tcp"), (f.Host!.Ip, f.Host.HostName, f.Host.Port, f.Host.Protocol));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), f.FirstSeen);
        Assert.Equal("asset-1", f.ToolFields["assetUuid"]);
        Assert.False(record.IsFixed);
    }

    [Fact]
    public void TestVprV2WinsOverTheOldModel()
    {
        var record = Assert.Single(TenableNormalizer.ParseVulnerabilities(
            TenableFixtures.Chunk(TenableFixtures.Vuln("f-1", vpr: "5.1", vprV2: "7.7"))));

        Assert.Equal(7.7, record.Finding.VprScore);
    }

    [Fact]
    public void TestWithoutVprAndCveTheCvssCarriesTheFinding()
    {
        var record = Assert.Single(TenableNormalizer.ParseVulnerabilities(
            TenableFixtures.Chunk(TenableFixtures.Vuln("f-1", vpr: null, epss: null, cves: ""))));

        Assert.Null(record.Finding.VprScore);
        Assert.Empty(record.Finding.Cves);
        Assert.Equal(9.8, record.Finding.Cvss3BaseScore);
        Assert.Null(record.Epss);
    }

    [Fact]
    public void TestAFixedRecordIsMarkedFixed()
    {
        var record = Assert.Single(TenableNormalizer.ParseVulnerabilities(
            TenableFixtures.Chunk(TenableFixtures.Vuln("f-1", state: "FIXED"))));

        Assert.True(record.IsFixed);
        Assert.Equal("fixed", record.Finding.RawStatus);
    }

    [Theory]
    [InlineData(0, NormalizedSeverity.None)]
    [InlineData(1, NormalizedSeverity.Low)]
    [InlineData(4, NormalizedSeverity.Critical)]
    public void TestTheSeverityIsTenablesZeroToFour(int id, NormalizedSeverity expected) =>
        Assert.Equal(expected, TenableNormalizer.MapSeverity(id, "high", 9.9));

    [Fact]
    public void TestAPercentageEpssIsReadAsAProbabilityAndAnOutOfRangeOneIsDropped()
    {
        var records = TenableNormalizer.ParseVulnerabilities(TenableFixtures.Chunk(
            TenableFixtures.Vuln("f-1", epss: "42"), TenableFixtures.Vuln("f-2", epss: "-3")));

        Assert.Equal([0.42, null], records.Select(r => r.Epss).ToArray());
    }

    [Fact]
    public void TestARecordWithoutIdentityIsSkipped() =>
        Assert.Empty(TenableNormalizer.ParseVulnerabilities("""[{"state":"OPEN","plugin":{"id":1}}, 3]"""));

    [Fact]
    public void TestAnAssetCarriesItsIdentitiesTagsAndRatings()
    {
        var asset = Assert.Single(TenableNormalizer.ParseAssets(TenableFixtures.Chunk(
            TenableFixtures.Asset("a-1", mac: "AA:BB:CC:DD:EE:FF", tags: TenableFixtures.Tag("BU", " Law ")))));

        Assert.Equal(("a-1", "web-01", "web-01.corp.example", "10.0.0.5"),
            (asset.Id, asset.HostNames.Single(), asset.Fqdns.Single(), asset.Ipv4s.Single()));
        Assert.Equal("AA:BB:CC:DD:EE:FF", asset.MacAddresses.Single());
        Assert.Equal(["BU: Law"], asset.Tags);
        Assert.Equal((8.0, 750.0), (asset.Acr, asset.Aes));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc), asset.LastAssessedAt);
        Assert.False(asset.Gone);
    }

    [Fact]
    public void TestTheV1SpellingsAndAMissingLicenceAreRead()
    {
        var v1 = Assert.Single(TenableNormalizer.ParseAssets(TenableFixtures.Chunk(
            TenableFixtures.Asset("a-1", acr: 3, aes: 1000, v1Names: true))));
        var unlicensed = Assert.Single(TenableNormalizer.ParseAssets(TenableFixtures.Chunk(
            TenableFixtures.Asset("a-2", acr: null, aes: null))));

        Assert.Equal((3.0, 1000.0, "web-01"), (v1.Acr, v1.Aes, v1.HostNames.Single()));
        Assert.Equal((null, null), (unlicensed.Acr, unlicensed.Aes));
    }

    [Fact]
    public void TestAnAesOutsideTheThousandScaleIsNotAReading()
    {
        var asset = Assert.Single(TenableNormalizer.ParseAssets(TenableFixtures.Chunk(
            TenableFixtures.Asset("a-1", acr: 11, aes: 1001))));

        Assert.Equal((null, null), (asset.Acr, asset.Aes));
    }

    [Fact]
    public void TestADeletedAssetIsGone() =>
        Assert.True(Assert.Single(TenableNormalizer.ParseAssets(TenableFixtures.Chunk(
            TenableFixtures.Asset("a-1", deleted: "2026-11-01T00:00:00Z")))).Gone);

    [Fact]
    public void TestNothingTheNormalizerProducesCarriesACriticality()
    {
        // The finding's host is a NormalizedHost, which has no criticality; the asset holds ACR as a reading only.
        Assert.DoesNotContain(typeof(NormalizedHost).GetProperties(), p => p.Name.Contains("Critical"));
        Assert.DoesNotContain(typeof(TenableAsset).GetProperties(), p => p.Name.Contains("Critical"));
    }
}
