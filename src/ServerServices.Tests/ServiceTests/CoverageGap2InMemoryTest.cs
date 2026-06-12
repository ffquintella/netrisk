using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using Sieve.Models;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class VulnerabilitiesGap2InMemoryTest : InMemoryServiceTestBase
{
    private readonly IVulnerabilitiesService _svc;
    public VulnerabilitiesGap2InMemoryTest() => _svc = GetService<IVulnerabilitiesService>();

    private static Vulnerability NewVuln(int id, string title = "V") => new()
    {
        Id = id, Title = title, Status = 1,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 2), DetectionCount = 1
    };

    [Fact]
    public async Task TestUpdateAsync()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1, "Before")));

        await _svc.UpdateAsync(NewVuln(1, "After"));

        Assert.Equal("After", _svc.GetById(1).Title);
        // invalid inputs are swallowed (logged) by UpdateAsync, so no throw expected
        await _svc.UpdateAsync(NewVuln(0));
    }

    [Fact]
    public void TestAddActionSync()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1)));

        var action = _svc.AddAction(1, 5, new NrAction { ObjectType = "vulnerability", DateTime = new DateTime(2026, 1, 1) });

        Assert.Equal(5, action.UserId);
    }

    [Fact]
    public void TestGetFiltredWithoutFixRequests()
    {
        Seed(ctx =>
        {
            ctx.Vulnerabilities.Add(NewVuln(1));
            ctx.Vulnerabilities.Add(NewVuln(2));
        });

        var list = _svc.GetFiltred(new SieveModel(), out var total, includeFixRequests: false);

        Assert.Equal(2, total);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task TestGetByIdWithDetails()
    {
        Seed(ctx => ctx.Vulnerabilities.Add(NewVuln(1)));

        var vuln = await _svc.GetByIdAsync(1, includeDetails: true);

        Assert.Equal(1, vuln.Id);
    }
}

public class EntitiesGap2InMemoryTest : InMemoryServiceTestBase
{
    private readonly IEntitiesService _svc;
    public EntitiesGap2InMemoryTest() => _svc = GetService<IEntitiesService>();

    [Fact]
    public void TestValidatePropertyListTypedValues()
    {
        // organization has String(name), Boolean(isMainOrganization), Integer(numberOfEmployees)
        var props = new List<EntitiesPropertyDto>
        {
            new() { Type = "name", Value = "Org", Name = "name" },
            new() { Type = "isMainOrganization", Value = "true", Name = "isMainOrganization" },
            new() { Type = "numberOfEmployees", Value = "42", Name = "numberOfEmployees" }
        };

        _svc.ValidatePropertyList("organization", props);   // should not throw
    }

    [Fact]
    public void TestValidatePropertyListInvalidValuesThrow()
    {
        Assert.Throws<Exception>(() => _svc.ValidatePropertyList("organization", new List<EntitiesPropertyDto>
        {
            new() { Type = "name", Value = "Org", Name = "name" },
            new() { Type = "isMainOrganization", Value = "notbool", Name = "isMainOrganization" }
        }));

        Assert.Throws<Exception>(() => _svc.ValidatePropertyList("organization", new List<EntitiesPropertyDto>
        {
            new() { Type = "name", Value = "Org", Name = "name" },
            new() { Type = "isMainOrganization", Value = "true", Name = "isMainOrganization" },
            new() { Type = "numberOfEmployees", Value = "notint", Name = "numberOfEmployees" }
        }));
    }

    [Fact]
    public void TestUpdateProperty()
    {
        var entity = _svc.CreateInstance(1, "person");
        var created = _svc.CreateProperty("person", ref entity, new EntitiesPropertyDto { Type = "name", Value = "Before", Name = "name" });

        var dto = new EntitiesPropertyDto { Id = created.Id, Type = "name", Value = "After", Name = "name" };
        var updated = _svc.UpdateProperty(ref entity, dto);

        Assert.Equal("After", updated.Value);
        Assert.Throws<DataNotFoundException>(() =>
        {
            var e = entity;
            _svc.UpdateProperty(ref e, new EntitiesPropertyDto { Id = 9999, Type = "name", Value = "x", Name = "name" });
        });
    }

    [Fact]
    public void TestGetEntitiesWithPropertyLoad()
    {
        var entity = _svc.CreateInstance(1, "person");
        _svc.CreateProperty("person", ref entity, new EntitiesPropertyDto { Type = "name", Value = "Zoe", Name = "name" });

        var all = _svc.GetEntities(propertyLoad: true);
        var byDef = _svc.GetEntities("person", propertyLoad: true);

        Assert.Single(all);
        Assert.Single(byDef);
    }

    [Fact]
    public void TestGetConfigurationCachedSecondCall()
    {
        var first = _svc.GetEntitiesConfigurationAsync().Result;
        var second = _svc.GetEntitiesConfigurationAsync().Result;   // cached branch

        Assert.Same(first, second);
    }
}

public class ClientRegistrationGap2InMemoryTest : InMemoryServiceTestBase
{
    private readonly IClientRegistrationService _svc;
    public ClientRegistrationGap2InMemoryTest() => _svc = GetService<IClientRegistrationService>();

    [Fact]
    public void TestGetRequestedEmpty()
    {
        Assert.Empty(_svc.GetRequested());
    }

    [Fact]
    public void TestRejectAndDeleteNotFound()
    {
        Assert.Equal(1, _svc.Reject(999));
        Assert.Equal(1, _svc.DeleteById(999));
        Assert.Equal(-1, _svc.IsAccepted("nobody"));
    }

    [Fact]
    public async Task TestFindApprovedMissing()
    {
        Assert.Null(await _svc.FindApprovedRegistrationAsync("nope"));
        Assert.Null(_svc.GetRegistrationById(999));
    }
}

public class RisksGap2InMemoryTest : InMemoryServiceTestBase
{
    private readonly IRisksService _svc;
    public RisksGap2InMemoryTest() => _svc = GetService<IRisksService>();

    private static Risk NewRisk(int id, string status = "Open") => new()
    {
        Id = id, Status = status, Subject = $"R{id}", ReferenceId = "R", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", Source = 1, Category = 1, Owner = 1, Manager = 1
    };

    [Fact]
    public async Task TestCreateRiskAsyncMissingReferences()
    {
        // No Source/Category seeded
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.CreateRiskAsync(NewRisk(0)));

        Seed(ctx => ctx.Sources.Add(new Source { Value = 1, Name = "S" }));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.CreateRiskAsync(NewRisk(0))); // category missing
    }

    [Fact]
    public void TestGetUserRisksWithModifyPermission()
    {
        Seed(ctx =>
        {
            ctx.Sources.Add(new Source { Value = 1, Name = "S" });
            ctx.Categories.Add(new Category { Value = 1, Name = "C" });
            var role = new Role { Value = 1, Name = "R" };
            role.Permissions.Add(new Permission { Id = 1, Key = "modify_risks", Name = "Modify", Description = "" });
            ctx.Roles.Add(role);
            ctx.Users.Add(new User { Value = 7, Name = "U", Type = "local", Enabled = true, RoleId = 1, Email = "u@x.io", Password = new byte[] { 1 } });
            ctx.Risks.Add(NewRisk(1, "Open"));
            ctx.Risks.Add(NewRisk(2, "Open"));
        });

        // user with modify_risks permission sees all risks (via GetAllAsync)
        var risks = _svc.GetUserRisks(new User { Value = 7, Admin = false, RoleId = 1 }, null, "Closed");

        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public void TestGetRiskCatalogsByIdsAndSubjectExistsFalse()
    {
        Seed(ctx =>
        {
            ctx.RiskCatalogs.Add(new RiskCatalog { Id = 1, Number = "1", Name = "A", Description = "d" });
            ctx.RiskCatalogs.Add(new RiskCatalog { Id = 2, Number = "2", Name = "B", Description = "d" });
        });

        Assert.Equal(2, _svc.GetRiskCatalogs(new List<int> { 1, 2 }).Count);
        Assert.False(_svc.SubjectExists("nonexistent"));
    }
}
