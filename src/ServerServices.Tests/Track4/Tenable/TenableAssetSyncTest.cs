using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;
using ServerServices.Integrations.Tenable;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>
/// M56, T267–T268 (S40 §6.6, §6.8, §9.4): the asset export lands on hosts by the identity chain (Tenable id, MAC, FQDN,
/// hostname, IP — the id before the IP), keeps ACR and AES as piecewise-constant readings only when collected, never writes
/// <c>hosts.criticality</c>, and attributes hosts by the connection's tag rules under their restamp policy.
/// </summary>
[TestSubject(typeof(TenableService))]
public class TenableAssetSyncTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Law = 8, Medicine = 9;
    private static readonly DateTime Now = new(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc);
    private readonly ITenableService _svc;

    public TenableAssetSyncTest()
    {
        _svc = GetService<ITenableService>();
        Seed(ctx =>
        {
            foreach (var id in new[] { Acme, Law, Medicine })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
        });
    }

    private async Task<int> ConnectionAsync(bool acr = true, bool aes = true)
    {
        var view = await _svc.CreateConnectionAsync(new TenableConnectionUpsert("Tenable FGV",
            TenableProduct.VulnerabilityManagement, null, Acme, true, 24, false, true, acr, aes, 1, 90, false, false,
            TenableFixtures.AccessKey, TenableFixtures.SecretKey, null, null, false, false));
        return view.Id;
    }

    private void StubAssets(params string[] assets)
    {
        FakeOutboundHttpClient.Reset();
        FakeOutboundHttpClient.RuleFor("/assets/v2/export", TenableFixtures.Started("a-exp"));
        FakeOutboundHttpClient.RuleFor("/assets/export/a-exp/status", TenableFixtures.Status("FINISHED", 1));
        FakeOutboundHttpClient.RuleFor("/assets/export/a-exp/chunks/1", TenableFixtures.Chunk(assets));
    }

    private async Task<TenableSyncResult> SyncAsync(int connectionId)
    {
        await _svc.QueueSyncAsync(connectionId);
        return await _svc.RunPendingAsync(DateTime.UtcNow);
    }

    private void AddRule(int connectionId, string value, int entity, int priority = 1,
        RestampPolicy policy = RestampPolicy.OnlyUnassigned) =>
        Seed(ctx => ctx.VendorEntityMappings.Add(new VendorEntityMapping
        {
            Provider = IntegrationKind.Tenable, ConnectionId = connectionId, MatchKind = VendorMatchKind.TenableTag, MatchValue = value,
            EntityId = entity, Priority = priority, RestampPolicy = policy, Enabled = true, CreatedAt = Now
        }));

    private static Host NewHost(int id, string name, string? ip = null, string? mac = null, string? fqdn = null,
        int? entity = null, int? criticality = null) => new()
    {
        Id = id, HostName = name, Ip = ip, MacAddress = mac, Fqdn = fqdn, EntityId = entity, Criticality = criticality,
        Source = "manual", RegistrationDate = Now, Status = 1
    };

    [Fact]
    public async Task TestTheIdentityChainMatchesTheTenableIdBeforeMacFqdnNameAndIp()
    {
        var connection = await ConnectionAsync();
        Seed(ctx =>
        {
            ctx.Hosts.AddRange(NewHost(1, "by-id"), NewHost(2, "by-mac", mac: "AA:AA:AA:AA:AA:AA"),
                NewHost(3, "by-fqdn", fqdn: "f3.corp.example"), NewHost(4, "web-04"), NewHost(5, "by-ip", ip: "10.9.9.5"));
            ctx.HostExternalIds.Add(new HostExternalId
            {
                HostId = 1, Provider = TenableService.ProviderName, ExternalId = "a-1", FirstSeenAt = Now, LastSeenAt = Now, CreatedAt = Now
            });
        });
        StubAssets(
            TenableFixtures.Asset("a-1", host: "elsewhere", ip: "10.9.9.5"), // its id wins over the IP that names host 5
            TenableFixtures.Asset("a-2", host: "x2", ip: "10.1.1.2", mac: "aa:aa:aa:aa:aa:aa"),
            TenableFixtures.Asset("a-3", host: "f3", ip: "10.1.1.3"),
            TenableFixtures.Asset("a-4", host: "web-04", ip: "10.1.1.4"),
            TenableFixtures.Asset("a-5", host: "x5", ip: "10.9.9.5"),
            TenableFixtures.Asset("a-6", host: "new-06", ip: "10.1.1.6"));

        var result = await SyncAsync(connection);

        Assert.Equal((6, 1, 5), (result.AssetsReceived, result.HostsCreated, result.HostsUpdated));
        await using var db = OpenContext();
        Assert.Equal([1, 2, 3, 4, 5],
            new[] { "a-1", "a-2", "a-3", "a-4", "a-5" }.Select(id => db.HostExternalIds.Single(x => x.ExternalId == id).HostId));
        var created = db.Hosts.Single(h => h.HostName == "new-06");
        Assert.Equal((TenableService.ProviderName, "a-6", Acme), (created.ExternalProvider, created.ExternalId, created.EntityId));
        Assert.Equal("by-id", db.Hosts.Single(h => h.Id == 1).HostName);
    }

    [Fact]
    public async Task TestAcrAndAesAreReadingsOnlyAndNeverTheHostsCriticality()
    {
        var connection = await ConnectionAsync(acr: true, aes: true);
        Seed(ctx => ctx.Hosts.Add(NewHost(1, "web-01", criticality: 2)));
        StubAssets(TenableFixtures.Asset("a-1", acr: 9, aes: 820), TenableFixtures.Asset("a-2", host: "db-02", ip: "10.0.0.6", acr: 3, aes: 100));

        var result = await SyncAsync(connection);

        Assert.Equal(4, result.ScoresWritten);
        await using var db = OpenContext();
        Assert.Equal((int?)2, db.Hosts.Single(h => h.Id == 1).Criticality);
        Assert.Null(db.Hosts.Single(h => h.HostName == "db-02").Criticality);
        var aes = db.VendorScores.Single(v => v.HostId == 1 && v.Source == VendorScoreSource.TenableAes);
        Assert.Equal((820.0, 1000.0, "a-1", (int?)null), (aes.Value, aes.ScaleMax, aes.ExternalRef, aes.EntityId));
        Assert.Contains("lastAssessedAt", aes.Details);
        Assert.Equal((9.0, 10.0), (db.VendorScores.Single(v => v.HostId == 1 && v.Source == VendorScoreSource.TenableAcr).Value,
            db.VendorScores.Single(v => v.HostId == 1 && v.Source == VendorScoreSource.TenableAcr).ScaleMax));
    }

    [Fact]
    public async Task TestARatingIsNotCollectedWhenItsOptionIsOff()
    {
        var connection = await ConnectionAsync(acr: false, aes: true);
        StubAssets(TenableFixtures.Asset("a-1", acr: 9, aes: 820));

        await SyncAsync(connection);

        await using var db = OpenContext();
        Assert.Equal([VendorScoreSource.TenableAes], db.VendorScores.Select(v => v.Source).ToList());
    }

    [Fact]
    public async Task TestAnUnchangedReadingExtendsItsRowAndAChangedOneOpensANewRow()
    {
        var connection = await ConnectionAsync(acr: false, aes: true);
        StubAssets(TenableFixtures.Asset("a-1", aes: 500));
        await SyncAsync(connection);
        DateTime firstCapture;
        await using (var db = OpenContext()) firstCapture = db.VendorScores.Single().LastCapturedAt;

        await SyncAsync(connection);
        await using (var db = OpenContext())
        {
            var row = db.VendorScores.Single();
            Assert.True(row.LastCapturedAt >= firstCapture);
            Assert.Equal(row.FirstCapturedAt, firstCapture);
        }

        StubAssets(TenableFixtures.Asset("a-1", aes: 640));
        await SyncAsync(connection);
        await using (var db = OpenContext())
            Assert.Equal([500.0, 640.0], db.VendorScores.OrderBy(v => v.Id).Select(v => v.Value).ToList());
    }

    [Fact]
    public async Task TestEmptyRatingsSayTheLicenceIsMissing()
    {
        var connection = await ConnectionAsync();
        StubAssets(TenableFixtures.Asset("a-1", acr: null, aes: null));

        var result = await SyncAsync(connection);

        Assert.Contains(result.Messages, m => m.Contains("ACR/AES missing"));
        await using var db = OpenContext();
        Assert.Empty(db.VendorScores);
        Assert.Equal(IntegrationSyncStatus.Succeeded, db.IntegrationSyncLogs.Single().Status);
    }

    [Fact]
    public async Task TestADeletedAssetIsSkipped()
    {
        var connection = await ConnectionAsync();
        StubAssets(TenableFixtures.Asset("a-1", deleted: "2026-11-01T00:00:00Z"));

        Assert.Equal(1, (await SyncAsync(connection)).AssetsReceived);
        await using var db = OpenContext();
        Assert.Empty(db.Hosts);
    }

    // --- T268: tag rules -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestARuleBeatsTheDefaultEntityForANewHostAndTheTagsAreKept()
    {
        var connection = await ConnectionAsync();
        AddRule(connection, "BU: Law", Law);
        StubAssets(TenableFixtures.Asset("a-1", host: "law-01", tags: TenableFixtures.Tag("bu", "law")),
            TenableFixtures.Asset("a-2", host: "misc-01", ip: "10.0.0.7", tags: TenableFixtures.Tag("BU", "Other")));

        var result = await SyncAsync(connection);

        Assert.Equal((2, 1, 0), (result.HostsCreated, result.HostsAssignedByRule, result.HostsReassignedByRule));
        await using var db = OpenContext();
        Assert.Equal((int?)Law, db.Hosts.Single(h => h.HostName == "law-01").EntityId);
        Assert.Equal((int?)Acme, db.Hosts.Single(h => h.HostName == "misc-01").EntityId);
        var identity = db.HostExternalIds.Single(x => x.ExternalId == "a-1");
        Assert.Equal((connection, "[\"bu: law\"]"), (identity.ConnectionId!.Value, identity.Tags));
        Assert.Contains("1 attributed and 0 reattributed by rule", db.IntegrationSyncLogs.Single().Summary);
    }

    [Fact]
    public async Task TestTheRestampPolicyDecidesWhichAttributedHostsMove()
    {
        var connection = await ConnectionAsync();
        Seed(ctx => ctx.Hosts.AddRange(NewHost(1, "free-01"), NewHost(2, "kept-01", entity: Medicine),
            NewHost(3, "moved-01", entity: Medicine)));
        AddRule(connection, "BU: Law", Law, 1);
        AddRule(connection, "BU: Follow", Law, 2, RestampPolicy.FollowMapping);
        StubAssets(TenableFixtures.Asset("a-1", host: "free-01", ip: "10.0.0.1", tags: TenableFixtures.Tag("BU", "Law")),
            TenableFixtures.Asset("a-2", host: "kept-01", ip: "10.0.0.2", tags: TenableFixtures.Tag("BU", "Law")),
            TenableFixtures.Asset("a-3", host: "moved-01", ip: "10.0.0.3", tags: TenableFixtures.Tag("BU", "Follow")));

        var result = await SyncAsync(connection);

        Assert.Equal((3, 1, 1), (result.HostsUpdated, result.HostsAssignedByRule, result.HostsReassignedByRule));
        await using var db = OpenContext();
        Assert.Equal([Law, Medicine, Law], db.Hosts.OrderBy(h => h.Id).Select(h => h.EntityId!.Value).ToList());
        var move = Assert.Single(db.AuditLogs.Where(a => a.EntityType == nameof(Host) && a.EntityId == 3 &&
                                                         a.Field == nameof(Host.EntityId)).ToList());
        Assert.Equal((Medicine.ToString(), Law.ToString(), TenableService.ImportAuditActor), (move.OldValue, move.NewValue, move.Actor));
    }

    [Fact]
    public async Task TestTenableRulesAreAcceptedWithTheirCandidatesAndPreview()
    {
        var connection = await ConnectionAsync();
        StubAssets(TenableFixtures.Asset("a-1", host: "law-01", tags: TenableFixtures.Tag("BU", "Law")),
            TenableFixtures.Asset("a-2", host: "law-02", ip: "10.0.0.8", tags: TenableFixtures.Tag("BU", "Law")));
        await SyncAsync(connection);
        var mappings = GetService<IVendorEntityMappingService>();
        var body = new VendorEntityMappingUpsert((int)IntegrationKind.Tenable, connection, VendorMatchKind.TenableTag, "bu:law",
            null, Law, 1, RestampPolicy.FollowMapping, true);

        var candidate = Assert.Single(await mappings.GetCandidatesAsync((int)IntegrationKind.Tenable, connection));
        var preview = await mappings.PreviewAsync(body, null);
        var saved = await mappings.CreateAsync(body, 0);

        Assert.Equal((VendorMatchKind.TenableTag, "BU: Law", 2), (candidate.Kind, candidate.Value, candidate.ObjectsSeen));
        Assert.Equal((2, 2), (preview.Matched, preview.WouldReassign));
        Assert.Equal(("bu: law", IntegrationKind.Tenable), (saved.Value, (IntegrationKind)saved.Provider));

        var wrongKind = await Assert.ThrowsAsync<VendorEntityMappingException>(() =>
            mappings.CreateAsync(body with { Kind = VendorMatchKind.V1CustomTag, Value = "x" }, 0));
        Assert.Equal(VendorEntityMappingErrors.InvalidKindForProvider, wrongKind.Code);
        var noConnection = await Assert.ThrowsAsync<VendorEntityMappingException>(() =>
            mappings.CreateAsync(body with { ConnectionId = 999, Value = "y" }, 0));
        Assert.Equal(VendorEntityMappingErrors.ConnectionNotFound, noConnection.Code);
    }
}
