using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Trend Micro Vision One connection management and synchronization
/// (Track 4 milestone 4.4).
///
/// The properties that make this integration safe to leave running overnight: an existing host is
/// matched rather than duplicated, data a person typed is not overwritten by data an agent guessed, a
/// virtual patch closes a finding only when the customer said it should, and the entity index is
/// weighted so a critical server at 90 is not averaged away by twenty test machines at 10.
/// </summary>
[TestSubject(typeof(TrendMicroService))]
public class TrendMicroServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly ITrendMicroService _svc;

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    public TrendMicroServiceInMemoryTest()
    {
        _svc = GetService<ITrendMicroService>();

        Seed(ctx =>
        {
            ctx.Entities.Add(new Entity
            {
                Id = 7, DefinitionName = "Acme", DefinitionVersion = "1", Status = "active"
            });

            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Email = "a@acme.com", Enabled = true,
                Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p")
            });

            ctx.SlaConfigurations.AddRange(
                Sla(4, 2, 15), Sla(3, 5, 30), Sla(2, 10, 60), Sla(1, 15, 90));
        });
    }

    private static SlaConfiguration Sla(int severity, int triage, int remediation) => new()
    {
        Severity = severity, MaxTriageDays = triage, MaxRemediationDays = remediation,
        EffectiveFrom = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private Task<TrendMicroConnectionView> ConnectionAsync(bool virtualPatchCloses = false,
        bool syncVulnerabilities = true, bool syncRiskScores = true, string name = "Acme Vision One") =>
        _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = name,
            Region = "eu",
            BaseUrl = "",
            EntityId = 7,
            Enabled = true,
            SyncIntervalHours = 24,
            SyncVulnerabilities = syncVulnerabilities,
            SyncRiskScores = syncRiskScores,
            VirtualPatchClosesFinding = virtualPatchCloses
        }, "api-key");

    private void StubApi(string devices, string? vulnerable = null, string? highRisk = null)
    {
        FakeOutboundHttpClient
            .RuleFor("/asrm/vulnerableDevices", vulnerable ?? """{"items":[]}""")
            .RuleFor("/asrm/highRiskDevices", highRisk ?? """{"items":[]}""")
            .RuleFor("/asrm/attackSurfaceDevices", devices);
    }

    // --- connections ------------------------------------------------------------------------

    [Fact]
    public async Task TheBaseUrlIsDerivedFromTheRegion()
    {
        var view = await ConnectionAsync();

        // A key issued in one region is rejected by every other; deriving the URL is what makes a
        // mistyped host impossible for the common case.
        Assert.Equal("https://api.eu.xdr.trendmicro.com", view.BaseUrl);
        Assert.True(view.HasApiKey);
    }

    [Fact]
    public async Task TheApiKeyIsStoredEncryptedAndNeverReturned()
    {
        await ConnectionAsync();

        await using var db = OpenContext();
        Assert.NotEqual("api-key", db.TrendMicroConnections.Single().EncryptedApiKey);
        Assert.Null(typeof(TrendMicroConnectionView).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task AnUnknownRegionWithNoExplicitBaseUrlIsRefused()
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateConnectionAsync(new TrendMicroConnection
            {
                Name = "x", Region = "mars", BaseUrl = "", SyncIntervalHours = 24
            }, "k"));

        Assert.Contains("not a Vision One region", thrown.Message);
    }

    [Fact]
    public async Task AnUnlistedRegionIsAllowedWithAnExplicitHttpsBaseUrl()
    {
        var view = await _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "private", Region = "custom", BaseUrl = "https://xdr.internal.acme",
            SyncIntervalHours = 24
        }, "k");

        Assert.Equal("https://xdr.internal.acme", view.BaseUrl);
    }

    [Fact]
    public async Task APlainHttpBaseUrlIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateConnectionAsync(new TrendMicroConnection
            {
                Name = "x", Region = "custom", BaseUrl = "http://xdr.internal.acme", SyncIntervalHours = 24
            }, "k"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public async Task AnAbsurdSyncIntervalIsRefused(int hours)
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateConnectionAsync(new TrendMicroConnection
            {
                Name = "x", Region = "eu", BaseUrl = "", SyncIntervalHours = hours
            }, "k"));
    }

    [Fact]
    public async Task UpdatingWithoutAKeyKeepsTheStoredOne()
    {
        var view = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new TrendMicroConnection
        {
            Id = view.Id, Name = view.Name, Region = "us", BaseUrl = "", SyncIntervalHours = 12
        }, apiKey: null);

        await using var db = OpenContext();
        var stored = db.TrendMicroConnections.Single();

        Assert.NotNull(stored.EncryptedApiKey);
        Assert.Equal("https://api.xdr.trendmicro.com", stored.BaseUrl);
    }

    [Fact]
    public async Task TestingAConnectionReachesTheAsrmEndpoint()
    {
        var view = await ConnectionAsync();

        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", """{"items":[],"totalCount":3}""");

        var result = await _svc.TestConnectionAsync(view.Id);

        Assert.True(result.Success, result.Message);
        Assert.Equal("eu", result.Details["Region"]);
    }

    // --- inventory --------------------------------------------------------------------------

    [Fact]
    public async Task InventorySyncCreatesHostsForNewDevices()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""
            {"items":[{"id":"agent-1","name":"db-prod-01","fqdn":"db-prod-01.acme.local",
                       "ip":["10.0.0.5"],"mac":["00:11:22:33:44:55"],"osName":"Windows Server 2022",
                       "osVersion":"10.0.20348","assetCriticality":"critical"}]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.HostsCreated);

        await using var db = OpenContext();
        var host = db.Hosts.Single();

        Assert.Equal("db-prod-01", host.HostName);
        Assert.Equal("10.0.20348", host.OsVersion);
        Assert.Equal(5, host.Criticality);
        Assert.Equal(TrendMicroService.ProviderName, host.ExternalProvider);
        Assert.Equal("agent-1", host.ExternalId);
        Assert.Equal(7, host.EntityId);
    }

    [Fact]
    public async Task AResyncUpdatesTheSameHostRatherThanCreatingASecond()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""{"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"]}]}""");

        await _svc.SyncAsync(view.Id);
        var second = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, second.HostsCreated);
        Assert.Equal(1, second.HostsUpdated);

        await using var db = OpenContext();
        Assert.Single(db.Hosts);
    }

    [Fact]
    public async Task AnExistingHostIsMatchedByMacBeforeIp()
    {
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, HostName = "typed-by-a-person", MacAddress = "00:11:22:33:44:55",
            Ip = "10.0.0.99", Source = "manual", RegistrationDate = Now, Status = 1, EntityId = 7
        }));

        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        // Same MAC, different IP: DHCP makes IP the weakest identity of the four, so matching on it
        // first would merge two machines that happened to share a lease.
        StubApi("""
            {"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"],
                       "mac":["00:11:22:33:44:55"]}]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, result.HostsCreated);

        await using var db = OpenContext();
        var host = db.Hosts.Single();

        // The name a person typed is better data than one an agent guessed; overwriting it nightly is
        // how an integration becomes something people turn off.
        Assert.Equal("typed-by-a-person", host.HostName);
        Assert.Equal("agent-1", host.ExternalId);
    }

    [Fact]
    public async Task CriticalityIsOwnedByTheProviderAndDoesOverwrite()
    {
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, HostName = "db-prod-01", Criticality = 1, Source = "manual",
            RegistrationDate = Now, Status = 1, EntityId = 7
        }));

        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""{"items":[{"id":"agent-1","name":"db-prod-01","assetCriticality":"critical"}]}""");

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        // The asset classification the customer configured in Vision One is more current than a NetRisk
        // value nobody maintains.
        Assert.Equal(5, db.Hosts.Single().Criticality);
    }

    // --- risk scores and the entity index ---------------------------------------------------

    [Fact]
    public async Task RiskScoresAreWrittenToTheHostAndRolledIntoAWeightedEntityIndex()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false);

        StubApi(
            devices: """
                {"items":[{"id":"a1","name":"critical-server","assetCriticality":5},
                          {"id":"a2","name":"test-box-1","assetCriticality":1},
                          {"id":"a3","name":"test-box-2","assetCriticality":1}]}
                """,
            highRisk: """
                {"items":[{"id":"a1","riskScore":90},{"id":"a2","riskScore":10},
                          {"id":"a3","riskScore":10}]}
                """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(3, result.PostureRowsWritten);

        // (90*5 + 10*1 + 10*1) / (5+1+1) = 67.14, not the unweighted 36.7 — a critical server at 90
        // should not be averaged away by two test machines.
        Assert.Equal(67.14, result.CyberRiskIndex!.Value, 2);

        await using var db = OpenContext();

        Assert.Equal(90, db.Hosts.Single(h => h.ExternalId == "a1").RiskScore);
        Assert.Equal(TrendMicroService.ProviderName, db.Hosts.Single(h => h.ExternalId == "a1").RiskScoreSource);

        var entity = db.Entities.Single(e => e.Id == 7);
        Assert.Equal(67.14, entity.CyberRiskIndex!.Value, 2);
        Assert.Equal(TrendMicroService.ProviderName, entity.PostureSource);
    }

    [Fact]
    public async Task RiskScoreSyncCanBeTurnedOff()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""{"items":[{"id":"a1","name":"x","riskScore":90}]}""");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Null(result.CyberRiskIndex);

        await using var db = OpenContext();
        Assert.Null(db.Hosts.Single().RiskScore);
    }

    // --- vulnerabilities --------------------------------------------------------------------

    [Fact]
    public async Task CvesBecomeFindingsThroughTheSharedIngestionPipeline()
    {
        var view = await ConnectionAsync();

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"]}]}""",
            vulnerable: """
                {"items":[{"id":"agent-1","name":"db-prod-01","vulnerabilities":[
                    {"cveId":"CVE-2026-1111","severity":"critical","cvssScore":9.8},
                    {"cveId":"CVE-2026-2222","severity":"medium","cvssScore":5.4}]}]}
                """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(2, result.FindingsCreated);
        Assert.NotNull(result.ImportId);

        await using var db = OpenContext();
        var findings = db.Vulnerabilities.ToList();

        Assert.Equal(2, findings.Count);

        var critical = findings.Single(f => f.Cves!.Contains("CVE-2026-1111"));

        Assert.Equal(TrendMicroService.ImporterName, critical.ImportSource);
        // Going through the ingestion pipeline is what gives these findings the dedup identity and the
        // SLA due date a Nessus import gets.
        Assert.Equal("agent-1:CVE-2026-1111", critical.ToolUniqueId);
        Assert.NotNull(critical.SlaDueDate);
        Assert.NotNull(critical.DedupKey);
    }

    [Fact]
    public async Task ARerunUpdatesTheSameFindingsRatherThanDuplicatingThem()
    {
        var view = await ConnectionAsync();

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """
                {"items":[{"id":"agent-1","vulnerabilities":[{"cveId":"CVE-2026-1111","severity":"high"}]}]}
                """);

        await _svc.SyncAsync(view.Id);
        var second = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, second.FindingsCreated);

        await using var db = OpenContext();
        Assert.Single(db.Vulnerabilities);
    }

    [Fact]
    public async Task AVirtualPatchIsRecordedInTheEvidenceButDoesNotCloseTheFindingByDefault()
    {
        var view = await ConnectionAsync(virtualPatchCloses: false);

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """
                {"items":[{"id":"agent-1","vulnerabilities":[
                    {"cveId":"CVE-2026-1111","severity":"critical","virtualPatchApplied":true,
                     "virtualPatchRuleId":"1011234"}]}]}
                """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, result.VirtualPatchesApplied);

        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();

        // The underlying software is still vulnerable; closing the finding by default would quietly hide
        // unpatched software.
        Assert.Equal(FindingStatus.Active, finding.LifecycleStatus);
        // The triager reading the finding needs to know a compensating control is in place without
        // going to Vision One to find out.
        Assert.Contains("virtual patch", finding.Details ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1011234", finding.Details ?? "");
    }

    [Fact]
    public async Task AVirtualPatchClosesTheFindingWhenTheCustomerSaidItShould()
    {
        var view = await ConnectionAsync(virtualPatchCloses: true);

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """
                {"items":[{"id":"agent-1","vulnerabilities":[
                    {"cveId":"CVE-2026-1111","severity":"critical","virtualPatchApplied":true,
                     "virtualPatchRuleId":"1011234"}]}]}
                """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.VirtualPatchesApplied);

        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();

        Assert.Equal(FindingStatus.Mitigated, finding.LifecycleStatus);

        // The IPS rule is in the audit trail, so the reason the finding closed is recoverable.
        var history = db.FindingStatusHistories.Single(h => h.ToStatus == FindingStatus.Mitigated);
        Assert.Contains("1011234", history.Justification!);
        Assert.Equal(FindingStatusChangeSource.Job, history.Source);
    }

    [Fact]
    public async Task AVirtualPatchDoesNotReopenAndCloseASuppressedFinding()
    {
        var view = await ConnectionAsync(virtualPatchCloses: true);

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """
                {"items":[{"id":"agent-1","vulnerabilities":[
                    {"cveId":"CVE-2026-1111","severity":"critical","virtualPatchApplied":true}]}]}
                """);

        await _svc.SyncAsync(view.Id);

        var lifecycle = GetService<IFindingLifecycleService>();

        await using (var db = OpenContext())
        {
            var findingId = db.Vulnerabilities.Single().Id;
            await lifecycle.TransitionAsync(findingId, FindingStatus.FalsePositive, 1,
                FindingStatusChangeSource.Manual, "Not exploitable here.");
        }

        var second = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, second.VirtualPatchesApplied);

        await using (var db = OpenContext())
            // A finding somebody marked false-positive must not be reopened and re-closed by an
            // integration.
            Assert.Equal(FindingStatus.FalsePositive, db.Vulnerabilities.Single().LifecycleStatus);
    }

    [Fact]
    public async Task VulnerabilitySyncCanBeTurnedOff()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false);

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"x"}]}""",
            vulnerable: """{"items":[{"id":"agent-1","vulnerabilities":[{"cveId":"CVE-2026-1111"}]}]}""");

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Empty(db.Vulnerabilities);
    }

    // --- sync log and scheduling ------------------------------------------------------------

    [Fact]
    public async Task EverySyncWritesALogRow()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""{"items":[{"id":"agent-1","name":"x"}]}""");

        await _svc.SyncAsync(view.Id);

        var log = Assert.Single(await _svc.GetSyncLogAsync());

        Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
        Assert.Equal(1, log.CreatedCount);
        Assert.NotNull(log.FinishedAt);
    }

    [Fact]
    public async Task AFailedSyncIsRecordedOnTheLogAndTheConnection()
    {
        var view = await ConnectionAsync();

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.Errors);

        var log = Assert.Single(await _svc.GetSyncLogAsync());
        Assert.Equal(IntegrationSyncStatus.Failed, log.Status);

        var connection = await _svc.GetConnectionAsync(view.Id);
        Assert.Equal(IntegrationSyncStatus.Failed, connection.LastSyncStatus);
        Assert.NotNull(connection.LastSyncError);
    }

    [Fact]
    public async Task OnlyConnectionsWhoseIntervalHasElapsedAreSynced()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        StubApi("""{"items":[{"id":"agent-1","name":"x"}]}""");

        await _svc.SyncAsync(view.Id);

        var immediately = await _svc.SyncDueConnectionsAsync(Now);
        Assert.Equal(0, immediately.HostsCreated + immediately.HostsUpdated);

        var tomorrow = await _svc.SyncDueConnectionsAsync(DateTime.UtcNow.AddDays(2));
        Assert.Equal(1, tomorrow.HostsUpdated);
    }

    [Fact]
    public async Task ADisabledConnectionIsNeverSyncedOnSchedule()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncRiskScores: false);

        await _svc.UpdateConnectionAsync(new TrendMicroConnection
        {
            Id = view.Id, Name = view.Name, Region = "eu", BaseUrl = "", Enabled = false,
            SyncIntervalHours = 24
        }, null);

        StubApi("""{"items":[{"id":"agent-1","name":"x"}]}""");

        var result = await _svc.SyncDueConnectionsAsync(DateTime.UtcNow.AddDays(2));

        Assert.Equal(0, result.HostsCreated);
    }

    // --- exemption write-back ---------------------------------------------------------------

    [Fact]
    public async Task AnExemptionIsNotPushedUnlessAConnectionAskedForIt()
    {
        var view = await ConnectionAsync();

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """{"items":[{"id":"agent-1","vulnerabilities":[{"cveId":"CVE-2026-1111"}]}]}""");

        await _svc.SyncAsync(view.Id);

        int findingId;
        await using (var db = OpenContext()) findingId = db.Vulnerabilities.First().Id;

        // Writing into somebody's EDR console is not something an integration should start doing on its
        // own, so the default is off and the caller is told rather than left to assume.
        Assert.False(await _svc.PushExemptionAsync(findingId, "Accepted by the CISO."));
    }

    [Fact]
    public async Task AnExemptionIsPushedWhenTheConnectionOptedIn()
    {
        var view = await _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "Acme", Region = "eu", BaseUrl = "", EntityId = 7, Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = true, SyncRiskScores = false,
            PushExemptions = true
        }, "api-key");

        StubApi(
            devices: """{"items":[{"id":"agent-1","name":"db-prod-01"}]}""",
            vulnerable: """{"items":[{"id":"agent-1","vulnerabilities":[{"cveId":"CVE-2026-1111"}]}]}""");

        await _svc.SyncAsync(view.Id);

        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices/update", "{}");

        int findingId;
        await using (var db = OpenContext()) findingId = db.Vulnerabilities.First().Id;

        Assert.True(await _svc.PushExemptionAsync(findingId, "Accepted by the CISO."));
    }

    [Fact]
    public async Task AnExemptionForAnUnknownFindingIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.PushExemptionAsync(404, "reason"));
    }

    // --- severity mapping -------------------------------------------------------------------

    [Theory]
    [InlineData("critical", null, NormalizedSeverity.Critical)]
    [InlineData("HIGH", null, NormalizedSeverity.High)]
    [InlineData("moderate", null, NormalizedSeverity.Medium)]
    [InlineData(null, 9.5, NormalizedSeverity.Critical)]
    [InlineData(null, 7.2, NormalizedSeverity.High)]
    [InlineData(null, 4.1, NormalizedSeverity.Medium)]
    [InlineData(null, 1.0, NormalizedSeverity.Low)]
    [InlineData(null, null, NormalizedSeverity.Medium)]
    public void SeverityPrefersTheWordAndFallsBackToCvss(string? word, double? cvss,
        NormalizedSeverity expected)
    {
        // Vision One omits the severity word on some ASRM payloads but rarely the score.
        Assert.Equal(expected, TrendMicroService.MapSeverity(new TrendMicroDeviceVulnerability
        {
            CveId = "CVE-1", Severity = word, CvssScore = cvss
        }));
    }
}
