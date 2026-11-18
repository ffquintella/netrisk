using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using Model.RiskIndex;
using Serilog;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M57, T271–T273 and T275 (S40 §6.9, §9.5, §12.2 <c>TrendMicroPostureSyncTest</c>): the posture and asset groups become
/// reference readings, the attack-surface endpoints become scored vendor assets, the Workbench alerts an attack-intensity
/// reading — each opted into and failing on its own — and the device's detection times, exploit signals and criticality
/// source are kept as the OAS spells them. Fixtures follow the published payload shapes (S40 §15.1: not checked against a
/// live tenant).
/// </summary>
[TestSubject(typeof(TrendMicroService))]
public class TrendMicroPostureSyncTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Finance = 8;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();
    private readonly ITrendMicroService _svc;

    public TrendMicroPostureSyncTest()
    {
        _svc = GetService<ITrendMicroService>();
        Seed(ctx =>
        {
            foreach (var id in new[] { Acme, Finance })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
        });
    }

    private Task<TrendMicroConnectionView> ConnectionAsync(bool posture = false, bool surface = false, bool workbench = false,
        bool riskScores = false, bool vulnerabilities = false) =>
        _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "Acme Vision One", Region = "eu", BaseUrl = "", EntityId = Acme, Enabled = true, SyncIntervalHours = 24,
            SyncVulnerabilities = vulnerabilities, SyncRiskScores = riskScores, SyncPosture = posture,
            SyncAttackSurfaceObjects = surface, SyncWorkbenchAlerts = workbench
        }, "api-key");

    private const string Posture = """
        {"riskIndex":63,"riskLevel":"high","riskCategoryLevel":{"exposure":"high","attack":"medium","securityConfiguration":"low"},
         "securityConfigurationStatus":{"agentFeatureStatus":[{"featureName":"behaviorMonitoring","adoptionRate":0.8},
                                                              {"featureName":"predictiveMachineLearning","adoptionRate":60}]}}
        """;

    private const string Groups = """
        {"items":[{"id":"g1","name":"Finance","riskIndex":72,"riskLevel":"high","assetCount":40},
                  {"id":"g2","name":"No score yet"}]}
        """;

    private void Stub(string? posture = Posture, string groups = Groups, int postureStatus = 200)
    {
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[{"id":"agent-1","name":"srv-01"}]}""");
        if (posture != null) FakeOutboundHttpClient.RuleFor("/asrm/securityPosture", posture, postureStatus);
        FakeOutboundHttpClient.RuleFor("/asrm/assetGroups", groups);
    }

    // --- T271 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestThePostureAndAssetGroupsBecomeReferenceReadings()
    {
        var view = await ConnectionAsync(posture: true);
        Seed(ctx => ctx.VendorEntityMappings.Add(new VendorEntityMapping
        {
            Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = view.Id, MatchKind = VendorMatchKind.V1AssetGroup,
            MatchValue = "Finance", EntityId = Finance, Priority = 1, RestampPolicy = RestampPolicy.OnlyUnassigned,
            Enabled = true, CreatedAt = Now
        }));
        Stub();

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((3, 0), (result.IndicatorsWritten, result.Errors));
        await using var db = OpenContext();
        var cri = db.VendorScores.Single(v => v.Source == VendorScoreSource.V1Cri);
        Assert.Equal((63d, 100d, "high", Acme, VendorScoreObjectKind.Tenant, TrendMicroService.TenantRef),
            (cri.Value, cri.ScaleMax, cri.Level, cri.EntityId!.Value, cri.ObjectKind, cri.ExternalRef));
        Assert.Contains("riskCategoryLevel", cri.Details);

        // 0.8 and 60 % read as fractions: the mean adoption is 0.7 (V1-SCS scores 1 − 0.7).
        Assert.Equal(0.7, db.VendorScores.Single(v => v.Source == VendorScoreSource.V1SecurityConfig).Value, 6);

        // The group rule names the group's node; it attributes no device (S40 §6.6), and a group without a score is no reading.
        var group = db.VendorScores.Single(v => v.Source == VendorScoreSource.V1AssetGroup);
        Assert.Equal(("g1", 72d, Finance, VendorScoreObjectKind.AssetGroup), (group.ExternalRef, group.Value,
            group.EntityId!.Value, group.ObjectKind));
        Assert.Equal(Acme, db.Hosts.Single().EntityId);
    }

    [Fact]
    public async Task TestAnUnchangedPostureExtendsItsReadingAndAChangeOpensANewOne()
    {
        var view = await ConnectionAsync(posture: true);
        Stub();
        await _svc.SyncAsync(view.Id);
        await _svc.SyncAsync(view.Id);

        FakeOutboundHttpClient.Reset();
        Stub(posture: Posture.Replace("\"riskIndex\":63", "\"riskIndex\":58"));
        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Equal([63d, 58d], db.VendorScores.Where(v => v.Source == VendorScoreSource.V1Cri).OrderBy(v => v.Id)
            .Select(v => v.Value).ToArray());
    }

    [Fact]
    public async Task TestWithThePostureOptionOffNothingIsRequestedOrWritten()
    {
        var view = await ConnectionAsync();
        Stub();

        await _svc.SyncAsync(view.Id);

        Assert.DoesNotContain(FakeOutboundHttpClient.Requests, r => r.Url.Contains("securityPosture") || r.Url.Contains("assetGroups"));
        await using var db = OpenContext();
        Assert.Empty(db.VendorScores.ToList());
    }

    [Fact]
    public async Task TestARefusedPostureIsReportedAndTheOtherPhasesStillRun()
    {
        var view = await ConnectionAsync(posture: true);
        Stub(posture: """{"error":{"code":"AccessDenied","message":"Insufficient permissions"}}""", postureStatus: 403);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.Errors);
        Assert.Contains(result.Messages, m => m.Contains("Third-party auditing (API only)"));
        await using var db = OpenContext();
        Assert.Single(db.VendorScores.Where(v => v.Source == VendorScoreSource.V1AssetGroup).ToList());
        var log = db.IntegrationSyncLogs.Single();
        Assert.Equal(IntegrationSyncStatus.PartiallySucceeded, log.Status);

        // The key resolved through ISecretResolver reaches the request header only — never a message or the trail.
        Assert.Contains(FakeOutboundHttpClient.Requests, r => r.Headers["Authorization"] == "Bearer api-key");
        Assert.DoesNotContain(result.Messages, m => m.Contains("api-key"));
        Assert.DoesNotContain("api-key", log.ProgressLog ?? "");
    }

    [Fact]
    public async Task TestTheConnectionTestNamesWhatEachEnabledPhaseLacks()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/asrm/securityPosture", """{"error":{"code":"AccessDenied"}}""", 403)
            .RuleFor("/workbench/alerts", """{"error":{"code":"AccessDenied"}}""", 403)
            .RuleFor("/asrm/", """{"items":[],"totalCount":0}""");
        var connection = new TrendMicroConnection
        {
            Name = "t", Region = "eu", BaseUrl = "https://api.eu.xdr.trendmicro.com", SyncPosture = true,
            SyncAttackSurfaceObjects = true, SyncWorkbenchAlerts = true
        };

        var result = await new TrendMicroClient(Log, http).TestAsync(connection, "k");

        Assert.False(result.Success);
        Assert.Contains("Third-party auditing (API only)", result.Message);
        Assert.Contains("Workbench", result.Message);
        Assert.DoesNotContain("Attack surface access", result.Message);
        Assert.DoesNotContain("Bearer", result.Message);
    }

    // --- T272 ---------------------------------------------------------------------------------------------------------

    private void StubSurface(string fqdns, string? publicIps = """{"items":[]}""", int publicIpStatus = 200)
    {
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[]}""");
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceGlobalFqdns", fqdns);
        if (publicIps != null) FakeOutboundHttpClient.RuleFor("/asrm/attackSurfacePublicIpAddresses", publicIps, publicIpStatus);
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDomainAccounts",
            """{"items":[{"id":"u1","userAccountName":"svc-backup","riskScore":30}]}""");
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurface", """{"items":[]}""");
    }

    [Fact]
    public async Task TestAttackSurfaceObjectsBecomeVendorAssetsWithScores()
    {
        var view = await ConnectionAsync(surface: true);
        StubSurface("""
            {"items":[{"id":"f1","fqdn":"www.acme.com","latestRiskScore":55,"assetCriticality":"high",
                       "firstSeenDateTime":"2026-01-02T00:00:00Z","lastDetectDateTime":"2026-11-17T08:00:00Z"}]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((2, 0, 0), (result.VendorAssetsSynced, result.VendorAssetsGone, result.Errors));
        await using var db = OpenContext();
        var fqdn = db.VendorAssets.Single(a => a.AssetKind == VendorAssetKind.Fqdn);
        Assert.Equal(("f1", "www.acme.com", 4, "high", Acme, VendorAssetStatus.Active),
            (fqdn.ExternalId, fqdn.Name, fqdn.Criticality!.Value, fqdn.CriticalityRaw, fqdn.EntityId!.Value, fqdn.Status));
        Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), fqdn.LastSeenAt);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), fqdn.FirstSeenAt);

        var reading = db.VendorScores.Single(v => v.VendorAssetId == fqdn.Id);
        Assert.Equal((VendorScoreSource.V1VendorAsset, VendorScoreObjectKind.VendorAsset, 55d, Acme),
            (reading.Source, reading.ObjectKind, reading.Value, reading.EntityId!.Value));
        Assert.Equal(VendorAssetKind.DomainAccount, db.VendorAssets.Single(a => a.ExternalId == "u1").AssetKind);
    }

    [Fact]
    public async Task TestAnAssetNoLongerListedIsGoneAndAKindThatFailedKeepsItsAssets()
    {
        var view = await ConnectionAsync(surface: true);
        StubSurface("""{"items":[{"id":"f1","fqdn":"www.acme.com","latestRiskScore":55}]}""",
            """{"items":[{"id":"ip1","ipAddress":"203.0.113.5","latestRiskScore":20}]}""");
        await _svc.SyncAsync(view.Id);

        FakeOutboundHttpClient.Reset();
        StubSurface("""{"items":[]}""", "{}", publicIpStatus: 403);
        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((1, 1), (result.VendorAssetsGone, result.Errors));
        await using var db = OpenContext();
        Assert.Equal(VendorAssetStatus.Gone, db.VendorAssets.Single(a => a.ExternalId == "f1").Status);
        Assert.Equal(VendorAssetStatus.Active, db.VendorAssets.Single(a => a.ExternalId == "ip1").Status);
    }

    // --- T273 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestWorkbenchAlertsAreCountedPerDayAndSeverityAsAnIndicatorNotAFactor()
    {
        var view = await ConnectionAsync(workbench: true);
        var today = DateTime.UtcNow.Date;
        string At(int daysAgo, int hour) => today.AddDays(-daysAgo).AddHours(hour).ToString("yyyy-MM-ddTHH:mm:ssZ");
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[]}""");
        FakeOutboundHttpClient.RuleFor("/workbench/alerts", $$"""
            {"items":[{"id":"WB-1","severity":"high","createdDateTime":"{{At(2, 1)}}"},
                      {"id":"WB-2","severity":"high","createdDateTime":"{{At(2, 3)}}"},
                      {"id":"WB-2","severity":"high","createdDateTime":"{{At(2, 3)}}"},
                      {"id":"WB-3","severity":"Critical","createdDateTime":"{{At(5, 1)}}"},
                      {"id":"WB-4","severity":"low","createdDateTime":"{{At(40, 1)}}"}]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(3, result.WorkbenchAlerts);
        var request = FakeOutboundHttpClient.Requests.Single(r => r.Url.Contains("/workbench/alerts"));
        Assert.Contains("startDateTime=", request.Url);
        Assert.Contains("endDateTime=", request.Url);

        await using var db = OpenContext();
        var reading = db.VendorScores.Single(v => v.Source == VendorScoreSource.V1AttackIntensity);
        Assert.Equal((3d, 0d, VendorScoreObjectKind.Tenant, Acme), (reading.Value, reading.ScaleMax, reading.ObjectKind,
            reading.EntityId!.Value));
        using var details = JsonDocument.Parse(reading.Details!);
        Assert.Equal(2, details.RootElement.GetProperty("bySeverity").GetProperty("high").GetInt32());
        Assert.Equal(1, details.RootElement.GetProperty("bySeverity").GetProperty("critical").GetInt32());
        Assert.Equal(2, details.RootElement.GetProperty("byDay").GetArrayLength());

        // An indicator beside the index: V1-WB is not a source a profile can enable (S39 §3.2, I4).
        Assert.DoesNotContain("V1-WB", RiskIndexSources.All);
    }

    // --- T275 ---------------------------------------------------------------------------------------------------------

    /// <summary>D-11: <c>lastActivity</c> and <c>lastUsedIp</c> are not OAS fields; the old parser read the first as the last sighting.</summary>
    [Fact]
    public void TestTheLastDetectionIsReadFromTheOasFieldAndNotFromLastActivity()
    {
        using var wrong = JsonDocument.Parse("""{"id":"a","lastActivity":"2026-01-01T00:00:00Z","lastUsedIp":"10.0.0.1"}""");
        Assert.Null(TrendMicroClient.ParseDevice(wrong.RootElement)!.LastSeen);

        using var right = JsonDocument.Parse(
            """{"id":"a","lastDetectDateTime":"2026-11-17T08:00:00Z","firstSeenDateTime":"2025-03-01T00:00:00Z"}""");
        var device = TrendMicroClient.ParseDevice(right.RootElement)!;
        Assert.Equal((new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            (device.LastSeen, device.FirstSeen));
    }

    [Fact]
    public async Task TestTheDeviceDetectionTimesAreRecordedWithoutCustomTags()
    {
        var view = await ConnectionAsync();
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """
            {"items":[{"id":"agent-1","name":"srv-01","lastDetectDateTime":"2026-11-17T08:00:00Z",
                       "firstSeenDateTime":"2025-03-01T00:00:00Z","assetCustomTags":[{"key":"BU","value":"Law"}]}]}
            """);

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        var identity = db.HostExternalIds.Single();
        Assert.Equal((new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            (identity.LastSeenAt, identity.FirstSeenAt));
        Assert.Null(identity.Tags);
    }

    [Fact]
    public async Task TestExploitAttemptsAndGlobalActivityArePersistedApart()
    {
        Seed(ctx =>
        {
            foreach (var severity in new[] { 1, 2, 3, 4 })
                ctx.SlaConfigurations.Add(new SlaConfiguration
                {
                    Severity = severity, MaxTriageDays = 5, MaxRemediationDays = 30, EffectiveFrom = DateTime.UnixEpoch,
                    CreatedAt = DateTime.UnixEpoch
                });
        });
        var view = await ConnectionAsync(vulnerabilities: true);
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[{"id":"agent-1","name":"srv-01"}]}""");
        FakeOutboundHttpClient.RuleFor("/asrm/vulnerableDevices", """
            {"items":[{"id":"agent-1","name":"srv-01","cveRecords":[
              {"id":"CVE-2026-0001","cvssScore":8.1,"exploitAttemptCount":3,"globalExploitActivityLevel":"low","mitigationStatus":"new"},
              {"id":"CVE-2026-0002","cvssScore":8.1,"exploitAttemptCount":0,"globalExploitActivityLevel":"High","mitigationStatus":"new"},
              {"id":"CVE-2026-0003","cvssScore":8.1,"mitigationStatus":"new"}]}]}
            """);

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        var byCve = db.Vulnerabilities.ToList().ToDictionary(v => v.RuleId!);
        Assert.Equal((3, "low"), (byCve["CVE-2026-0001"].ExploitAttemptCount, byCve["CVE-2026-0001"].GlobalExploitActivity));
        Assert.Equal((0, "high"), (byCve["CVE-2026-0002"].ExploitAttemptCount, byCve["CVE-2026-0002"].GlobalExploitActivity));
        Assert.Equal(((int?)null, (string?)null),
            (byCve["CVE-2026-0003"].ExploitAttemptCount, byCve["CVE-2026-0003"].GlobalExploitActivity));
    }

    [Fact]
    public async Task TestVisionOneNeverOverwritesAManualOrCmdbCriticalityAndRecordsItsOwn()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(Host(301, "agent-1", 2, CriticalitySources.Manual));
            ctx.Hosts.Add(Host(302, "agent-2", 3, CriticalitySources.Cmdb));
            ctx.Hosts.Add(Host(303, "agent-3", 1, null));
        });
        var view = await ConnectionAsync(riskScores: true);
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """
            {"items":[{"id":"agent-1","name":"h-301","assetCriticality":"critical","latestRiskScore":10},
                      {"id":"agent-2","name":"h-302","assetCriticality":"critical","latestRiskScore":10},
                      {"id":"agent-3","name":"h-303","assetCriticality":"critical","latestRiskScore":10},
                      {"id":"agent-4","name":"h-new","assetCriticality":"high"}]}
            """);

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        (int?, string?) Of(string id)
        {
            var h = db.Hosts.Single(x => x.ExternalId == id);
            return (h.Criticality, h.CriticalitySource);
        }

        Assert.Equal((2, CriticalitySources.Manual), Of("agent-1"));
        Assert.Equal((3, CriticalitySources.Cmdb), Of("agent-2"));
        Assert.Equal((5, CriticalitySources.VisionOne), Of("agent-3"));
        Assert.Equal((4, CriticalitySources.VisionOne), Of("agent-4"));
    }

    private static Host Host(int id, string externalId, int criticality, string? source) => new()
    {
        Id = id, HostName = $"h-{id}", ExternalId = externalId, ExternalProvider = TrendMicroService.ProviderName, Source = "test",
        Status = 1, EntityId = Acme, Criticality = criticality, CriticalitySource = source, RegistrationDate = Now
    };
}
