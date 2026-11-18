using System;
using System.Linq;
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
/// T263 (S40 §6.6, §6.9, §8.8): the Vision One sync applies the connection's tag → entity rules — a rule beats the default
/// entity for a new host, "only unassigned" never moves an attributed host, "follow the mapping" does and the Host audit
/// records it — keeps each device's tags in <c>host_external_ids</c>, and does neither while custom tags are off.
/// </summary>
[TestSubject(typeof(TrendMicroService))]
public class TrendMicroTagRulesSyncTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Law = 8, Medicine = 9;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly ITrendMicroService _svc;

    public TrendMicroTagRulesSyncTest()
    {
        _svc = GetService<ITrendMicroService>();
        Seed(ctx =>
        {
            foreach (var id in new[] { Acme, Law, Medicine })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
        });
    }

    private Task<TrendMicroConnectionView> ConnectionAsync(bool syncCustomTags = true, string name = "Acme Vision One") =>
        _svc.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = name, Region = "eu", BaseUrl = "", EntityId = Acme, Enabled = true, SyncIntervalHours = 24,
            SyncVulnerabilities = false, SyncRiskScores = false, SyncCustomTags = syncCustomTags
        }, "api-key");

    private void AddRule(int connectionId, string value, int entity, int priority = 1,
        RestampPolicy policy = RestampPolicy.OnlyUnassigned, bool enabled = true) =>
        Seed(ctx => ctx.VendorEntityMappings.Add(new VendorEntityMapping
        {
            Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = connectionId, MatchKind = VendorMatchKind.V1CustomTag,
            MatchValue = value, EntityId = entity, Priority = priority, RestampPolicy = policy, Enabled = enabled, CreatedAt = Now
        }));

    /// <summary>Replaces the inventory answer: the fake answers with its first matching rule, so a resync needs a reset.</summary>
    private void StubDevices(string items)
    {
        FakeOutboundHttpClient.Reset();
        FakeOutboundHttpClient.RuleFor("/asrm/attackSurfaceDevices", "{\"items\":[" + items + "]}");
    }

    private static string Device(string id, string name, string tagKey, string tagValue) =>
        $$"""{"id":"{{id}}","name":"{{name}}","assetCustomTags":[{"id":"t-{{id}}","key":"{{tagKey}}","value":"{{tagValue}}"}]}""";

    [Fact]
    public async Task TestANewDeviceIsCreatedUnderTheRulesEntityNotTheDefault()
    {
        var view = await ConnectionAsync();
        AddRule(view.Id, "BU: Law", Law);
        StubDevices(Device("agent-1", "law-01", "bu", "law") + "," + Device("agent-2", "misc-01", "BU", "Other"));

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((2, 1, 0, 2), (result.HostsCreated, result.HostsAssignedByRule, result.HostsReassignedByRule,
            result.TagsRecorded));
        await using var db = OpenContext();
        Assert.Equal(Law, db.Hosts.Single(h => h.HostName == "law-01").EntityId);
        Assert.Equal(Acme, db.Hosts.Single(h => h.HostName == "misc-01").EntityId);

        var identity = db.HostExternalIds.Single(x => x.ExternalId == "agent-1");
        Assert.Equal((TrendMicroService.ProviderName, view.Id, "[\"bu: law\"]"),
            (identity.Provider, identity.ConnectionId, identity.Tags));

        var log = db.IntegrationSyncLogs.Single(l => l.ConnectionId == view.Id);
        Assert.Contains("1 host(s) attributed and 0 reattributed by rule", log.Summary);
    }

    [Fact]
    public async Task TestTheRestampPolicyDecidesWhichAttributedHostsMove()
    {
        var view = await ConnectionAsync();
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host { Id = 1, HostName = "free-01", Source = "manual", RegistrationDate = Now, Status = 1 });
            ctx.Hosts.Add(new Host
            {
                Id = 2, HostName = "kept-01", Source = "manual", RegistrationDate = Now, Status = 1, EntityId = Medicine
            });
            ctx.Hosts.Add(new Host
            {
                Id = 3, HostName = "moved-01", Source = "manual", RegistrationDate = Now, Status = 1, EntityId = Medicine
            });
        });
        AddRule(view.Id, "BU: Law", Law, 1);
        AddRule(view.Id, "BU: Follow", Law, 2, RestampPolicy.FollowMapping);
        StubDevices(Device("agent-1", "free-01", "BU", "Law") + "," + Device("agent-2", "kept-01", "BU", "Law") + "," +
                    Device("agent-3", "moved-01", "BU", "Follow"));

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((3, 1, 1), (result.HostsUpdated, result.HostsAssignedByRule, result.HostsReassignedByRule));
        await using var db = OpenContext();
        Assert.Equal([Law, Medicine, Law], db.Hosts.OrderBy(h => h.Id).Select(h => h.EntityId).ToList());

        var move = Assert.Single(db.AuditLogs.Where(a => a.EntityType == nameof(Host) && a.EntityId == 3 &&
                                                         a.Field == nameof(Host.EntityId)).ToList());
        Assert.Equal((Medicine.ToString(), Law.ToString(), TrendMicroService.ImportAuditActor),
            (move.OldValue, move.NewValue, move.Actor));
    }

    [Fact]
    public async Task TestTheLowestPriorityRuleWinsADeviceTwoRulesMatch()
    {
        var view = await ConnectionAsync();
        AddRule(view.Id, "BU: Law", Law, 5);
        AddRule(view.Id, "Site: Rio", Medicine, 2);
        StubDevices("""{"id":"agent-1","name":"both-01","assetCustomTags":[{"key":"BU","value":"Law"},"Site: Rio"]}""");

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Equal(Medicine, db.Hosts.Single().EntityId);
    }

    [Fact]
    public async Task TestADisabledRuleIsNotApplied()
    {
        var view = await ConnectionAsync();
        AddRule(view.Id, "BU: Law", Law, enabled: false);
        StubDevices(Device("agent-1", "law-01", "BU", "Law"));

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, result.HostsAssignedByRule);
        await using var db = OpenContext();
        Assert.Equal(Acme, db.Hosts.Single().EntityId);
    }

    [Fact]
    public async Task TestWithCustomTagsOffTheRulesAreInertAndSaySo()
    {
        var view = await ConnectionAsync(syncCustomTags: false);
        AddRule(view.Id, "BU: Law", Law);
        StubDevices(Device("agent-1", "law-01", "BU", "Law"));

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal((0, 0), (result.HostsAssignedByRule, result.TagsRecorded));
        await using var db = OpenContext();
        Assert.Equal(Acme, db.Hosts.Single().EntityId);
        // M57, T275: the device's identity and detection times are recorded whatever the option; its tags are not.
        Assert.Null(Assert.Single(db.HostExternalIds.ToList()).Tags);
        Assert.Contains("Skipped: 1 tag rule(s) are not applied", db.IntegrationSyncLogs.Single().ProgressLog);
    }

    [Fact]
    public async Task TestAResyncRewritesTheDevicesTagsInPlace()
    {
        var view = await ConnectionAsync();
        StubDevices(Device("agent-1", "law-01", "BU", "Law"));
        await _svc.SyncAsync(view.Id);

        StubDevices("""{"id":"agent-1","name":"law-01"}""");
        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        var identity = Assert.Single(db.HostExternalIds.ToList());
        Assert.Null(identity.Tags);
        Assert.NotNull(identity.UpdatedAt);
    }

    [Fact]
    public async Task TestDeletingAConnectionTakesOnlyItsOwnRules()
    {
        var first = await ConnectionAsync();
        var second = await ConnectionAsync(name: "Second tenant");
        AddRule(first.Id, "BU: Law", Law);
        AddRule(second.Id, "BU: Law", Law);

        await _svc.DeleteConnectionAsync(first.Id);

        await using var db = OpenContext();
        var left = Assert.Single(db.VendorEntityMappings.ToList());
        Assert.Equal(second.Id, left.ConnectionId);
    }

    [Fact]
    public async Task TestTheConnectionCarriesTheCustomTagsOption()
    {
        var view = await ConnectionAsync();
        Assert.True(view.SyncCustomTags);

        var stored = new TrendMicroConnection
        {
            Id = view.Id, Name = view.Name, Region = view.Region, BaseUrl = view.BaseUrl, EntityId = Acme, Enabled = true,
            SyncIntervalHours = 24, SyncCustomTags = false
        };
        Assert.False((await _svc.UpdateConnectionAsync(stored, null)).SyncCustomTags);
        Assert.False((await _svc.GetConnectionAsync(view.Id)).SyncCustomTags);
    }
}
