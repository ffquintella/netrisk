using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using ServerServices.Interfaces;
using ServerServices.Services;
using Serilog;
using Xunit;
using ILogger = Serilog.ILogger;

namespace API.Tests.Security;

/// <summary>
/// T292 and T241 observed through the controllers, under a scoped principal, on the production
/// <see cref="DalService"/> (only the provider is in-memory) and the real <see cref="EntitiesService"/> and
/// <see cref="RisksService"/>. Unit tests of the expansion cannot see a controller that reads around it, or a write
/// path that changes the tree before anyone checks — so these go in at the action and look at what reached the store.
/// <code>
///   Org 1 ─┬─ Unit X 10 ── Sub X 11 ── Process 12 (criticality 3)
///          └─ Unit Y 20 ─┬─ Sub Y 21
///                        └─ Process P 30 (organizationUnit = X, EAV only)
/// </code>
/// </summary>
[TestSubject(typeof(EntitiesController))]
public class EntityHierarchyEndToEndTest : APITests.BaseControllerTest
{
    private const int Org = 1, UnitX = 10, SubX = 11, ProcessUnderSubX = 12, UnitY = 20, SubY = 21, EavProcess = 30;
    private static readonly int[] AllEntities = [Org, UnitX, SubX, ProcessUnderSubX, UnitY, SubY, EavProcess];

    private readonly FixedAccessor _accessor = new();
    private readonly InMemoryPrincipalDal _dal;

    public EntityHierarchyEndToEndTest()
    {
        _dal = new InMemoryPrincipalDal(Guid.NewGuid().ToString(), _accessor);

        ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        db.Entities.AddRange(
            NewEntity(Org, "organization", null), NewEntity(UnitX, "organizationUnit", Org),
            NewEntity(SubX, "subOrganizationUnit", UnitX), NewEntity(ProcessUnderSubX, "businessProcess", SubX),
            NewEntity(UnitY, "organizationUnit", Org), NewEntity(SubY, "subOrganizationUnit", UnitY),
            NewEntity(EavProcess, "businessProcess", UnitY));

        // Each with the properties its definition requires, so a PUT echoing them back validates.
        db.EntitiesProperties.Add(Property(Org, "isMainOrganization", "true"));
        db.EntitiesProperties.Add(Property(UnitX, "mainOrganization", Org.ToString()));
        db.EntitiesProperties.Add(Property(UnitY, "mainOrganization", Org.ToString()));
        db.EntitiesProperties.Add(Property(SubX, "parentOrganizationUnit", UnitX.ToString()));
        db.EntitiesProperties.Add(Property(SubY, "parentOrganizationUnit", UnitY.ToString()));
        foreach (var process in new[] { ProcessUnderSubX, EavProcess })
        {
            db.EntitiesProperties.Add(Property(process, "description", "d"));
            db.EntitiesProperties.Add(Property(process, "objective", "o"));
            db.EntitiesProperties.Add(Property(process, "isActive", "true"));
        }

        // The process under Sub X names Unit X as its unit (it may; the tree is what grants). Process P names Unit X
        // too, but sits under Y in the tree: the EAV-only link.
        db.EntitiesProperties.Add(Property(ProcessUnderSubX, "organizationUnit", UnitX.ToString()));

        foreach (var id in AllEntities)
        {
            db.EntitiesProperties.Add(Property(id, "name", $"entity {id}"));
            db.Risks.Add(new Risk
            {
                Id = 100 + id, Status = "New", Subject = $"risk of {id}", ReferenceId = $"R{id}", Assessment = "",
                Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", EntityId = id,
                SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow
            });
        }

        db.EntitiesProperties.Add(Property(ProcessUnderSubX, RiskContextProperties.Criticality, "3"));
        db.EntitiesProperties.Add(Property(EavProcess, "organizationUnit", UnitX.ToString()));
        db.SaveChanges();
    }

    private static Entity NewEntity(int id, string definition, int? parent) => new()
    {
        Id = id, DefinitionName = definition, DefinitionVersion = "2.6", Status = "active", Parent = parent,
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow, CreatedBy = 1, UpdatedBy = 1
    };

    private static EntitiesProperty Property(int entity, string type, string value) => new()
    {
        Entity = entity, Type = type, Name = $"{type}-{entity}", Value = value, OldValue = ""
    };

    private void ActAs(params Claim[] claims)
    {
        // "testUser" is the account the shared users mock resolves; Sid is the user id the DAL attributes audit rows to.
        var all = new List<Claim> { new(ClaimTypes.Name, "testUser"), new(ClaimTypes.Sid, "1") };
        all.AddRange(claims);
        _accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(all, "Bearer")) };
    }

    private static Claim EntityClaim(int id) => new("entity_id", id.ToString());
    private static Claim Permission(string key) => new("Permission", key);

    private T Controller<T>() where T : notnull =>
        ResolveController<T>(s =>
        {
            s.AddSingleton<IHttpContextAccessor>(_accessor);
            s.AddSingleton<IDalService>(_dal);
            s.AddTransient<IEntitiesService>(sp => new EntitiesService(sp.GetRequiredService<ILogger>(), _dal));
            // The real register service over the same DAL; its collaborators play no part in listing.
            s.AddTransient<IRisksService>(sp => new RisksService(_dal,
                NSubstitute.Substitute.For<IRolesService>(),
                NSubstitute.Substitute.For<ServerServices.Filtering.IEntityFilterMapperProvider>(),
                sp.GetRequiredService<IUsersService>(),
                NSubstitute.Substitute.For<INotificationEventPublisher>(),
                NSubstitute.Substitute.For<IRiskWorkflowService>()));
        });

    private EntityDto Body(int id, Action<EntityDto>? change = null)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        var entity = db.Entities.AsNoTracking().Include(e => e.EntitiesProperties).Single(e => e.Id == id);
        var dto = new EntityDto
        {
            Id = id, DefinitionName = entity.DefinitionName, Status = entity.Status, Parent = entity.Parent,
            EntitiesProperties = entity.EntitiesProperties
                .Select(p => new EntitiesPropertyDto { Id = p.Id, Type = p.Type, Name = p.Name, Value = p.Value }).ToList()
        };
        change?.Invoke(dto);
        return dto;
    }

    private static void SetProperty(EntityDto dto, string type, string value)
    {
        dto.EntitiesProperties.RemoveAll(p => p.Type == type);
        dto.EntitiesProperties.Add(new EntitiesPropertyDto { Type = type, Name = $"{type}-{dto.Id}", Value = value });
    }

    private Entity Stored(int id)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.Entities.AsNoTracking().Include(e => e.EntitiesProperties).Single(e => e.Id == id);
    }

    private static string? ValueOf(Entity entity, string type) =>
        entity.EntitiesProperties.FirstOrDefault(p => p.Type == type)?.Value;

    private static void AssertRefused(ActionResult? result, int status, string error)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, refused.StatusCode);
        Assert.Equal(error, refused.Value!.GetType().GetProperty("error")!.GetValue(refused.Value));
    }

    // --- T292: reads through the controller --------------------------------------------------------------------

    [Fact]
    public async Task TestTheRiskListOfAUnitHolderIsTheUnitsSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission("riskmanagement"));

        var result = await Controller<RisksController>().GetAllAsync(includeClosed: true);

        var risks = Assert.IsType<List<Risk>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX }, risks.Select(r => r.EntityId!.Value).Order());
    }

    [Fact]
    public async Task TestTheRiskListOfASubunitHolderHasNeitherTheParentNorTheSiblings()
    {
        ActAs(EntityClaim(SubX), Permission("riskmanagement"));

        var result = await Controller<RisksController>().GetAllAsync(includeClosed: true);

        var risks = Assert.IsType<List<Risk>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { SubX, ProcessUnderSubX }, risks.Select(r => r.EntityId!.Value).Order());
    }

    // --- T241: risk-context properties --------------------------------------------------------------------------

    [Fact]
    public void TestARiskContextChangeWithoutThePermissionIsRefusedAndNothingIsWritten()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(ProcessUnderSubX,
            Body(ProcessUnderSubX, b => SetProperty(b, RiskContextProperties.Criticality, "5")));

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "risk_context_requires_permission");
        Assert.Equal("3", ValueOf(Stored(ProcessUnderSubX), RiskContextProperties.Criticality));
    }

    [Fact]
    public void TestClearingARiskContextValueIsAChangeToo()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(ProcessUnderSubX,
            Body(ProcessUnderSubX, b => b.EntitiesProperties.RemoveAll(p => p.Type == RiskContextProperties.Criticality)));

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "risk_context_requires_permission");
        Assert.Equal("3", ValueOf(Stored(ProcessUnderSubX), RiskContextProperties.Criticality));
    }

    [Fact]
    public void TestARiskContextChangeWithThePermissionButARestrictedScopeIsRefused()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage), Permission(EntityPermissions.RiskContext));

        var result = Controller<EntitiesController>().Update(ProcessUnderSubX,
            Body(ProcessUnderSubX, b => SetProperty(b, RiskContextProperties.Criticality, "5")));

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "risk_context_requires_global_scope");
        Assert.Equal("3", ValueOf(Stored(ProcessUnderSubX), RiskContextProperties.Criticality));
    }

    /// <summary>The permitted change goes through, and the field-level trail (T241) records the old and new value.</summary>
    [Fact]
    public void TestAGlobalHolderChangesRiskContextAndTheAuditTrailRecordsIt()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage), Permission(EntityPermissions.RiskContext));

        var result = Controller<EntitiesController>().Update(ProcessUnderSubX,
            Body(ProcessUnderSubX, b => SetProperty(b, RiskContextProperties.Criticality, "5")));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("5", ValueOf(Stored(ProcessUnderSubX), RiskContextProperties.Criticality));

        using var db = _dal.GetContext(bypassEntityScope: true);
        var row = db.AuditLogs.AsNoTracking().Single(a =>
            a.EntityType == nameof(EntitiesProperty) && a.Field == nameof(EntitiesProperty.Value) && a.NewValue == "5");
        Assert.Equal("3", row.OldValue);
        Assert.Equal("testUser", row.Actor);
    }

    [Fact]
    public void TestAnEditThatLeavesRiskContextAloneNeedsOnlyEntitiesManageInsideTheSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(ProcessUnderSubX,
            Body(ProcessUnderSubX, b => SetProperty(b, "name", "renamed")));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("renamed", ValueOf(Stored(ProcessUnderSubX), "name"));
        Assert.Equal("3", ValueOf(Stored(ProcessUnderSubX), RiskContextProperties.Criticality));
    }

    [Fact]
    public void TestCreatingWithARiskContextValueWithoutThePermissionIsRefusedAndNothingIsCreated()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage));
        var before = CountEntities();

        var result = Controller<EntitiesController>().Create(new EntityDto
        {
            DefinitionName = "businessProcess", Status = "active", Parent = SubX,
            EntitiesProperties =
            [
                new EntitiesPropertyDto { Type = "name", Name = "name-new", Value = "new process" },
                new EntitiesPropertyDto { Type = "description", Name = "description-new", Value = "d" },
                new EntitiesPropertyDto { Type = "objective", Name = "objective-new", Value = "o" },
                new EntitiesPropertyDto { Type = "isActive", Name = "isActive-new", Value = "true" },
                new EntitiesPropertyDto { Type = "organizationUnit", Name = "organizationUnit-new", Value = UnitX.ToString() },
                new EntitiesPropertyDto { Type = RiskContextProperties.Criticality, Name = "criticality-new", Value = "4" }
            ]
        });

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "risk_context_requires_permission");
        Assert.Equal(before, CountEntities());
    }

    private int CountEntities()
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.Entities.Count();
    }

    // --- T292 on the entity map itself: the parent link is an access grant -------------------------------------

    [Fact]
    public void TestAScopedManagerCannotPullAnotherUnitsEntityIntoTheirSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(SubY, Body(SubY, b => b.Parent = SubX));

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "entity_outside_scope");
        Assert.Equal(UnitY, Stored(SubY).Parent);
    }

    [Fact]
    public void TestAScopedManagerCannotMoveTheirEntityOutOfTheirSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(SubX, Body(SubX, b => b.Parent = UnitY));

        AssertRefused(result.Result, StatusCodes.Status403Forbidden, "entity_outside_scope");
        Assert.Equal(UnitX, Stored(SubX).Parent);
    }

    [Fact]
    public void TestARe_parentThatWouldCloseACycleIsRefused()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage));

        var result = Controller<EntitiesController>().Update(UnitX, Body(UnitX, b => b.Parent = ProcessUnderSubX));

        AssertRefused(result.Result, StatusCodes.Status400BadRequest, "entity_parent_cycle");
        Assert.Equal(Org, Stored(UnitX).Parent);
    }

    [Fact]
    public void TestAScopedManagerCreatesOnlyUnderTheirSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage));
        var controller = Controller<EntitiesController>();

        EntityDto Sub(int? parent) => new()
        {
            DefinitionName = "subOrganizationUnit", Status = "active", Parent = parent,
            EntitiesProperties =
            [
                new EntitiesPropertyDto { Type = "name", Name = "name-new", Value = "new unit" },
                new EntitiesPropertyDto { Type = "parentOrganizationUnit", Name = "parent-new", Value = "Parent" }
            ]
        };

        AssertRefused(controller.Create(Sub(UnitY)).Result, StatusCodes.Status403Forbidden, "entity_outside_scope");
        AssertRefused(controller.Create(Sub(null)).Result, StatusCodes.Status403Forbidden, "entity_root_requires_global_scope");

        var created = Assert.IsType<Entity>(Assert.IsType<OkObjectResult>(controller.Create(Sub(SubX)).Result).Value);
        Assert.Equal(SubX, Stored(created.Id).Parent);
    }

    [Fact]
    public void TestAScopedManagerDeletesOnlyInsideTheirSubtree()
    {
        ActAs(EntityClaim(UnitX), Permission(EntityPermissions.Manage));
        var controller = Controller<EntitiesController>();

        AssertRefused(controller.DeleteOne(EavProcess), StatusCodes.Status403Forbidden, "entity_outside_scope");
        AssertRefused(controller.DeleteOne(UnitY), StatusCodes.Status403Forbidden, "entity_outside_scope");
        Assert.Equal(AllEntities.Length, CountEntities());

        Assert.IsType<OkResult>(controller.DeleteOne(ProcessUnderSubX));
        Assert.Equal(AllEntities.Length - 1, CountEntities());
    }

    /// <summary>A re-parent through the API moves what the unit's holders see on their next request (T292).</summary>
    [Fact]
    public async Task TestARe_parentThroughTheApiMovesAccessWithoutANewLogin()
    {
        ActAs(new Claim("scope", "global"), Permission(EntityPermissions.Manage));
        Assert.IsType<OkObjectResult>(Controller<EntitiesController>().Update(SubY, Body(SubY, b => b.Parent = SubX)).Result);

        ActAs(EntityClaim(UnitX), Permission("riskmanagement"));
        var result = await Controller<RisksController>().GetAllAsync(includeClosed: true);

        var risks = Assert.IsType<List<Risk>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { UnitX, SubX, ProcessUnderSubX, SubY }, risks.Select(r => r.EntityId!.Value).Order());
    }

    // --- T241: the attributes -----------------------------------------------------------------------------------

    public static TheoryData<string> WriteActions() =>
        new(nameof(EntitiesController.Create), nameof(EntitiesController.Update), nameof(EntitiesController.DeleteOne));

    [Theory]
    [MemberData(nameof(WriteActions))]
    public void TestEveryWriteActionRequiresEntitiesManage(string action)
    {
        var method = typeof(EntitiesController).GetMethod(action)!;

        var permission = Assert.Single(method.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true));
        Assert.Equal(EntityPermissions.Manage, permission.Permission);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    /// <summary>The reads stay on the class's <c>RequireValidUser</c>: the map is visible to every authenticated user.</summary>
    [Fact]
    public void TestTheReadsStayOpenToEveryValidUser()
    {
        Assert.Equal("RequireValidUser",
            Assert.Single(typeof(EntitiesController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);

        var reads = typeof(EntitiesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpGetAttribute>().Any()).ToList();

        Assert.NotEmpty(reads);
        Assert.All(reads, m => Assert.Empty(m.GetCustomAttributes<AuthorizeAttribute>(inherit: true)));
    }

    // --- the test doubles ---------------------------------------------------------------------------------------

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    /// <summary>The production DAL service with the provider swapped: scope, identity and actor come from the accessor.</summary>
    private sealed class InMemoryPrincipalDal(string name, IHttpContextAccessor accessor)
        : DalService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "server=localhost;database=netrisk_tests"
        }).Build(), accessor)
    {
        protected override DbContextOptionsBuilder<NRDbContext> GetDbContextOptionsBuilder() =>
            new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(name);
    }
}
