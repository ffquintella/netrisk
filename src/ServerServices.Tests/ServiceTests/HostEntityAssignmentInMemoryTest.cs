using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Filtering;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using Xunit;
using HostsService = ServerServices.Services.HostsService;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M58, T281–T282 (S39 D-18, D-20): the hosts API's entity assignment — recorded as manual, validated, confined to the
/// caller's scope — the server-owned assessment date, the <c>entityId</c> filter and the backfill report.
/// </summary>
[TestSubject(typeof(HostsService))]
public class HostEntityAssignmentInMemoryTest : InMemoryServiceTestBase
{
    private const int Acme = 7, Law = 8;
    private static readonly DateTime Scanned = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private readonly IHostsService _svc;

    public HostEntityAssignmentInMemoryTest()
    {
        _svc = GetService<IHostsService>();
        Seed(ctx =>
        {
            foreach (var id in new[] { Acme, Law })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1", Status = "active" });
        });
    }

    private static Host NewHost(int id, int? entity = null, string? source = null) => new()
    {
        Id = id, Ip = $"10.0.0.{id}", HostName = $"h{id}", Status = 1, Source = "manual",
        RegistrationDate = new DateTime(2026, 1, 1), EntityId = entity, EntitySource = source
    };

    [Fact]
    public async Task TestAnEntitySetThroughTheApiIsRecordedAsManual()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, Acme, EntitySources.VisionOne)));

        var edit = NewHost(1, Law, EntitySources.Rule);
        await _svc.UpdateAsync(edit);

        await using var db = OpenContext();
        var host = db.Hosts.Single();
        Assert.Equal(((int?)Law, EntitySources.Manual), (host.EntityId, host.EntitySource));
    }

    [Fact]
    public void TestAnUnchangedEntityKeepsItsWriterAndAClearedOneLosesIt()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1, Acme, EntitySources.Cmdb));
            ctx.Hosts.Add(NewHost(2, Acme, EntitySources.Cmdb));
        });

        // A body that forges a writer does not get it.
        _svc.Update(NewHost(1, Acme, EntitySources.Manual));
        _svc.Update(NewHost(2, null, EntitySources.Manual));

        using var db = OpenContext();
        Assert.Equal(EntitySources.Cmdb, db.Hosts.Single(h => h.Id == 1).EntitySource);
        var cleared = db.Hosts.Single(h => h.Id == 2);
        Assert.Equal(((int?)null, (string?)null), (cleared.EntityId, cleared.EntitySource));
    }

    [Fact]
    public async Task TestACreatedHostWithAnEntityIsManualAndOneWithoutHasNoWriter()
    {
        var with = await _svc.CreateAsync(NewHost(0, Acme, EntitySources.Cmdb));
        var without = _svc.Create(NewHost(0, null, EntitySources.Cmdb));

        Assert.Equal(EntitySources.Manual, with.EntitySource);
        Assert.Null(without.EntitySource);
    }

    /// <summary>D-18: only a scan writes the assessment date — not a forged body, and not a stale dialog copy.</summary>
    [Fact]
    public async Task TestTheAssessmentDateIsNeverTakenFromTheBody()
    {
        var host = NewHost(1);
        host.LastAssessedAt = Scanned;
        Seed(ctx => ctx.Hosts.Add(host));

        var stale = NewHost(1);
        stale.LastAssessedAt = Scanned.AddDays(-30);
        await _svc.UpdateAsync(stale);

        var forged = NewHost(0);
        forged.LastAssessedAt = Scanned;
        var created = _svc.Create(forged);

        await using var db = OpenContext();
        Assert.Equal(Scanned, db.Hosts.Single(h => h.Id == 1).LastAssessedAt);
        Assert.Null(db.Hosts.Single(h => h.Id == created.Id).LastAssessedAt);
    }

    [Fact]
    public async Task TestAnEntityThatDoesNotExistIsRefused()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.UpdateAsync(NewHost(1, 999)));
        Assert.Equal(nameof(Host.EntityId), ex.ParameterName);
        Assert.Throws<InvalidParameterException>(() => _svc.Create(NewHost(0, 999)));

        await using var db = OpenContext();
        Assert.Null(db.Hosts.Single().EntityId);
    }

    /// <summary>Who may assign: a scoped caller cannot file a host under an entity outside its scope.</summary>
    [Fact]
    public async Task TestAScopedCallerCannotAssignAHostOutsideItsScope()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, Acme)));
        ScopeTo(Acme);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() => _svc.UpdateAsync(NewHost(1, Law)));
        Assert.Throws<EntityScopeViolationException>(() => _svc.Create(NewHost(0, Law)));

        ScopeToEverything();
        await using var db = OpenContext();
        Assert.Equal(Acme, db.Hosts.Single().EntityId);
    }

    /// <summary>
    /// The defect: the existence check ran before the scope check, so a scoped caller got "does not exist" for an unknown
    /// id and a scope refusal for a real one outside its scope — learning which entity ids exist. Both are now the same
    /// scope refusal.
    /// </summary>
    [Fact]
    public async Task TestAScopedCallerNamingAnUnknownEntityGetsTheScopeRefusalNotTheExistenceOne()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1, Acme)));
        ScopeTo(Acme);

        var update = await Assert.ThrowsAsync<EntityScopeViolationException>(() => _svc.UpdateAsync(NewHost(1, 999)));
        var create = Assert.Throws<EntityScopeViolationException>(() => _svc.Create(NewHost(0, 999)));
        var outOfScope = Assert.Throws<EntityScopeViolationException>(() => _svc.Create(NewHost(0, Law)));

        Assert.Equal((nameof(Host), (int?)999), (update.EntityType, update.EntityId));
        Assert.Equal(create.Message.Replace("999", "{id}"), outOfScope.Message.Replace(Law.ToString(), "{id}"));
    }

    /// <summary>
    /// The defect: a caller holding one entity claim who created a host without naming an entity had it filed under the
    /// claim by the DAL's write guard with no writer recorded, so a <c>FollowMapping</c> sync could later move a host a
    /// person created. The filing is now the caller's, recorded as manual.
    /// </summary>
    [Fact]
    public async Task TestAHostASingleClaimCallerCreatesWithoutAnEntityIsFiledUnderTheClaimAsManual()
    {
        ScopeTo(Acme);

        var created = _svc.Create(NewHost(0));
        var second = NewHost(0);
        second.Ip = "10.0.1.1";
        var createdAsync = await _svc.CreateAsync(second);

        ScopeToEverything();
        await using var db = OpenContext();
        foreach (var id in new[] { created.Id, createdAsync.Id })
        {
            var host = db.Hosts.Single(h => h.Id == id);
            Assert.Equal(((int?)Acme, EntitySources.Manual), (host.EntityId, host.EntitySource));
        }
    }

    /// <summary>A caller spanning several claims has no single entity to file under: the write guard still refuses it.</summary>
    [Fact]
    public void TestACallerWithSeveralClaimsMustNameTheEntity()
    {
        ScopeTo(Acme, Law);

        Assert.Throws<EntityScopeViolationException>(() => _svc.Create(NewHost(0)));
    }

    [Fact]
    public async Task TestTheHostsCanBeFilteredByEntityIncludingTheUnassigned()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1, Acme));
            ctx.Hosts.Add(NewHost(2, Law));
            ctx.Hosts.Add(NewHost(3));
        });

        var (acme, acmeTotal) = await _svc.GetFiltredAsync(new ListQuery { Filters = $"entityId=={Acme}" });
        var (none, noneTotal) = await _svc.GetFiltredAsync(new ListQuery { Filters = "entityId==null" });

        Assert.Equal((1, 1), (acme.Single().Id, acmeTotal));
        Assert.Equal((3, 1), (none.Single().Id, noneTotal));
    }

    [Fact]
    public async Task TestTheBackfillReportListsUnassignedHostsWithTheirConnectionsTarget()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1, Acme));
            ctx.Hosts.Add(NewHost(2));
            ctx.Hosts.Add(NewHost(3));
            ctx.Hosts.Add(NewHost(4));
            ctx.TrendMicroConnections.Add(new TrendMicroConnection
            {
                Id = 5, Name = "v1", Region = "eu", BaseUrl = "https://example.invalid", EntityId = Law
            });
            ctx.HostExternalIds.Add(new HostExternalId
            {
                HostId = 2, Provider = TrendMicroService.ProviderName, ConnectionId = 5, ExternalId = "agent-2",
                FirstSeenAt = Scanned, LastSeenAt = Scanned, CreatedAt = Scanned
            });
            ctx.IssueTrackerConnections.Add(new IssueTrackerConnection
            {
                Id = 6, Name = "cmdb", BaseUrl = "https://example.invalid", ProjectKey = "SD", EntityId = Acme,
                CreatedAt = Scanned
            });
            ctx.JiraAssetObjects.Add(new JiraAssetObject
            {
                ConnectionId = 6, ObjectId = "1042", TargetHostId = 3, FirstSeenAt = Scanned
            });
        });

        var report = await _svc.GetUnassignedAsync();

        Assert.Equal(new[] { 2, 3, 4 }, report.Select(r => r.Id));
        Assert.Equal(((int?)Law, "visionone:5"), (report[0].SuggestedEntityId, report[0].SuggestedBy));
        Assert.Equal(((int?)Acme, "jira-assets:6"), (report[1].SuggestedEntityId, report[1].SuggestedBy));
        Assert.Null(report[2].SuggestedEntityId);

        Assert.Single(await _svc.GetUnassignedAsync(limit: 1));
    }

    [Fact]
    public async Task TestAScopedCallerGetsAnEmptyBackfillReport()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));
        ScopeTo(Acme);

        Assert.Empty(await _svc.GetUnassignedAsync());
    }
}
