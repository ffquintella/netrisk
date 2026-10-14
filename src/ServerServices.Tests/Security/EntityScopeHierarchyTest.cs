using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Exceptions;
using DAL.Interfaces;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Attribution;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// T292 (S39 §10.4, S40 §11.3, §12.2): entity scope is hierarchical. A claim on an entity grants the entity and
/// its descendants in <c>entities.parent</c> — never its ancestors, never its siblings, never an entity linked to
/// it only through an EAV property — for every read filter and for the write guard.
///
/// Every test acts as a principal through the production <see cref="DalService"/> (<see cref="PrincipalDalService"/>
/// swaps only the provider), so the scope under test is the one <see cref="DalService.GetCurrentEntityScope"/>
/// derives from the claims, exactly as for a request. The tree:
/// <code>
///   Org 1 ─┬─ Unit X 10 ── Sub X 11 ── Process 12        (process 12 is X's grandchild)
///          └─ Unit Y 20 ─┬─ Sub Y 21                    (Y is X's sibling)
///                        └─ Process P 30                (P lists X in its organizationUnit EAV property)
/// </code>
/// </summary>
[TestSubject(typeof(DalService))]
public class EntityScopeHierarchyTest : InMemoryServiceTestBase
{
    private const int Org = 1, UnitX = 10, SubX = 11, ProcessUnderSubX = 12, UnitY = 20, SubY = 21, EavProcess = 30;

    private static readonly int[] AllEntities = [Org, UnitX, SubX, ProcessUnderSubX, UnitY, SubY, EavProcess];

    // Hands the service the base constructor builds back to this one; base and derived constructors run on one thread.
    [ThreadStatic] private static PrincipalDalService? _building;

    private readonly PrincipalDalService _dal;

    public EntityScopeHierarchyTest() : base(name => _building = new PrincipalDalService(name, new FixedHttpContextAccessor()))
    {
        _dal = _building!;

        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        db.Entities.AddRange(
            NewEntity(Org, "organization", null),
            NewEntity(UnitX, "organizationUnit", Org),
            NewEntity(SubX, "subOrganizationUnit", UnitX),
            NewEntity(ProcessUnderSubX, "businessProcess", SubX),
            NewEntity(UnitY, "organizationUnit", Org),
            NewEntity(SubY, "subOrganizationUnit", UnitY),
            NewEntity(EavProcess, "businessProcess", UnitY));

        // The EAV-only link: P names Unit X as its organization unit, but is not X's child in the tree.
        db.EntitiesProperties.Add(new EntitiesProperty
        {
            Entity = EavProcess, Type = "organizationUnit", Name = "organizationUnit", Value = UnitX.ToString(),
            OldValue = ""
        });
        db.SaveChanges();
    }

    private static Entity NewEntity(int id, string definition, int? parent) => new()
    {
        Id = id, DefinitionName = definition, DefinitionVersion = "2.6", Status = "active", Parent = parent,
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow
    };

    private static Claim EntityClaim(int id) => new("entity_id", id.ToString());

    private void PlantEverywhere(Type type)
    {
        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        var catalogue = new ScopedRecordCatalogue(db);
        foreach (var entity in AllEntities) catalogue.Plant(type, entity);
        db.SaveChanges();
        _planted = catalogue;
    }

    private ScopedRecordCatalogue? _planted;

    private IReadOnlySet<int> VisibleAs(Type type, params int[] claims)
    {
        _dal.ActAs(claims.Select(EntityClaim).ToArray());
        using var db = _dal.GetContext();
        return ScopedRecordCatalogue.VisibleEntities(db, type, _planted!);
    }

    public static TheoryData<string> EveryScopedType() => new(ScopedRecordCatalogue.AllTypes.Select(t => t.Name));

    public static TheoryData<string> EveryGuardedType() =>
        new(ScopedRecordCatalogue.DirectTypes.Where(t => typeof(IEntityScoped).IsAssignableFrom(t)).Select(t => t.Name));

    // --- reads, for every scoped record type -------------------------------------------------------------------

    /// <summary>A claim on Unit X sees X, its child and its grandchild — and not the organization above it, the
    /// sibling unit, the sibling's child, or the process tied to X only by an EAV property.</summary>
    [Theory]
    [MemberData(nameof(EveryScopedType))]
    public void TestAClaimOnAUnitGrantsItsDescendantsAndNothingElse(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);
        PlantEverywhere(type);

        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, VisibleAs(type, UnitX).Order());
    }

    /// <summary>A claim on Sub X sees Sub X and its child — never Unit X, its parent, nor anything under Y.</summary>
    [Theory]
    [MemberData(nameof(EveryScopedType))]
    public void TestAClaimOnASubunitGrantsNeitherItsParentNorItsSiblings(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);
        PlantEverywhere(type);

        Assert.Equal(new[] { SubX, ProcessUnderSubX }, VisibleAs(type, SubX).Order());
    }

    /// <summary>Two claims grant the union of both subtrees, with nothing in between.</summary>
    [Theory]
    [MemberData(nameof(EveryScopedType))]
    public void TestTwoClaimsGrantTheUnionOfTheirSubtrees(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);
        PlantEverywhere(type);

        Assert.Equal(new[] { SubX, ProcessUnderSubX, SubY }, VisibleAs(type, SubX, SubY).Order());
    }

    /// <summary>Global scope (administrators and the job host, T237) still reads every row.</summary>
    [Theory]
    [MemberData(nameof(EveryScopedType))]
    public void TestGlobalScopeStillReadsEverything(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);
        PlantEverywhere(type);

        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        Assert.Equal(AllEntities.Order(), ScopedRecordCatalogue.VisibleEntities(db, type, _planted!).Order());
    }

    /// <summary>A principal with no entity claim still reads nothing: the tree never turns an empty scope into one.</summary>
    [Theory]
    [MemberData(nameof(EveryScopedType))]
    public void TestNoClaimIsStillDenyAll(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);
        PlantEverywhere(type);

        Assert.Empty(VisibleAs(type));
    }

    /// <summary>The list above is the model's list: a new filtered type must be added to it, and so to these tests.</summary>
    [Fact]
    public void TestEveryFilteredTypeIsCovered()
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        Assert.Empty(ScopedRecordCatalogue.Uncatalogued(db.Model));
    }

    // --- the write guard, for every guarded record type ---------------------------------------------------------

    /// <summary>
    /// A claim on Unit X writes into its grandchild, and is refused the organization above it, the sibling unit and
    /// the EAV-linked process — on insert and on re-stamping a visible row out of the subtree.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryGuardedType))]
    public void TestTheWriteGuardFollowsTheSameTree(string typeName)
    {
        var type = ScopedRecordCatalogue.Resolve(typeName);

        foreach (var allowed in new[] { UnitX, SubX, ProcessUnderSubX })
        {
            _dal.ActAs(EntityClaim(UnitX));
            using var db = _dal.GetContext();
            new ScopedRecordCatalogue(db).Plant(type, allowed);
            db.SaveChanges();
        }

        foreach (var refused in new[] { Org, UnitY, SubY, EavProcess })
        {
            _dal.ActAs(EntityClaim(UnitX));
            using var db = _dal.GetContext();
            new ScopedRecordCatalogue(db).Plant(type, refused);

            var ex = Assert.Throws<EntityScopeViolationException>(() => db.SaveChanges());
            Assert.Equal(refused, ex.EntityId);
        }

        // Re-stamping: the row filed under Sub X, moved to the organization above X.
        PlantEverywhere(type);
        _dal.ActAs(EntityClaim(UnitX));
        using (var db = _dal.GetContext())
        {
            var key = _planted!.EntityOfRow.Single(kv => kv.Key.Item1 == type && kv.Value == SubX).Key.Item2;
            var row = LoadByKey(db, type, key);
            ((IEntityScoped)row).EntityId = Org;
            Assert.Throws<EntityScopeViolationException>(() => db.SaveChanges());
        }
    }

    /// <summary>A caller with one claim still gets an unassigned new record filed under that claim, not under some
    /// descendant, even though the claim now covers several entities.</summary>
    [Fact]
    public void TestASingleClaimStillFilesUnassignedRecordsUnderTheClaim()
    {
        _dal.ActAs(EntityClaim(UnitX));
        using var db = _dal.GetContext();

        var scope = db.EntityScope;
        Assert.Equal(new[] { UnitX }, scope.ClaimedEntityIds);
        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, scope.EntityIds.Order());

        var risk = new Risk
        {
            Status = "New", Subject = "auto-filed", ReferenceId = "R-auto", Assessment = "", Notes = "",
            RiskCatalogMapping = "", ThreatCatalogMapping = ""
        };
        db.Risks.Add(risk);
        db.SaveChanges();

        Assert.Equal(UnitX, risk.EntityId);
    }

    private static object LoadByKey(AuditableContext db, Type type, string key)
    {
        var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
            .MakeGenericMethod(type).Invoke(db, null)!;
        var probe = new ScopedRecordCatalogue(db);
        return set.ToList().Single(r => probe.KeyString(r) == key);
    }

    // --- the services that read through the scope -------------------------------------------------------------

    private void PlantRegisters()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        var catalogue = new ScopedRecordCatalogue(db);
        foreach (var entity in AllEntities)
        {
            foreach (var type in new[] { typeof(Risk), typeof(Host), typeof(Incident), typeof(Vulnerability) })
                catalogue.Plant(type, entity);
        }

        db.SaveChanges();
        _planted = catalogue;
    }

    private int[] EntitiesOf(IEnumerable<object> rows, Type type)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        var probe = new ScopedRecordCatalogue(db);
        return rows.Select(r => _planted!.EntityOfRow[(type, probe.KeyString(r))]).Order().ToArray();
    }

    [Fact]
    public async Task TestTheRegisterServicesListTheSubtree()
    {
        PlantRegisters();
        _dal.ActAs(EntityClaim(UnitX));
        int[] expected = [UnitX, SubX, ProcessUnderSubX];

        Assert.Equal(expected, EntitiesOf(await GetService<IRisksService>().GetAllAsync(notStatus: null), typeof(Risk)));
        Assert.Equal(expected, EntitiesOf(GetService<IHostsService>().GetAll(), typeof(Host)));
        Assert.Equal(expected, EntitiesOf(await GetService<IIncidentsService>().GetAllAsync(), typeof(Incident)));
        Assert.Equal(expected, EntitiesOf(GetService<IVulnerabilitiesService>().GetAll(), typeof(Vulnerability)));
    }

    /// <summary>
    /// The one query that also narrows by an explicit principal (<c>ApplyEntityScope</c>) must expand it the same
    /// way, or it would cut the subtree back to the claim.
    /// </summary>
    [Fact]
    public async Task TestTheExplicitPrincipalFilterIsHierarchicalToo()
    {
        PlantRegisters();
        _dal.ActAs(EntityClaim(UnitX));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "scoped"), EntityClaim(SubX)], "Bearer"));
        var risks = await GetService<IRisksService>().GetAllAsync(notStatus: null, userPrincipal: principal);

        Assert.Equal(new[] { SubX, ProcessUnderSubX }, EntitiesOf(risks, typeof(Risk)));
    }

    /// <summary>Scenario attribution (T240) runs under the caller's scope, so it attributes the subtree's risks.</summary>
    [Fact]
    public async Task TestScenarioAttributionRespectsTheHierarchy()
    {
        PlantRegisters();
        _dal.ActAs(EntityClaim(UnitX));

        await using var db = _dal.GetContext();
        var attributed = await ScenarioAttribution.Pairs(db).Select(p => p.EntityId).Distinct().ToListAsync();

        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, attributed.Order());
    }

    // --- the tree changing under a live session ----------------------------------------------------------------

    /// <summary>
    /// Moving Sub Y under Sub X grants it to X's users on their next request, and moving Sub X under Y takes it away
    /// — with the same claims, no new login. The cached tree is invalidated by the save that re-parents.
    /// </summary>
    [Fact]
    public void TestAReparentMovesAccessWithoutANewLogin()
    {
        PlantEverywhere(typeof(Risk));
        Assert.DoesNotContain(SubY, VisibleAs(typeof(Risk), UnitX));

        Reparent(SubY, SubX);
        Assert.Contains(SubY, VisibleAs(typeof(Risk), UnitX));

        Reparent(SubX, UnitY);
        Assert.Equal(new[] { UnitX }, VisibleAs(typeof(Risk), UnitX).Order());
    }

    private void Reparent(int entity, int newParent)
    {
        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        db.Entities.Single(e => e.Id == entity).Parent = newParent;
        db.SaveChanges();
    }

    /// <summary>A parent cycle neither hangs the expansion nor widens it: each member grants only itself.</summary>
    [Fact]
    public void TestAParentCycleDoesNotWidenTheScope()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            db.Entities.AddRange(NewEntity(40, "organizationUnit", 41), NewEntity(41, "organizationUnit", 40),
                NewEntity(42, "organizationUnit", 42), NewEntity(43, "subOrganizationUnit", 40));
            db.SaveChanges();
        }

        _dal.ActAs(EntityClaim(40));
        Assert.Equal(new[] { 40 }, _dal.GetCurrentEntityScope().EntityIds);

        _dal.ActAs(EntityClaim(42));
        Assert.Equal(new[] { 42 }, _dal.GetCurrentEntityScope().EntityIds);

        _dal.ActAs(EntityClaim(43));
        Assert.Equal(new[] { 43 }, _dal.GetCurrentEntityScope().EntityIds);

        // The well-formed part of the tree is unaffected.
        _dal.ActAs(EntityClaim(UnitX));
        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, _dal.GetCurrentEntityScope().EntityIds.Order());
    }

    /// <summary>The Admin role and <c>scope=global</c> stay unrestricted whatever entity claims ride along.</summary>
    [Fact]
    public void TestAdminAndGlobalStayUnrestricted()
    {
        _dal.ActAs(new Claim("scope", "global"), EntityClaim(SubX));
        Assert.True(_dal.GetCurrentEntityScope().IsUnrestricted);

        _dal.ActAs(new Claim(ClaimTypes.Role, "Admin"), EntityClaim(SubX));
        Assert.True(_dal.GetCurrentEntityScope().IsUnrestricted);
    }
}
