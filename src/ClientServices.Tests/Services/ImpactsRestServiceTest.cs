using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.DI;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Model.Exceptions;
using Model.Globalization;
using NSubstitute;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

// CS0618 suppressed for this file: several tests here cover IImpactsService.GetAll(), the *obsolete*
// synchronous overload, which is still shipped and still has callers. The async replacement has its
// own tests in the same class; switching these to it would leave the method the product still exposes
// untested, which is the opposite of what the warning wants.
#pragma warning disable CS0618
[TestSubject(typeof(ImpactsRestService))]
public class ImpactsRestServiceTest : BaseServiceTest
{
    private const string Path = "/Impacts";

    private readonly StubRestBackend _backend = new();
    private readonly IListLocalizationService _localization = Substitute.For<IListLocalizationService>();
    private readonly IImpactsService _service;

    public ImpactsRestServiceTest()
    {
        // The service hands whatever the server returned to the localization service and caches the
        // *localized* list, so the double has to actually produce one.
        _localization.LocalizeList(Arg.Any<List<LocalizableListItem>>())
            .Returns(call => ((List<LocalizableListItem>)call[0])
                .Select(i => new LocalizableListItem { Key = i.Key, Value = i.Value, LocalizedValue = "L:" + i.Value })
                .ToList());

        _service = ServiceRegistration
            .GetServiceProvider(s =>
            {
                s.AddSingleton<IRestService>(_backend);
                s.AddSingleton(_localization);
            })
            .GetRequiredService<IImpactsService>();
    }

    private static List<LocalizableListItem> TwoImpacts() =>
    [
        new() { Key = 1, Value = "Insignificant" },
        new() { Key = 2, Value = "Catastrophic" }
    ];

    // ---------------- GetAll ----------------

    [Fact]
    public void TestGetAllReturnsTheLocalizedList()
    {
        _backend.OnGet(Path, TwoImpacts());

        var impacts = _service.GetAll();

        Assert.Equal(2, impacts.Count);
        Assert.Equal(1, impacts[0].Key);
        Assert.Equal("L:Insignificant", impacts[0].LocalizedValue);
        Assert.Equal("L:Catastrophic", impacts[1].LocalizedValue);
        Assert.Equal("GET " + Path, _backend.LastRequest.ToString());
        _localization.Received(1).LocalizeList(Arg.Any<List<LocalizableListItem>>());
    }

    [Fact]
    public void TestGetAllCachesTheListAfterTheFirstCall()
    {
        _backend.OnGet(Path, TwoImpacts());

        var first = _service.GetAll();
        var second = _service.GetAll();

        Assert.Same(first, second);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public void TestGetAllThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(() => _service.GetAll());
        Assert.Equal(Path, ex.Url);
        Assert.Equal("GET", ex.Method);
    }

    [Fact]
    public void TestGetAllWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetAll());
        Assert.Equal("Error listing impacts", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestGetAllWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, Path);

        Assert.Throws<RestComunicationException>(() => _service.GetAll());
    }

    // ---------------- GetAllAsync ----------------

    [Fact]
    public async Task TestGetAllAsyncReturnsTheLocalizedList()
    {
        _backend.OnGet(Path, TwoImpacts());

        var impacts = await _service.GetAllAsync();

        Assert.Equal(2, impacts.Count);
        Assert.Equal("L:Catastrophic", impacts[1].LocalizedValue);
        Assert.Equal("GET " + Path, _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncServesTheCacheFilledByGetAll()
    {
        _backend.OnGet(Path, TwoImpacts());

        var sync = _service.GetAll();
        var async = await _service.GetAllAsync();

        Assert.Same(sync, async);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());
        Assert.Equal(Path, ex.Url);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, Path, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
        Assert.Equal("Error listing impacts", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, Path);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }
}
#pragma warning restore CS0618
