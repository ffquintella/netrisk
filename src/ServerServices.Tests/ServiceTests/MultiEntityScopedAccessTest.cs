using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Entities;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class MultiEntityScopedAccessTest : InMemoryServiceTestBase
{
    private readonly IRisksService _risksService;
    private readonly IDalService _dalService;

    public MultiEntityScopedAccessTest()
    {
        _risksService = GetService<IRisksService>();
        _dalService = GetService<IDalService>();
    }

    [Fact]
    public async Task TestEntityScopedDataAccessEnforcement()
    {
        // 1. Setup Seed Data
        Seed(ctx =>
        {
            // Seed Users
            var userA = new User { Value = 10, Name = "UserA", Email = "a@netrisk.io", Type = "Internal", Enabled = true, Password = new byte[16] };
            var userB = new User { Value = 20, Name = "UserB", Email = "b@netrisk.io", Type = "Internal", Enabled = true, Password = new byte[16] };
            var userAdmin = new User { Value = 30, Name = "Admin", Email = "admin@netrisk.io", Type = "Internal", Enabled = true, Password = new byte[16], Admin = true };
            ctx.Users.AddRange(userA, userB, userAdmin);

            // Seed Business Entities
            var entity1 = new Entity { Id = 100, DefinitionName = "EntityOne", DefinitionVersion = "1", Created = DateTime.UtcNow, Updated = DateTime.UtcNow, Status = "Active" };
            var entity2 = new Entity { Id = 200, DefinitionName = "EntityTwo", DefinitionVersion = "1", Created = DateTime.UtcNow, Updated = DateTime.UtcNow, Status = "Active" };
            ctx.Entities.AddRange(entity1, entity2);

            // Seed Roles
            var role = new Role { Value = 1, Name = "Analyst" };
            ctx.Roles.Add(role);

            // Seed User-Entity-Role assignments
            var assignmentA = new UserEntityRole { Id = 1, UserId = 10, EntityId = 100, RoleId = 1, CreatedAt = DateTime.UtcNow };
            var assignmentB = new UserEntityRole { Id = 2, UserId = 20, EntityId = 200, RoleId = 1, CreatedAt = DateTime.UtcNow };
            ctx.UserEntityRoles.AddRange(assignmentA, assignmentB);

            // Seed Risks with different EntityIds
            var risk1 = new Risk
            {
                Id = 1001,
                Subject = "Risk in Entity One",
                Status = "New",
                ReferenceId = "R1",
                EntityId = 100,
                Assessment = "a",
                Notes = "n",
                RiskCatalogMapping = "m",
                ThreatCatalogMapping = "t",
                TemplateGroupId = 1
            };
            var risk2 = new Risk
            {
                Id = 1002,
                Subject = "Risk in Entity Two",
                Status = "New",
                ReferenceId = "R2",
                EntityId = 200,
                Assessment = "a",
                Notes = "n",
                RiskCatalogMapping = "m",
                ThreatCatalogMapping = "t",
                TemplateGroupId = 1
            };
            ctx.Risks.AddRange(risk1, risk2);
        });

        // 2. Build claims principal for User A (Entity 1)
        var identityA = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "UserA"),
            new Claim(ClaimTypes.Sid, "10"),
            new Claim("entity_id", "100")
        }, "TestAuth");
        var principalA = new ClaimsPrincipal(identityA);

        // Retrieve data for User A
        var risksA = await _risksService.GetAllAsync(userPrincipal: principalA);
        Assert.Single(risksA);
        Assert.Equal(1001, risksA[0].Id);
        Assert.Equal("Risk in Entity One", risksA[0].Subject);

        // 3. Build claims principal for User B (Entity 2)
        var identityB = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "UserB"),
            new Claim(ClaimTypes.Sid, "20"),
            new Claim("entity_id", "200")
        }, "TestAuth");
        var principalB = new ClaimsPrincipal(identityB);

        // Retrieve data for User B
        var risksB = await _risksService.GetAllAsync(userPrincipal: principalB);
        Assert.Single(risksB);
        Assert.Equal(1002, risksB[0].Id);
        Assert.Equal("Risk in Entity Two", risksB[0].Subject);

        // 4. Build claims principal for Global Admin (sees all!)
        var identityAdmin = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Admin"),
            new Claim(ClaimTypes.Sid, "30"),
            new Claim("scope", "global")
        }, "TestAuth");
        var principalAdmin = new ClaimsPrincipal(identityAdmin);

        // Retrieve data for Global Admin
        var risksAdmin = await _risksService.GetAllAsync(userPrincipal: principalAdmin);
        Assert.Equal(2, risksAdmin.Count);
        Assert.Contains(risksAdmin, r => r.Id == 1001);
        Assert.Contains(risksAdmin, r => r.Id == 1002);
    }
}
