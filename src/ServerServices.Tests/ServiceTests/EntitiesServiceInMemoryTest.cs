using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(EntitiesService))]
public class EntitiesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IEntitiesService _svc;

    public EntitiesServiceInMemoryTest()
    {
        _svc = GetService<IEntitiesService>();
    }

    private static Entity NewEntity(int id, string def = "person") => new()
    {
        Id = id,
        DefinitionName = def,
        DefinitionVersion = "2.3",
        Status = "active",
        Created = new DateTime(2026, 1, 1),
        Updated = new DateTime(2026, 1, 1)
    };

    private static EntitiesProperty NewProperty(int id, int entityId, string type = "name", string value = "X") => new()
    {
        Id = id, Entity = entityId, Type = type, Value = value, OldValue = "", Name = type
    };

    [Fact]
    public async Task TestGetEntitiesConfiguration()
    {
        var config = await _svc.GetEntitiesConfigurationAsync();

        Assert.NotNull(config);
        Assert.NotEmpty(config.Definitions);
        Assert.Contains("person", config.Definitions.Keys);
    }

    [Fact]
    public void TestCreateInstance()
    {
        var entity = _svc.CreateInstance(7, "person");

        Assert.True(entity.Id > 0);
        Assert.Equal("person", entity.DefinitionName);
        Assert.Equal(7, entity.CreatedBy);
        Assert.Equal("active", entity.Status);
    }

    [Fact]
    public void TestCreateInstanceWithParent()
    {
        var parent = _svc.CreateInstance(1, "organization");
        var child = _svc.CreateInstance(1, "person", parent.Id);

        Assert.Equal(parent.Id, child.Parent);
    }

    [Fact]
    public void TestGetEntityNotFound()
    {
        Assert.Throws<DataNotFoundException>(() => _svc.GetEntity(999));
    }

    [Fact]
    public void TestGetEntity()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1)));

        var entity = _svc.GetEntity(1);

        Assert.Equal(1, entity.Id);
    }

    [Fact]
    public void TestGetEntitiesAll()
    {
        Seed(ctx =>
        {
            ctx.Entities.Add(NewEntity(1, "person"));
            ctx.Entities.Add(NewEntity(2, "organization"));
        });

        Assert.Equal(2, _svc.GetEntities().Count);
        Assert.Single(_svc.GetEntities("person"));
    }

    [Fact]
    public void TestGetEntitiesUnknownDefinitionThrows()
    {
        Assert.Throws<EntityDefinitionNotFoundException>(() => _svc.GetEntities("does-not-exist"));
    }

    [Fact]
    public void TestDeleteEntity()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1)));

        var deleted = _svc.DeleteEntity(1);

        Assert.Equal(1, deleted.Id);
        Assert.Throws<DataNotFoundException>(() => _svc.GetEntity(1));
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteEntity(1));
    }

    [Fact]
    public void TestDeleteEntitiesProperty()
    {
        Seed(ctx => ctx.EntitiesProperties.Add(NewProperty(1, 10)));

        _svc.DeleteEntitiesProperty(1);

        using var ctx = OpenContext();
        Assert.Empty(ctx.EntitiesProperties.ToList());
        Assert.Throws<DataNotFoundException>(() => _svc.DeleteEntitiesProperty(1));
    }

    [Fact]
    public void TestTryDeleteEntitiesPropertyById()
    {
        Seed(ctx => ctx.EntitiesProperties.Add(NewProperty(1, 10)));

        _svc.TryDeleteEntitiesProperty(1);           // existing
        _svc.TryDeleteEntitiesProperty(999);         // missing → no throw

        using var ctx = OpenContext();
        Assert.Empty(ctx.EntitiesProperties.ToList());
    }

    [Fact]
    public void TestTryDeleteEntitiesPropertyByTypeAndEntity()
    {
        Seed(ctx =>
        {
            ctx.EntitiesProperties.Add(NewProperty(1, 10, "name"));
            ctx.EntitiesProperties.Add(NewProperty(2, 10, "name"));
        });

        _svc.TryDeleteEntitiesProperty("name", 10);
        _svc.TryDeleteEntitiesProperty("missing", 10); // no-op

        using var ctx = OpenContext();
        Assert.Empty(ctx.EntitiesProperties.ToList());
    }

    [Fact]
    public void TestUpdateEntitiesProperty()
    {
        Seed(ctx => ctx.EntitiesProperties.Add(NewProperty(1, 10, value: "Before")));

        _svc.UpdateEntitiesProperty(NewProperty(1, 10, value: "After"));

        using var ctx = OpenContext();
        Assert.Equal("After", ctx.EntitiesProperties.First().Value);
        Assert.Throws<DataNotFoundException>(() => _svc.UpdateEntitiesProperty(NewProperty(99, 10)));
    }

    [Fact]
    public void TestUpdateEntity()
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(1)));

        var update = NewEntity(1);
        update.Status = "inactive";
        _svc.UpdateEntity(update);

        Assert.Equal("inactive", _svc.GetEntity(1).Status);
        Assert.Throws<DataNotFoundException>(() => _svc.UpdateEntity(NewEntity(99)));
    }

    [Fact]
    public void TestValidatePropertyListMissingRequiredThrows()
    {
        // "person" requires "name"; passing none should throw.
        Assert.Throws<Exception>(() =>
            _svc.ValidatePropertyList("person", new List<EntitiesPropertyDto>()));
    }

    [Fact]
    public void TestValidatePropertyListValid()
    {
        var props = new List<EntitiesPropertyDto>
        {
            new() { Type = "name", Value = "Alice" }
        };

        // Should not throw.
        _svc.ValidatePropertyList("person", props);
    }

    [Fact]
    public void TestCreateProperty()
    {
        var entity = _svc.CreateInstance(1, "person");
        var dto = new EntitiesPropertyDto { Type = "name", Value = "Bob", Name = "name" };

        var prop = _svc.CreateProperty("person", ref entity, dto);

        Assert.True(prop.Id > 0);
        Assert.Equal("Bob", prop.Value);

        // duplicate non-multiple property throws
        Assert.Throws<Exception>(() =>
            _svc.CreateProperty("person", ref entity, new EntitiesPropertyDto { Type = "name", Value = "Dup", Name = "name" }));
    }
}
