using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using Model.Exceptions;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="EntitiesRestService"/> over a programmable HTTP backend plus a deterministic
/// in-memory cache.
///
/// The cache is a hand-written fake rather than the shipped <c>MemoryCacheService</c> so the tests
/// can assert which keys the service invalidated, which <see cref="IMemoryCacheService"/> itself
/// does not expose. It is not an NSubstitute double, because most of this service round-trips
/// through the cache after every call (it returns <c>GetCachedEntities(...)</c>, not the HTTP
/// payload) and a substitute answering null there makes the service throw before it can return.
///
/// The <see cref="IAuthenticationService"/> is a substitute so the <c>Unauthorized</c> branch cannot
/// reach <c>MutableConfigurationService</c>, which writes a LiteDB file on disk.
/// </summary>
[TestSubject(typeof(EntitiesRestService))]
public class EntitiesRestServiceTest : BaseServiceTest
{
    private sealed class FakeMemoryCache : IMemoryCacheService
    {
        private readonly Dictionary<(Type Type, string Key), object> _values = new();

        public List<string> Removals { get; } = new();

        public void Set<T>(string key, T value, TimeSpan? timeSpan = null)
        {
            if (value is null) return;
            _values[(typeof(T), key)] = value;
        }

        public T? Get<T>(string key)
            => _values.TryGetValue((typeof(T), key), out var value) ? (T)value : default;

        public void Remove<T>(string key)
        {
            Removals.Add($"{typeof(T).Name}:{key}");

            if (key == "*")
            {
                foreach (var stored in _values.Keys.Where(k => k.Type == typeof(T)).ToList())
                {
                    _values.Remove(stored);
                }
                return;
            }

            _values.Remove((typeof(T), key));
        }

        public bool HasCache<T>(string key)
            => key == "*"
                ? _values.Keys.Any(k => k.Type == typeof(T))
                : _values.ContainsKey((typeof(T), key));
    }

    private readonly StubRestBackend _backend = new();
    private readonly FakeMemoryCache _cache = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IEntitiesService _service;

    public EntitiesRestServiceTest()
    {
        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton<IMemoryCacheService>(_cache);
                s.AddSingleton(_authentication);
            })
            .GetRequiredService<IEntitiesService>();
    }

    private static Entity NamedEntity(int id, string name, string definition = "Server") => new()
    {
        Id = id,
        DefinitionName = definition,
        DefinitionVersion = "1.0",
        Status = "active",
        CreatedBy = 1,
        UpdatedBy = 1,
        EntitiesProperties = new List<EntitiesProperty>
        {
            new() { Id = id * 10, Type = "name", Value = name, Name = "name-" + id, OldValue = "", Entity = id }
        }
    };

    private static EntityDto NamedDto(int id, string name) => new()
    {
        Id = id,
        DefinitionName = "Server",
        Status = "active",
        EntitiesProperties = [new EntitiesPropertyDto { Id = id * 10, Type = "name", Value = name, Name = "name-" + id }]
    };

    private static EntitiesConfiguration Configuration() => new()
    {
        Version = "1.2",
        Definitions = new Dictionary<string, EntityDefinition>
        {
            ["Server"] = new()
            {
                IsRoot = false,
                IconKind = "Server",
                AllowedChildren = ["Service"],
                Properties = new Dictionary<string, EntityType>
                {
                    ["name"] = new()
                    {
                        Type = "string", Label = "Name", Nullable = false, DefaultValue = "new-server",
                        MinSize = 1, MaxSize = 100
                    },
                    ["notes"] = new()
                    {
                        Type = "string", Label = "Notes", Nullable = true, DefaultValue = null
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------- ClearCache

    [Fact]
    public void TestClearCacheDropsEveryCachedEntityList()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(1, "Alpha")]);
        _cache.Set<List<Entity>>("Server", [NamedEntity(1, "Alpha")]);
        _cache.Set("EntitiesConfiguration", Configuration());

        _service.ClearCache();

        Assert.Null(_cache.Get<List<Entity>>("All"));
        Assert.Null(_cache.Get<List<Entity>>("Server"));
        Assert.Contains("List`1:*", _cache.Removals);
        // Known limitation: only the entity lists are dropped — a stale configuration survives.
        Assert.NotNull(_cache.Get<EntitiesConfiguration>("EntitiesConfiguration"));
    }

    // ---------------------------------------------------------------- GetEntitiesConfigurationAsync

    [Fact]
    public async Task TestGetEntitiesConfigurationAsyncFetchesAndCachesTheConfiguration()
    {
        _backend.OnGet("/Entities/Configuration", Configuration());

        var configuration = await _service.GetEntitiesConfigurationAsync();

        Assert.Equal("1.2", configuration.Version);
        Assert.True(configuration.Definitions.ContainsKey("Server"));
        Assert.Equal("new-server", configuration.Definitions["Server"].Properties["name"].DefaultValue);
        Assert.Equal("GET /Entities/Configuration", _backend.LastRequest.ToString());
        Assert.NotNull(_cache.Get<EntitiesConfiguration>("EntitiesConfiguration"));
    }

    [Fact]
    public async Task TestGetEntitiesConfigurationAsyncServesTheCacheWithoutCallingTheServer()
    {
        _cache.Set("EntitiesConfiguration", Configuration());

        var configuration = await _service.GetEntitiesConfigurationAsync();

        Assert.Equal("1.2", configuration.Version);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestGetEntitiesConfigurationAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Entities/Configuration", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetEntitiesConfigurationAsync());
    }

    [Fact]
    public async Task TestGetEntitiesConfigurationAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Entities/Configuration", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetEntitiesConfigurationAsync());
    }

    [Fact]
    public async Task TestGetEntitiesConfigurationAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Entities/Configuration");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetEntitiesConfigurationAsync());
    }

    [Fact]
    public void TestGetEntitiesConfigurationRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Entities/Configuration", Configuration());

        var configuration = _service.GetEntitiesConfiguration();

        Assert.Equal("1.2", configuration.Version);
        Assert.True(_backend.Sent(Method.Get, "/Entities/Configuration"));
    }

    // ---------------------------------------------------------------- GetAllAsync

    [Fact]
    public async Task TestGetAllAsyncFetchesCachesAndSortsByName()
    {
        _backend.OnGet("/Entities", new List<Entity> { NamedEntity(1, "Beta"), NamedEntity(2, "Alpha") });

        var entities = await _service.GetAllAsync();

        Assert.Equal(2, entities.Count);
        Assert.Equal(2, entities[0].Id);
        Assert.Equal("Alpha", entities[0].EntitiesProperties.First().Value);
        Assert.Equal("Beta", entities[1].EntitiesProperties.First().Value);
        Assert.Equal("/Entities", _backend.LastRequest.Path);
        Assert.Contains("propertyLoad", _backend.LastRequest.Query);
        Assert.NotNull(_cache.Get<List<Entity>>("All"));
    }

    [Fact]
    public async Task TestGetAllAsyncFiltersByDefinitionAndOmitsThePropertyLoadFlag()
    {
        _backend.OnGet("/Entities", new List<Entity> { NamedEntity(3, "Gamma") });

        var entities = await _service.GetAllAsync("Server", false);

        Assert.Single(entities);
        Assert.Equal("?entityDefinition=Server", _backend.LastRequest.Query);
        // The definition-scoped answer is cached under the definition name, not under "All".
        Assert.NotNull(_cache.Get<List<Entity>>("Server"));
        Assert.Null(_cache.Get<List<Entity>>("All"));
    }

    [Fact]
    public async Task TestGetAllAsyncServesTheFullCacheWithoutCallingTheServer()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(1, "Beta"), NamedEntity(2, "Alpha")]);

        var entities = await _service.GetAllAsync();

        Assert.Equal(2, entities.Count);
        Assert.Equal(2, entities[0].Id);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestGetAllAsyncServesTheDefinitionCacheWithoutCallingTheServer()
    {
        _cache.Set<List<Entity>>("Server", [NamedEntity(4, "Delta")]);

        var entities = await _service.GetAllAsync("Server");

        Assert.Equal(4, Assert.Single(entities).Id);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerHasNothing()
    {
        _backend.OnStatus(Method.Get, "/Entities", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Entities", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync("Server"));
    }

    [Fact]
    public void TestGetAllRunsTheAsyncCallSynchronously()
    {
        _backend.OnGet("/Entities", new List<Entity> { NamedEntity(1, "Beta"), NamedEntity(2, "Alpha") });

        var entities = _service.GetAll();

        Assert.Equal(2, entities.Count);
        Assert.Equal(2, entities[0].Id);
        Assert.True(_backend.Sent(Method.Get, "/Entities"));
    }

    // ---------------------------------------------------------------- GetEntity

    [Fact]
    public void TestGetEntityThrowsWhenTheFullListWasNeverLoaded()
    {
        // Known limitation, still open: GetEntity consults GetCachedEntities("All") before anything
        // else, and that helper throws when the "All" key is absent. So a caller that has not called
        // GetAll first can never reach the HTTP request at all. What the fix changed is the type —
        // a typed NullObjectException naming the missing cache instead of a bare Exception.
        _backend.OnGet("/Entities/5", NamedEntity(5, "Epsilon"));

        var exception = Assert.Throws<NullObjectException>(() => _service.GetEntity(5));

        Assert.Equal("entities cache All", exception.ObjectName);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestGetEntityServesTheCachedEntityWithoutCallingTheServer()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);

        var entity = _service.GetEntity(5);

        Assert.Equal(5, entity.Id);
        Assert.Equal("Epsilon", entity.EntitiesProperties.First().Value);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestGetEntityFetchesAndAddsItToTheCache()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnGet("/Entities/5", NamedEntity(5, "Epsilon"));

        var entity = _service.GetEntity(5);

        Assert.Equal(5, entity.Id);
        Assert.Equal("Server", entity.DefinitionName);
        Assert.Equal("/Entities/5", _backend.LastRequest.Path);
        Assert.Contains("propertyLoad", _backend.LastRequest.Query);
        Assert.Equal(5, Assert.Single(_cache.Get<List<Entity>>("All")!).Id);
    }

    [Fact]
    public void TestGetEntityOmitsThePropertyLoadFlagWhenPropertiesAreNotWanted()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnGet("/Entities/6", NamedEntity(6, "Zeta"));

        _service.GetEntity(6, false);

        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public void TestGetEntityThrowsWhenTheServerHasNothing()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Get, "/Entities/7", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetEntity(7));
    }

    [Fact]
    public void TestGetEntityWrapsAServerError()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Get, "/Entities/7", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetEntity(7));
    }

    // ---------------------------------------------------------------- GetMandatoryPropertiesAsync

    [Fact]
    public async Task TestGetMandatoryPropertiesAsyncKeepsOnlyTheNonNullableProperties()
    {
        _cache.Set("EntitiesConfiguration", Configuration());

        var properties = await _service.GetMandatoryPropertiesAsync("Server");

        var property = Assert.Single(properties);
        Assert.Equal("name", property.Type);
        Assert.Equal("new-server", property.Value);
        Assert.Equal("name-", property.Name);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestGetMandatoryPropertiesAsyncLoadsTheConfigurationWhenItIsNotCached()
    {
        _backend.OnGet("/Entities/Configuration", Configuration());

        var properties = await _service.GetMandatoryPropertiesAsync("Server");

        Assert.Single(properties);
        Assert.True(_backend.Sent(Method.Get, "/Entities/Configuration"));
    }

    [Fact]
    public async Task TestGetMandatoryPropertiesAsyncThrowsForAnUnknownDefinition()
    {
        _cache.Set("EntitiesConfiguration", Configuration());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.GetMandatoryPropertiesAsync("Router"));
    }

    // ---------------------------------------------------------------- CreateEntity

    [Fact]
    public void TestCreateEntityPostsTheDtoAndCachesTheAnswer()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnPost("/Entities", NamedEntity(9, "Theta"));

        var created = _service.CreateEntity(NamedDto(0, "Theta"));

        Assert.NotNull(created);
        Assert.Equal(9, created.Id);
        Assert.Equal("/Entities", _backend.LastRequest.Path);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Contains("Theta", _backend.LastRequest.Body);
        Assert.Equal(9, Assert.Single(_cache.Get<List<Entity>>("All")!).Id);
    }

    [Fact]
    public void TestCreateEntityThrowsWhenTheServerHasNothing()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Post, "/Entities", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.CreateEntity(NamedDto(0, "Theta")));
    }

    [Fact]
    public void TestCreateEntityWrapsAServerError()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Post, "/Entities", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.CreateEntity(NamedDto(0, "Theta")));
    }

    // ---------------------------------------------------------------- SaveEntityAsync

    [Fact]
    public async Task TestSaveEntityAsyncPutsToTheEntityUrlAndReturnsTheSavedEntity()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);
        _backend.OnPut("/Entities/5", NamedEntity(5, "Epsilon renamed"));

        var saved = await _service.SaveEntityAsync(NamedDto(5, "Epsilon renamed"));

        Assert.NotNull(saved);
        Assert.Equal("Epsilon renamed", saved.EntitiesProperties.First().Value);
        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal("/Entities/5", _backend.LastRequest.Path);
        Assert.Contains("Epsilon renamed", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestSaveEntityAsyncEvictsTheOldEntityWithoutCachingTheNewOne()
    {
        // Known limitation: the update path removes the stale entity from the "All" cache but never
        // puts the freshly saved one back, so the next GetAll answers without it until the cache is
        // reloaded.
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);
        _backend.OnPut("/Entities/5", NamedEntity(5, "Epsilon renamed"));

        await _service.SaveEntityAsync(NamedDto(5, "Epsilon renamed"));

        Assert.Empty(_cache.Get<List<Entity>>("All")!);
    }

    [Fact]
    public async Task TestSaveEntityAsyncThrowsWhenTheServerHasNothing()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Put, "/Entities/5", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SaveEntityAsync(NamedDto(5, "Epsilon")));
    }

    [Fact]
    public async Task TestSaveEntityAsyncWrapsAServerError()
    {
        _cache.Set<List<Entity>>("All", []);
        _backend.OnStatus(Method.Put, "/Entities/5", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.SaveEntityAsync(NamedDto(5, "Epsilon")));
    }

    [Fact]
    public void TestSaveEntityRunsTheAsyncCallSynchronously()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);
        _backend.OnPut("/Entities/5", NamedEntity(5, "Epsilon renamed"));

#pragma warning disable CS0618 // the synchronous overload is obsolete but still shipped
        var saved = _service.SaveEntity(NamedDto(5, "Epsilon renamed"));
#pragma warning restore CS0618

        Assert.NotNull(saved);
        Assert.Equal(5, saved.Id);
        Assert.True(_backend.Sent(Method.Put, "/Entities/5"));
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public void TestDeleteRemovesTheEntityFromTheServerAndTheCache()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon"), NamedEntity(6, "Zeta")]);
        _backend.OnDelete("/Entities/5", "");

        _service.Delete(5);

        Assert.True(_backend.Sent(Method.Delete, "/Entities/5"));
        Assert.Equal(6, Assert.Single(_cache.Get<List<Entity>>("All")!).Id);
    }

    [Fact]
    public void TestDeleteWrapsAServerError()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);
        _backend.OnStatus(Method.Delete, "/Entities/5", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Delete(5));
    }

    [Fact]
    public void TestDeleteWrapsATransportFailure()
    {
        _cache.Set<List<Entity>>("All", [NamedEntity(5, "Epsilon")]);
        _backend.OnTransportFailure(Method.Delete, "/Entities/5");

        Assert.Throws<RestComunicationException>(() => _service.Delete(5));
    }
}
