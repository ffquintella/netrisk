using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestone 8.3.3 — the appetite list as the governance admin screen consumes it.
///
/// The point of these tests is what travels with each row, not just its numbers. An entity has no
/// <c>name</c> column — the name is a row in the <c>entities_properties</c> bag — so a list that
/// includes the entity but not the bag reaches the client with no name at all, and every grid bound
/// to it can only show the numeric id. That is exactly what the appetite grid showed.
/// </summary>
[TestSubject(typeof(RiskAppetitesService))]
public class RiskAppetitesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskAppetitesService _appetites;

    public RiskAppetitesServiceInMemoryTest()
    {
        _appetites = GetService<IRiskAppetitesService>();
    }

    private static readonly DateTime Now = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "organization", DefinitionVersion = "1",
        Created = Now, Updated = Now, CreatedBy = 1, UpdatedBy = 1, Status = "active"
    };

    private static EntitiesProperty NewName(int id, int entityId, string value) => new()
    {
        Id = id, Entity = entityId, Type = "name", Name = "name", Value = value, OldValue = ""
    };

    private static RiskAppetite NewAppetite(int id, int? entityId, double ceiling = 8, double dual = 6) => new()
    {
        Id = id, EntityId = entityId, MaxAcceptableResidual = ceiling, DualApprovalThreshold = dual,
        CreatedAt = Now, CreatedById = 1
    };

    [Fact]
    public async Task AnAppetiteRowCarriesItsEntitysName()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(3));
            ctx.EntitiesProperties.Add(NewName(1, 3, "Retail Bank"));
            ctx.RiskAppetites.Add(NewAppetite(1, entityId: 3));
        });

        var rows = await _appetites.GetAllAsync();

        var row = Assert.Single(rows);
        Assert.NotNull(row.Entity);

        // The bag itself has to be on the row: Entity.DisplayName reads it, and with the bag missing
        // it degrades to "#3" — which is the numeric id the grid was already showing.
        Assert.Equal("Retail Bank", row.Entity!.EntitiesProperties
            .FirstOrDefault(p => p.Type == "name")?.Value);
        Assert.Equal("Retail Bank", row.Entity!.DisplayName);
    }

    [Fact]
    public async Task TheOrganizationWideRowComesFirstAndCarriesNoEntity()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(3));
            ctx.Entities.Add(NewEntity(7));
            ctx.EntitiesProperties.Add(NewName(1, 3, "Retail Bank"));
            ctx.EntitiesProperties.Add(NewName(2, 7, "Asset Management"));
            ctx.RiskAppetites.Add(NewAppetite(1, entityId: 7));
            ctx.RiskAppetites.Add(NewAppetite(2, entityId: null));
            ctx.RiskAppetites.Add(NewAppetite(3, entityId: 3));
        });

        var rows = await _appetites.GetAllAsync();

        Assert.Equal(new int?[] { null, 3, 7 }, rows.Select(r => r.EntityId));

        // Null entity, so there is no name to render — the scope label is the client's to supply.
        Assert.Null(rows[0].Entity);
        Assert.Equal("Retail Bank", rows[1].Entity!.DisplayName);
        Assert.Equal("Asset Management", rows[2].Entity!.DisplayName);
    }

    [Fact]
    public async Task AnEntityWithNoNamePropertyFallsBackToItsId()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(4));
            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = 1, Entity = 4, Type = "description", Name = "description",
                Value = "no name row at all", OldValue = ""
            });
            ctx.RiskAppetites.Add(NewAppetite(1, entityId: 4));
        });

        var rows = await _appetites.GetAllAsync();

        // Not the empty string: a blank cell reads as "no scope", which is the opposite of what an
        // entity-scoped appetite means.
        Assert.Equal("#4", Assert.Single(rows).Entity!.DisplayName);
    }
}
