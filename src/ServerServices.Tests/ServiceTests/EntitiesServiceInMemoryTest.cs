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

    #region ReplaceProperties

    /// <summary>
    /// A businessProcess is the shape that exposed the old update path: "applications" and
    /// "organizationUnit" are multi-valued, the rest single-valued.
    /// </summary>
    private Entity NewBusinessProcess(int parentId = 0)
    {
        Seed(ctx => ctx.Entities.Add(NewEntity(500, "organizationUnit")));
        return _svc.CreateInstance(1, "businessProcess", parentId);
    }

    private static EntitiesPropertyDto Dto(string type, string value, int id = 0) =>
        new() { Id = id, Type = type, Value = value, Name = $"{type}-x" };

    [Fact]
    public void TestReplacePropertiesCreatesTheWholeBag()
    {
        var entity = NewBusinessProcess();

        var rows = _svc.ReplaceProperties(entity, [
            Dto("name", "Check"), Dto("description", "d"), Dto("objective", "o"),
            Dto("isActive", "true"), Dto("organizationUnit", "500")
        ]);

        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.True(r.Id > 0));
        Assert.Equal(5, entity.EntitiesProperties.Count);
    }

    /// <summary>
    /// A multi-valued property carrying exactly one value. The old code inferred multi-valuedness
    /// from the number of rows in the payload, so this was indistinguishable from a single-valued
    /// property and took a different, id-dependent path.
    /// </summary>
    [Fact]
    public void TestReplacePropertiesStoresOneValueOfAMultiValuedProperty()
    {
        var entity = NewBusinessProcess();

        _svc.ReplaceProperties(entity, [Dto("name", "Check"), Dto("organizationUnit", "500")]);

        using var ctx = OpenContext();
        Assert.Equal("500", ctx.EntitiesProperties.Single(p => p.Type == "organizationUnit").Value);
    }

    [Fact]
    public void TestReplacePropertiesIsIdempotentAndKeepsRowIds()
    {
        var entity = NewBusinessProcess();
        var payload = new List<EntitiesPropertyDto> { Dto("name", "Check"), Dto("organizationUnit", "500") };

        var first = _svc.ReplaceProperties(entity, payload).Select(r => r.Id).OrderBy(i => i).ToList();
        var second = _svc.ReplaceProperties(entity, payload).Select(r => r.Id).OrderBy(i => i).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>Omitting a nullable property is how a cleared field is expressed.</summary>
    [Fact]
    public void TestReplacePropertiesClearsAnOmittedProperty()
    {
        var entity = NewBusinessProcess();
        _svc.ReplaceProperties(entity, [Dto("name", "Check"), Dto("applications", "7")]);

        _svc.ReplaceProperties(entity, [Dto("name", "Check")]);

        using var ctx = OpenContext();
        Assert.Empty(ctx.EntitiesProperties.Where(p => p.Type == "applications").ToList());
    }

    [Fact]
    public void TestReplacePropertiesRecordsTheOldValueOfASingleValuedProperty()
    {
        var entity = NewBusinessProcess();
        _svc.ReplaceProperties(entity, [Dto("name", "Before")]);

        _svc.ReplaceProperties(entity, [Dto("name", "After")]);

        using var ctx = OpenContext();
        var row = ctx.EntitiesProperties.Single(p => p.Type == "name");
        Assert.Equal("After", row.Value);
        Assert.Equal("Before", row.OldValue);
    }

    [Fact]
    public void TestReplacePropertiesRefusesTwoValuesForASingleValuedProperty()
    {
        var entity = NewBusinessProcess();

        var ex = Assert.Throws<Exception>(() =>
            _svc.ReplaceProperties(entity, [Dto("name", "One"), Dto("name", "Two")]));

        Assert.Contains("single value", ex.Message);
    }

    [Fact]
    public void TestReplacePropertiesRefusesAnUnknownPropertyType()
    {
        var entity = NewBusinessProcess();

        Assert.ThrowsAny<Exception>(() =>
            _svc.ReplaceProperties(entity, [Dto("nosuchproperty", "x")]));
    }

    /// <summary>
    /// A Definition(...) property whose value is the literal "Parent" stores the parent's id, the
    /// same substitution CreateProperty makes.
    /// </summary>
    [Fact]
    public void TestReplacePropertiesResolvesTheParentPlaceholder()
    {
        var entity = NewBusinessProcess(parentId: 500);

        _svc.ReplaceProperties(entity, [Dto("name", "Check"), Dto("organizationUnit", "Parent")]);

        using var ctx = OpenContext();
        Assert.Equal("500", ctx.EntitiesProperties.Single(p => p.Type == "organizationUnit").Value);
    }

    [Fact]
    public void TestReplacePropertiesRefusesTheParentPlaceholderWithoutAParent()
    {
        var entity = NewBusinessProcess();

        var ex = Assert.Throws<Exception>(() =>
            _svc.ReplaceProperties(entity, [Dto("organizationUnit", "Parent")]));

        Assert.Contains("Parent is required", ex.Message);
    }

    #endregion

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
