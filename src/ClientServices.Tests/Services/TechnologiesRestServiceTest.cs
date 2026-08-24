using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(TechnologiesRestService))]
public class TechnologiesRestServiceTest : BaseServiceTest
{
    private const string Path = "/Technologies";

    private readonly StubRestBackend _backend = new();
    private readonly ITechnologiesService _service;

    public TechnologiesRestServiceTest()
    {
        _service = ResolveWith<ITechnologiesService>(_backend);
    }

    /// <summary>Deliberately out of order, so the service's own sort is what the test observes.</summary>
    private static List<Technology> UnsortedTechnologies() =>
    [
        new() { Value = 3, Name = "Zookeeper" },
        new() { Value = 1, Name = "Apache" },
        new() { Value = 2, Name = "MySQL" }
    ];

    // ---------------- GetAll ----------------

    [Fact]
    public void TestGetAllReturnsTheTechnologiesSortedByName()
    {
        _backend.OnGet(Path, UnsortedTechnologies());

        var technologies = _service.GetAll();

        Assert.Equal(3, technologies.Count);
        Assert.Equal(new[] { "Apache", "MySQL", "Zookeeper" }, technologies.ConvertAll(t => t.Name));
        Assert.Equal(1, technologies[0].Value);
        Assert.Equal("GET " + Path, _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAllReturnsAnEmptyListWhenThereAreNoTechnologies()
    {
        _backend.OnGet(Path, new List<Technology>());

        Assert.Empty(_service.GetAll());
    }

    [Fact]
    public void TestGetAllThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.GetAll());
        // Note the message points at "/Technology" while the request goes to "/Technologies".
        Assert.Equal("/Technology", ex.Url);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public void TestGetAllWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetAll());
        Assert.Equal("Error listing Technologies", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestGetAllWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, Path);

        Assert.Throws<RestComunicationException>(() => _service.GetAll());
    }

    // ---------------- GetAllAsync ----------------

    [Fact]
    public async Task TestGetAllAsyncReturnsTheTechnologiesSortedByName()
    {
        _backend.OnGet(Path, UnsortedTechnologies());

        var technologies = await _service.GetAllAsync();

        Assert.Equal(3, technologies.Count);
        Assert.Equal(new[] { "Apache", "MySQL", "Zookeeper" }, technologies.ConvertAll(t => t.Name));
        Assert.Equal("GET " + Path, _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncGoesToTheServerOnEveryCall()
    {
        _backend.OnGet(Path, UnsortedTechnologies());

        await _service.GetAllAsync();
        await _service.GetAllAsync();

        Assert.Equal(2, _backend.Requests.Count);
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());
        Assert.Equal("/Technology", ex.Url);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
        Assert.Equal("Error listing Technologies", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, Path);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }
}
