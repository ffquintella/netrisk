using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M58, T281–T282 (S39 D-14, D-18, D-20): the Vision One inventory gives a matched host the connection's target entity
/// under the connection's re-stamp policy and never over a manual assignment, and its CVEs land on the device's own host
/// by agent id — not on another machine that holds its IP — without stamping the scan-only assessment date.
/// </summary>
[TestSubject(typeof(TrendMicroService))]
public class TrendMicroEntityRestampTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Medicine = 9;
    private static readonly DateTime Then = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly ITrendMicroService _svc;

    public TrendMicroEntityRestampTest()
    {
        _svc = GetService<ITrendMicroService>();
        Seed(ctx =>
        {
            foreach (var id in new[] { Acme, Medicine })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
            ctx.Users.Add(new User
            {
                Value = 1, Name = "admin", Login = "admin", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "admin@example.invalid"
            });
        });
    }

    private Task<TrendMicroConnectionView> ConnectionAsync(RestampPolicy policy, bool syncVulnerabilities = false) =>
        _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "Acme Vision One", Region = "eu", BaseUrl = "", EntityId = Acme, Enabled = true, SyncIntervalHours = 24,
            SyncVulnerabilities = syncVulnerabilities, SyncRiskScores = false, EntityRestampPolicy = policy
        }, "api-key");

    private static Host Known(int id, string name, int? entity, string? source) => new()
    {
        Id = id, HostName = name, Source = "manual", RegistrationDate = Then, Status = 1, EntityId = entity,
        EntitySource = source
    };

    private static string Device(string id, string name) => $$"""{"id":"{{id}}","name":"{{name}}"}""";

    [Fact]
    public async Task TestTheConnectionsPolicyIsStoredAndReturned()
    {
        var view = await ConnectionAsync(RestampPolicy.FollowMapping);

        Assert.Equal(RestampPolicy.FollowMapping, view.EntityRestampPolicy);
        Assert.Equal(RestampPolicy.FollowMapping, (await _svc.GetConnectionAsync(view.Id)).EntityRestampPolicy);
    }

    /// <summary>The defect: a host the inventory matched kept a null entity, whatever the connection's target.</summary>
    [Fact]
    public async Task TestAMatchedHostWithNoEntityTakesTheConnectionsTarget()
    {
        var view = await ConnectionAsync(RestampPolicy.OnlyUnassigned);
        Seed(ctx =>
        {
            ctx.Hosts.Add(Known(1, "free-01", null, null));
            ctx.Hosts.Add(Known(2, "elsewhere-01", Medicine, null));
        });
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices",
            "{\"items\":[" + Device("agent-1", "free-01") + "," + Device("agent-2", "elsewhere-01") + "]}");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((1, 0), (result.HostsAssignedByConnection, result.HostsReassignedByConnection));
        await using var db = OpenContext();
        var free = db.Hosts.Single(h => h.Id == 1);
        Assert.Equal(((int?)Acme, EntitySources.VisionOne), (free.EntityId, free.EntitySource));
        Assert.Equal(Medicine, db.Hosts.Single(h => h.Id == 2).EntityId);
    }

    [Fact]
    public async Task TestFollowMappingMovesWhatItOutranksAndNeverAManualAssignment()
    {
        var view = await ConnectionAsync(RestampPolicy.FollowMapping);
        Seed(ctx =>
        {
            ctx.Hosts.Add(Known(1, "unknown-01", Medicine, null));
            ctx.Hosts.Add(Known(2, "manual-01", Medicine, EntitySources.Manual));
            ctx.Hosts.Add(Known(3, "cmdb-01", Medicine, EntitySources.Cmdb));
        });
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", "{\"items\":[" + Device("agent-1", "unknown-01") +
                                                                        "," + Device("agent-2", "manual-01") + "," +
                                                                        Device("agent-3", "cmdb-01") + "]}");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((0, 1), (result.HostsAssignedByConnection, result.HostsReassignedByConnection));
        await using var db = OpenContext();
        Assert.Equal([Acme, Medicine, Medicine], db.Hosts.OrderBy(h => h.Id).Select(h => h.EntityId!.Value).ToList());
        Assert.Equal(EntitySources.Manual, db.Hosts.Single(h => h.Id == 2).EntitySource);

        var move = Assert.Single(db.AuditLogs.Where(a => a.EntityType == nameof(Host) && a.Field == nameof(Host.EntityId))
            .ToList());
        Assert.Equal((1, Medicine.ToString(), Acme.ToString()), (move.EntityId, move.OldValue, move.NewValue));
    }

    [Fact]
    public async Task TestAFollowMappingTagRuleNoLongerMovesAManualAssignment()
    {
        var view = await ConnectionAsync(RestampPolicy.OnlyUnassigned);
        Seed(ctx =>
        {
            ctx.Hosts.Add(Known(1, "manual-01", Medicine, EntitySources.Manual));
            ctx.VendorEntityMappings.Add(new VendorEntityMapping
            {
                Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = view.Id, MatchKind = VendorMatchKind.V1CustomTag,
                MatchValue = "BU: Law", EntityId = Acme, Priority = 1, RestampPolicy = RestampPolicy.FollowMapping,
                Enabled = true, CreatedAt = Then
            });
        });
        await _svc.UpdateConnectionAsync(new TrendMicroConnection
        {
            Id = view.Id, Name = view.Name, Region = "eu", BaseUrl = "", EntityId = Acme, Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = false, SyncRiskScores = false, SyncCustomTags = true
        }, null);
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices",
            """{"items":[{"id":"agent-1","name":"manual-01","assetCustomTags":[{"id":"t1","key":"BU","value":"Law"}]}]}""");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, result.HostsReassignedByRule);
        await using var db = OpenContext();
        Assert.Equal(Medicine, db.Hosts.Single().EntityId);
    }

    /// <summary>
    /// The defect (D-14): the CVE's host was looked up by IP first. The device moved to an address a printer holds;
    /// its CVE was filed on the printer. It now lands on the host the inventory knows by the agent id, and — being
    /// Vision One's own detection feed — does not stamp the scan-only assessment date (D-18).
    /// </summary>
    [Fact]
    public async Task TestACveLandsOnTheDevicesHostNotOnTheMachineHoldingItsIp()
    {
        var view = await ConnectionAsync(RestampPolicy.OnlyUnassigned, syncVulnerabilities: true);
        Seed(ctx =>
        {
            ctx.SlaConfigurations.Add(new SlaConfiguration
            {
                Severity = 4, MaxTriageDays = 2, MaxRemediationDays = 15, EffectiveFrom = Then, CreatedAt = Then
            });
            ctx.Hosts.Add(new Host { Id = 1, HostName = "printer", Ip = "10.0.0.5", Source = "manual", Status = 1, RegistrationDate = Then });
            ctx.Hosts.Add(new Host
            {
                Id = 2, HostName = "db-prod-01", Ip = "10.0.0.99", Source = TrendMicroService.ProviderName, Status = 1,
                RegistrationDate = Then, ExternalProvider = TrendMicroService.ProviderName, ExternalId = "agent-1",
                EntityId = Acme
            });
        });
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices",
            """{"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"]}]}""");
        FakeOutboundHttpClient.RuleFor("/asrm/vulnerableDevices",
            """{"items":[{"id":"agent-1","name":"db-prod-01","cveRecords":[{"id":"CVE-2026-1111","severity":"critical","cvssScore":9.8}]}]}""");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.FindingsCreated);
        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Single().HostId);
        Assert.All(db.Hosts.ToList(), h => Assert.Null(h.LastAssessedAt));
    }
}
