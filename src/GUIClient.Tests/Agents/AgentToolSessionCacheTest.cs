using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using GUIClient.Agents;
using GUIClient.Agents.ReadTools;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// M62 review finding 1: a tool reads through the API with the current session, never from a process-wide cache an
/// earlier session filled. User A's session warms the real ClientServices caches; user B, whose scope does not include
/// A's items, then calls the tools: the API must be asked, and A's item must not come back.
/// </summary>
[TestSubject(typeof(AgentReadToolCatalog))]
[Collection(ClientServicesAccessorCollection.Name)]
public class AgentToolSessionCacheTest
{
    private static (HttpStatusCode, string) Json(object value) => (HttpStatusCode.OK, JsonSerializer.Serialize(value));

    [Fact]
    public async Task TestGetHostAsksTheApiAndNeverServesAnotherSessionsCachedHost()
    {
        var api = new RefusingRestService();
        var services = new RealClientServices(api);
        api.Responder = (_, path) => path == "/Hosts"
            ? Json(new List<Host> { new() { Id = 1, HostName = "alice-db", Source = "scan" } })
            : null;
        await services.Hosts.GetAllAsync();                                // user A's session fills the host cache
        Assert.Equal("alice-db", services.Hosts.GetOne(1)!.HostName);

        // User B can read hosts, but host 1 is outside B's scope: the list by id comes back empty.
        api.Responder = (_, path) => path switch
        {
            "/Hosts/Filtered" => Json(new List<Host>()),
            "/Hosts/1/Services" => Json(new List<HostsService>()),
            "/Hosts/1/VulnerabilitySummary" => Json(new { hostId = 1 }),
            _ => null
        };
        var before = api.Requests.Count;
        var answer = await AgentReadRig.OverRealServices(services).CallAsync("get_host");

        Assert.Contains(api.Requests.Skip(before), r => r.EndsWith("/Hosts/Filtered", StringComparison.Ordinal));
        Assert.Equal(AgentToolErrors.NotFound, answer.Status);
        Assert.DoesNotContain("alice-db", answer.Body);
    }

    [Theory]
    [InlineData("get_entity")]
    [InlineData("get_entity_relationships")]
    [InlineData("search_entities")]
    public async Task TestEntityToolsAskTheApiAndNeverServeAnotherSessionsCachedEntities(string tool)
    {
        var api = new RefusingRestService();
        var services = new RealClientServices(api);
        var alice = new Entity
        {
            Id = 1, DefinitionName = "application", Status = "Active",
            EntitiesProperties = new List<EntitiesProperty> { new() { Type = "name", Value = "alice-payroll" } }
        };
        api.Responder = (_, path) => path == "/Entities" ? Json(new List<Entity> { alice }) : null;
        await services.Entities.GetAllAsync();                             // user A's session fills the 60-minute cache
        Assert.True(services.Cache.HasCache<List<Entity>>("All"));

        api.Responder = (_, path) => path == "/Entities" ? Json(new List<Entity>()) : null; // user B sees none
        var before = api.Requests.Count;
        var answer = await AgentReadRig.OverRealServices(services).CallAsync(tool);

        Assert.Contains(api.Requests.Skip(before), r => r.EndsWith("/Entities", StringComparison.Ordinal));
        Assert.DoesNotContain("alice-payroll", answer.Body);
        if (tool != "search_entities") Assert.Equal(AgentToolErrors.NotFound, answer.Status);
        else Assert.Equal(0, (int)answer.Data!["total"]!);
    }

    [Fact]
    public async Task TestAToolRefusedByTheApiIsNotAnsweredFromTheCache()
    {
        var api = new RefusingRestService();
        var services = new RealClientServices(api);
        api.Responder = (_, path) => path == "/Entities"
            ? Json(new List<Entity> { new() { Id = 1, DefinitionName = "application", Status = "Active",
                EntitiesProperties = new List<EntitiesProperty> { new() { Type = "name", Value = "alice-payroll" } } } })
            : null;
        await services.Entities.GetAllAsync();

        api.Responder = null; // the server now refuses this session everything
        var answer = await AgentReadRig.OverRealServices(services).CallAsync("get_entity");

        Assert.Equal(AgentToolErrors.Refused, answer.Status);
        Assert.DoesNotContain("alice-payroll", answer.Body);
    }
}
