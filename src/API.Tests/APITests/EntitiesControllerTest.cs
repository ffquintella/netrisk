using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(EntitiesController))]
public class EntitiesControllerTest : BaseControllerTest
{
    private readonly IEntitiesService _entitiesService = Substitute.For<IEntitiesService>();
    private readonly EntitiesController _controller;

    public EntitiesControllerTest()
    {
        _controller = ResolveController<EntitiesController>(s => s.AddSingleton(_entitiesService));
    }

    private static Entity MakeEntity(int id, string definitionName)
    {
        return new Entity
        {
            Id = id,
            DefinitionName = definitionName,
            DefinitionVersion = "1.0",
            Status = "active",
            CreatedBy = 1,
            UpdatedBy = 1
        };
    }

    private static EntitiesProperty MakeProperty(int id, string type, string value)
    {
        return new EntitiesProperty
        {
            Id = id,
            Type = type,
            Value = value,
            Name = type,
            OldValue = string.Empty,
            Entity = 1
        };
    }

    /// <summary>
    /// Stubs the two property writers, which take the entity by <c>ref</c>. The callback writes the
    /// caller's instance back into the ref slot so the controller keeps a usable entity afterwards.
    /// Only the ref parameter uses an argument matcher: mixing matchers with a ref parameter makes
    /// NSubstitute's matcher queue positional, so the other arguments are passed as literals.
    /// </summary>
    private void StubPropertyWriters(Entity entity, List<EntitiesPropertyDto> properties)
    {
        foreach (var property in properties)
        {
            var entityForCreate = Arg.Any<Entity>();
            _entitiesService.CreateProperty(entity.DefinitionName, ref entityForCreate, property)
                .Returns(callInfo =>
                {
                    callInfo[1] = entity;
                    return MakeProperty(0, property.Type, property.Value);
                });

            var entityForUpdate = Arg.Any<Entity>();
            _entitiesService.UpdateProperty(ref entityForUpdate, property, false)
                .Returns(callInfo =>
                {
                    callInfo[0] = entity;
                    return MakeProperty(property.Id, property.Type, property.Value);
                });
        }
    }

    #region GetConfiguration

    [Fact]
    public async Task TestGetConfiguration()
    {
        _entitiesService.GetEntitiesConfigurationAsync()
            .Returns(new EntitiesConfiguration { Version = "2.0" });

        var result = await _controller.GetConfiguration();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var configuration = Assert.IsType<EntitiesConfiguration>(okResult.Value);

        Assert.Equal("2.0", configuration.Version);
    }

    [Fact]
    public async Task TestGetConfigurationReturns500OnError()
    {
        _entitiesService.GetEntitiesConfigurationAsync()
            .Returns<Task<EntitiesConfiguration>>(_ => throw new Exception("boom"));

        var result = await _controller.GetConfiguration();

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region ListAll

    [Fact]
    public void TestListAll()
    {
        _entitiesService.GetEntities("host", true).Returns(new List<Entity>
        {
            MakeEntity(1, "host"),
            MakeEntity(2, "host")
        });

        var result = _controller.ListAll("host", true);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var entities = Assert.IsType<List<Entity>>(okResult.Value);

        Assert.Equal(2, entities.Count);
        Assert.Equal("host", entities[0].DefinitionName);
    }

    [Fact]
    public void TestListAllWithDefaultArguments()
    {
        _entitiesService.GetEntities(null, false).Returns(new List<Entity> { MakeEntity(1, "host") });

        var result = _controller.ListAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var entities = Assert.IsType<List<Entity>>(okResult.Value);

        Assert.Single(entities);
    }

    [Fact]
    public void TestListAllUnknownDefinitionReturns404()
    {
        _entitiesService.GetEntities("missing", false)
            .Returns<List<Entity>>(_ => throw new EntityDefinitionNotFoundException("missing"));

        var result = _controller.ListAll("missing");

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
    }

    [Fact]
    public void TestListAllReturns500OnError()
    {
        _entitiesService.GetEntities("boom", false)
            .Returns<List<Entity>>(_ => throw new Exception("boom"));

        var result = _controller.ListAll("boom");

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region DeleteOne

    [Fact]
    public void TestDeleteOne()
    {
        _entitiesService.DeleteEntity(1).Returns(MakeEntity(1, "host"));

        var result = _controller.DeleteOne(1);

        Assert.IsType<OkResult>(result);
        _entitiesService.Received(1).DeleteEntity(1);
    }

    [Fact]
    public void TestDeleteOneNotFoundReturns404()
    {
        _entitiesService.DeleteEntity(999)
            .Returns<Entity>(_ => throw new DataNotFoundException("entities", "999"));

        var result = _controller.DeleteOne(999);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
    }

    [Fact]
    public void TestDeleteOneReturns500OnError()
    {
        _entitiesService.DeleteEntity(2).Returns<Entity>(_ => throw new Exception("boom"));

        var result = _controller.DeleteOne(2);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetOne

    [Fact]
    public void TestGetOne()
    {
        _entitiesService.GetEntity(1).Returns(MakeEntity(1, "host"));

        var result = _controller.GetOne(1);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var entity = Assert.IsType<Entity>(okResult.Value);

        Assert.Equal(1, entity.Id);
        Assert.Equal("host", entity.DefinitionName);
    }

    [Fact]
    public void TestGetOneReturns500OnError()
    {
        _entitiesService.GetEntity(999)
            .Returns<Entity>(_ => throw new DataNotFoundException("entities", "999"));

        var result = _controller.GetOne(999);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region Update

    [Fact]
    public void TestUpdateWithoutProperties()
    {
        var entity = MakeEntity(1, "host");
        _entitiesService.GetEntity(1).Returns(entity);

        var dto = new EntityDto
        {
            Id = 1,
            DefinitionName = "host",
            Status = "inactive",
            Parent = 3,
            EntitiesProperties = new List<EntitiesPropertyDto>()
        };

        var result = _controller.Update(1, dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var updated = Assert.IsType<Entity>(okResult.Value);

        Assert.Equal("inactive", updated.Status);
        Assert.Equal(3, updated.Parent.Value);
        Assert.Equal(1, updated.UpdatedBy);
        _entitiesService.Received(1).UpdateEntity(entity);
    }

    [Fact]
    public void TestUpdateWithSingleValueProperties()
    {
        var entity = MakeEntity(1, "host");
        _entitiesService.GetEntity(1).Returns(entity);

        var properties = new List<EntitiesPropertyDto>
        {
            // Already persisted -> update path.
            new EntitiesPropertyDto { Id = 10, Type = "name", Value = "server-a", Name = "name" },
            // Brand new -> create path.
            new EntitiesPropertyDto { Id = 0, Type = "ip", Value = "10.0.0.1", Name = "ip" }
        };

        StubPropertyWriters(entity, properties);

        var dto = new EntityDto
        {
            Id = 1,
            DefinitionName = "host",
            Status = "active",
            EntitiesProperties = properties
        };

        var result = _controller.Update(1, dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<Entity>(okResult.Value);

        Assert.Equal(2, entity.EntitiesProperties.Count);
        _entitiesService.Received(1).UpdateEntity(entity);
        _entitiesService.DidNotReceive().TryDeleteEntitiesProperty(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void TestUpdateWithMultiValueProperties()
    {
        var entity = MakeEntity(1, "host");
        _entitiesService.GetEntity(1).Returns(entity);

        var properties = new List<EntitiesPropertyDto>
        {
            // Two values of the same type -> multivalue path; the persisted one triggers the delete.
            new EntitiesPropertyDto { Id = 10, Type = "ip", Value = "10.0.0.1", Name = "ip" },
            new EntitiesPropertyDto { Id = 11, Type = "ip", Value = "10.0.0.2", Name = "ip" }
        };

        StubPropertyWriters(entity, properties);

        var dto = new EntityDto
        {
            Id = 1,
            DefinitionName = "host",
            Status = "active",
            EntitiesProperties = properties
        };

        var result = _controller.Update(1, dto);

        Assert.IsType<OkObjectResult>(result.Result);

        // Deleted once only, even though two properties share the type.
        _entitiesService.Received(1).TryDeleteEntitiesProperty("ip", 1);
        _entitiesService.Received(1).UpdateEntity(entity);
    }

    [Fact]
    public void TestUpdateReturns500OnError()
    {
        _entitiesService.GetEntity(999)
            .Returns<Entity>(_ => throw new DataNotFoundException("entities", "999"));

        var dto = new EntityDto
        {
            Id = 999,
            DefinitionName = "host",
            Status = "active",
            EntitiesProperties = new List<EntitiesPropertyDto>()
        };

        var result = _controller.Update(999, dto);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region Create

    [Fact]
    public void TestCreateWithoutParent()
    {
        var created = MakeEntity(5, "host");
        _entitiesService.CreateInstance(1, "host", 0).Returns(created);

        var properties = new List<EntitiesPropertyDto>
        {
            new EntitiesPropertyDto { Id = 0, Type = "name", Value = "server-a", Name = "name" }
        };

        StubPropertyWriters(created, properties);

        var dto = new EntityDto
        {
            DefinitionName = "host",
            Status = "active",
            EntitiesProperties = properties
        };

        var result = _controller.Create(dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var entity = Assert.IsType<Entity>(okResult.Value);

        Assert.Equal(5, entity.Id);
        _entitiesService.Received(1).ValidatePropertyList("host", properties);
        _entitiesService.Received(1).UpdateEntity(created);
    }

    [Fact]
    public void TestCreateWithParent()
    {
        var created = MakeEntity(6, "host");
        _entitiesService.CreateInstance(1, "host", 4).Returns(created);

        var dto = new EntityDto
        {
            DefinitionName = "host",
            Status = "active",
            Parent = 4,
            EntitiesProperties = new List<EntitiesPropertyDto>()
        };

        var result = _controller.Create(dto);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var entity = Assert.IsType<Entity>(okResult.Value);

        Assert.Equal(6, entity.Id);
        _entitiesService.Received(1).CreateInstance(1, "host", 4);
        _entitiesService.Received(1).UpdateEntity(created);
    }

    [Fact]
    public void TestCreateReturns500OnInvalidPropertyList()
    {
        _entitiesService
            .When(x => x.ValidatePropertyList("broken", Arg.Any<List<EntitiesPropertyDto>>()))
            .Do(_ => throw new EntityDefinitionNotFoundException("broken"));

        var dto = new EntityDto
        {
            DefinitionName = "broken",
            Status = "active",
            EntitiesProperties = new List<EntitiesPropertyDto>()
        };

        var result = _controller.Create(dto);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion
}
