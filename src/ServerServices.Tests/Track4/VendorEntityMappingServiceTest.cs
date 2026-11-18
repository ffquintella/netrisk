using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// T263 (S40 §6.6, §8.6, §12.2 <c>VendorEntityMappingServiceTest</c>): the rules' list, candidates, writes and preview —
/// a duplicate is refused case-insensitively, every write needs global scope, the candidates are the connection's own
/// tags through the host filter, and the preview puts each matched host in exactly one bucket.
/// </summary>
[TestSubject(typeof(VendorEntityMappingService))]
public class VendorEntityMappingServiceTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Law = 8, Medicine = 9, Connection = 1, OtherConnection = 2;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly IVendorEntityMappingService _svc;

    public VendorEntityMappingServiceTest()
    {
        _svc = GetService<IVendorEntityMappingService>();
        Seed(ctx =>
        {
            foreach (var (id, name) in new[] { (Acme, "Acme"), (Law, "Law School"), (Medicine, "Medicine") })
            {
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
                ctx.EntitiesProperties.Add(new EntitiesProperty
                {
                    Entity = id, Type = "name", Name = "name", Value = name, OldValue = ""
                });
            }

            foreach (var id in new[] { Connection, OtherConnection })
                ctx.TrendMicroConnections.Add(new TrendMicroConnection
                {
                    Id = id, Name = "V1-" + id, Region = "us", BaseUrl = "https://api.xdr.trendmicro.com", EntityId = Acme,
                    CreatedAt = Now
                });

            // Four hosts of connection 1 carry "BU: Law": unassigned, already Law, elsewhere (Medicine), and one that a
            // second tag also claims; one host of connection 2 carries it too and must not count.
            AddHost(ctx, 1, null, Connection, "BU: Law");
            AddHost(ctx, 2, Law, Connection, "BU: Law");
            AddHost(ctx, 3, Medicine, Connection, "BU: Law");
            AddHost(ctx, 4, Acme, Connection, "BU: Law", "Site: Rio");
            AddHost(ctx, 5, null, Connection, "Site: Rio");
            AddHost(ctx, 6, null, OtherConnection, "BU: Law");
        });
    }

    private static void AddHost(DAL.Context.AuditableContext ctx, int id, int? entity, int connection, params string[] tags)
    {
        ctx.Hosts.Add(new Host
        {
            Id = id, HostName = "host-" + id, Source = "manual", RegistrationDate = Now, Status = 1, EntityId = entity
        });
        ctx.HostExternalIds.Add(new HostExternalId
        {
            Id = id, HostId = id, Provider = TrendMicroService.ProviderName, ConnectionId = connection, ExternalId = "agent-" + id,
            Tags = System.Text.Json.JsonSerializer.Serialize(tags), FirstSeenAt = Now, LastSeenAt = Now, CreatedAt = Now
        });
    }

    private static VendorEntityMappingUpsert Rule(string value, int entity = Law, int priority = 1,
        RestampPolicy policy = RestampPolicy.OnlyUnassigned, VendorMatchKind kind = VendorMatchKind.V1CustomTag,
        int connection = Connection, int provider = (int)IntegrationKind.TrendMicroVisionOne) =>
        new(provider, connection, kind, value, null, entity, priority, policy, true);

    private static async Task<string> CodeOf(Func<Task> action) =>
        (await Assert.ThrowsAsync<VendorEntityMappingException>(action)).Code;

    [Fact]
    public async Task TestACreatedRuleIsListedInCanonicalFormWithItsEntityName()
    {
        var created = await _svc.CreateAsync(Rule("  bu:Law "), userId: 0);

        var listed = Assert.Single(await _svc.ListAsync((int)IntegrationKind.TrendMicroVisionOne, Connection));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("bu: Law", listed.Value);
        Assert.Equal("Law School", listed.EntityName);
        Assert.Empty(await _svc.ListAsync((int)IntegrationKind.TrendMicroVisionOne, OtherConnection));
    }

    [Fact]
    public async Task TestCreatingARuleIsAudited()
    {
        await _svc.CreateAsync(Rule("BU: Law"), userId: 0);

        await using var db = OpenContext();
        Assert.Contains(db.AuditLogs.ToList(), a => a.EntityType == nameof(VendorEntityMapping));
    }

    [Fact]
    public async Task TestTheSameValueTwiceOnAConnectionIsRefusedWhateverItsCase()
    {
        await _svc.CreateAsync(Rule("BU: Law"), userId: 0);

        Assert.Equal(VendorEntityMappingErrors.Duplicate, await CodeOf(() => _svc.CreateAsync(Rule("bu:law", Medicine), 0)));
        // The same value is a different rule on another connection, and as another kind.
        await _svc.CreateAsync(Rule("BU: Law", connection: OtherConnection), 0);
        await _svc.CreateAsync(Rule("BU: Law", kind: VendorMatchKind.V1AssetGroup), 0);
    }

    [Fact]
    public async Task TestAnUpdateMayKeepItsOwnValueButNotTakeAnothers()
    {
        var law = await _svc.CreateAsync(Rule("BU: Law"), 0);
        await _svc.CreateAsync(Rule("Site: Rio", priority: 2), 0);

        var updated = await _svc.UpdateAsync(law.Id, Rule("BU: Law", Medicine, 3, RestampPolicy.FollowMapping));
        Assert.Equal(Medicine, updated.EntityId);
        Assert.Equal(RestampPolicy.FollowMapping, updated.Policy);

        Assert.Equal(VendorEntityMappingErrors.Duplicate, await CodeOf(() => _svc.UpdateAsync(law.Id, Rule("site: rio"))));
        Assert.Equal(VendorEntityMappingErrors.NotFound, await CodeOf(() => _svc.UpdateAsync(999, Rule("x"))));
    }

    [Theory]
    [InlineData("   ", Law, 1, 1, VendorMatchKind.V1CustomTag, Connection, 2, VendorEntityMappingErrors.ValueRequired)]
    [InlineData("BU: Law", 999, 1, 1, VendorMatchKind.V1CustomTag, Connection, 2, VendorEntityMappingErrors.EntityNotFound)]
    [InlineData("BU: Law", Law, 0, 1, VendorMatchKind.V1CustomTag, Connection, 2, VendorEntityMappingErrors.InvalidPriority)]
    [InlineData("BU: Law", Law, 10000, 1, VendorMatchKind.V1CustomTag, Connection, 2, VendorEntityMappingErrors.InvalidPriority)]
    [InlineData("BU: Law", Law, 1, 9, VendorMatchKind.V1CustomTag, Connection, 2, VendorEntityMappingErrors.InvalidPolicy)]
    [InlineData("BU: Law", Law, 1, 1, VendorMatchKind.TenableTag, Connection, 2, VendorEntityMappingErrors.InvalidKindForProvider)]
    [InlineData("BU: Law", Law, 1, 1, VendorMatchKind.V1CustomTag, 99, 2, VendorEntityMappingErrors.ConnectionNotFound)]
    [InlineData("BU: Law", Law, 1, 1, VendorMatchKind.TenableTag, Connection, 4, VendorEntityMappingErrors.InvalidProvider)]
    [InlineData("BU: Law", Law, 1, 1, VendorMatchKind.V1CustomTag, Connection, 7, VendorEntityMappingErrors.InvalidKindForProvider)]
    [InlineData("BU: Law", Law, 1, 1, VendorMatchKind.TenableTag, Connection, 7, VendorEntityMappingErrors.ConnectionNotFound)]
    public async Task TestAnInvalidRuleIsRefusedWithItsCode(string value, int entity, int priority, int policy,
        VendorMatchKind kind, int connection, int provider, string code)
    {
        var request = new VendorEntityMappingUpsert(provider, connection, kind, value, null, entity, priority,
            (RestampPolicy)policy, true);

        var thrown = await Assert.ThrowsAsync<VendorEntityMappingException>(() => _svc.CreateAsync(request, 0));

        Assert.Equal(code, thrown.Code);
        Assert.Equal(400, thrown.Status);
    }

    [Fact]
    public async Task TestAValueLongerThanTheColumnIsRefused()
    {
        Assert.Equal(VendorEntityMappingErrors.ValueTooLong,
            await CodeOf(() => _svc.CreateAsync(Rule(new string('x', VendorTagText.MaxLength + 1)), 0)));
    }

    [Fact]
    public async Task TestAScopedHolderCanNeitherWriteNorPreview()
    {
        var law = await _svc.CreateAsync(Rule("BU: Law"), 0);
        ScopeTo(Law);

        foreach (var write in new Func<Task>[]
                 {
                     () => _svc.CreateAsync(Rule("Site: Rio"), 0), () => _svc.UpdateAsync(law.Id, Rule("BU: Law")),
                     () => _svc.DeleteAsync(law.Id), () => _svc.PreviewAsync(Rule("BU: Law"), null)
                 })
        {
            var thrown = await Assert.ThrowsAsync<VendorEntityMappingException>(write);
            Assert.Equal(403, thrown.Status);
            Assert.Equal(VendorEntityMappingErrors.RequiresGlobalScope, thrown.Code);
        }

        // Reading is the controller's configuration permission alone.
        Assert.Single(await _svc.ListAsync((int)IntegrationKind.TrendMicroVisionOne, Connection));
    }

    [Fact]
    public async Task TestDeletingARuleLeavesItsHostsWhereTheyAre()
    {
        var law = await _svc.CreateAsync(Rule("BU: Law"), 0);

        await _svc.DeleteAsync(law.Id);

        Assert.Empty(await _svc.ListAsync((int)IntegrationKind.TrendMicroVisionOne, Connection));
        await using var db = OpenContext();
        Assert.Equal(Law, db.Hosts.Single(h => h.Id == 2).EntityId);
        Assert.Equal(VendorEntityMappingErrors.NotFound, await CodeOf(() => _svc.DeleteAsync(law.Id)));
    }

    [Fact]
    public async Task TestTheCandidatesAreTheConnectionsOwnTagsWithTheirCounts()
    {
        var candidates = await _svc.GetCandidatesAsync((int)IntegrationKind.TrendMicroVisionOne, Connection);

        Assert.Equal(["BU: Law", "Site: Rio"], candidates.Select(c => c.Value));
        Assert.Equal([4, 2], candidates.Select(c => c.ObjectsSeen));
        Assert.All(candidates, c => Assert.Equal(VendorMatchKind.V1CustomTag, c.Kind));
        Assert.Empty(await _svc.GetCandidatesAsync((int)IntegrationKind.SecurityScorecard, Connection));
    }

    /// <summary>M57, T271: once the posture sync has read the asset groups, a Vision One connection offers them too.</summary>
    [Fact]
    public async Task TestTheSyncedAssetGroupsAreOfferedAsCandidates()
    {
        Seed(ctx =>
        {
            ctx.VendorScores.Add(Group(Connection, "g1", "{\"name\":\"Finance\",\"assetCount\":40}", Now.AddDays(-1)));
            ctx.VendorScores.Add(Group(Connection, "g1", "{\"name\":\"Finance & Ops\",\"assetCount\":42}", Now));
            ctx.VendorScores.Add(Group(OtherConnection, "g9", "{\"name\":\"Other\"}", Now));
        });

        var candidates = await _svc.GetCandidatesAsync((int)IntegrationKind.TrendMicroVisionOne, Connection);

        var group = Assert.Single(candidates, c => c.Kind == VendorMatchKind.V1AssetGroup);
        Assert.Equal(("g1", "Finance & Ops", 42), (group.Value, group.Label, group.ObjectsSeen));
        Assert.Equal(2, candidates.Count(c => c.Kind == VendorMatchKind.V1CustomTag));
    }

    private static VendorScore Group(int connection, string id, string details, DateTime at) => new()
    {
        Source = VendorScoreSource.V1AssetGroup, ConnectionId = connection, ObjectKind = VendorScoreObjectKind.AssetGroup,
        ExternalRef = id, EntityId = Acme, Value = 50, ScaleMax = 100, Details = details, FirstCapturedAt = at,
        LastCapturedAt = at, CreatedAt = at
    };

    [Fact]
    public async Task TestAScopedReaderIsNotOfferedTagsSeenOnlyOutsideTheirScope()
    {
        ScopeTo(Medicine);

        var candidates = await _svc.GetCandidatesAsync((int)IntegrationKind.TrendMicroVisionOne, Connection);

        var only = Assert.Single(candidates);
        Assert.Equal("BU: Law", only.Value);
        Assert.Equal(1, only.ObjectsSeen);
    }

    [Fact]
    public async Task TestThePreviewPutsEachMatchedHostInOneBucket()
    {
        // Rule 1 (Site: Rio → Acme, priority 1) wins host 4; the previewed rule matches hosts 1–4 of connection 1.
        await _svc.CreateAsync(Rule("Site: Rio", Acme, 1), 0);

        var only = await _svc.PreviewAsync(Rule("BU: Law", Law, 5), null);
        Assert.Equal((4, 1, 0, 1, 2), (only.Matched, only.WouldAssign, only.WouldReassign, only.Conflicts, only.AlreadyAssigned));

        var follow = await _svc.PreviewAsync(Rule("BU: Law", Law, 5, RestampPolicy.FollowMapping), null);
        Assert.Equal((4, 1, 1, 1, 1),
            (follow.Matched, follow.WouldAssign, follow.WouldReassign, follow.Conflicts, follow.AlreadyAssigned));
        Assert.Equal(4, follow.Sample.Count);
        var moved = Assert.Single(follow.Sample, s => s.OutcomeKey == VendorMappingOutcomes.Reassign);
        Assert.Equal(("host-3", "Medicine", "Law School"), (moved.ObjectLabel, moved.CurrentEntity, moved.NewEntity));
        Assert.Contains(follow.Sample, s => s is { ObjectLabel: "host-4", OutcomeKey: VendorMappingOutcomes.Conflict });
    }

    [Fact]
    public async Task TestThePreviewOfAnEditedRuleReplacesItRatherThanCompetingWithIt()
    {
        var site = await _svc.CreateAsync(Rule("Site: Rio", Acme, 1), 0);

        // The edited rule is the request: lowering Site: Rio's priority below a saved BU: Law rule is a conflict on host 4,
        // never a conflict of the rule with its own saved copy.
        await _svc.CreateAsync(Rule("BU: Law", Law, 2), 0);
        var preview = await _svc.PreviewAsync(Rule("Site: Rio", Acme, 9), site.Id);

        Assert.Equal((2, 1, 0, 1, 0),
            (preview.Matched, preview.WouldAssign, preview.WouldReassign, preview.Conflicts, preview.AlreadyAssigned));
    }

    [Fact]
    public async Task TestAnAssetGroupPreviewAttributesNothing()
    {
        var preview = await _svc.PreviewAsync(Rule("BU: Law", kind: VendorMatchKind.V1AssetGroup), null);

        Assert.Equal(0, preview.Matched);
        Assert.Empty(preview.Sample);
    }
}
