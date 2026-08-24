using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.EntitiesDto;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(ReportsRestService))]
public class ReportsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IReportsService _service;

    public ReportsRestServiceTest()
    {
        _service = ResolveWith<IReportsService>(_backend);
    }

    private static Report SavedReport(int id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatorId = 2,
        FileId = 40 + id,
        Parameters = "{\"scope\":\"all\"}",
        CreationDate = new DateTime(2024, 4, 4, 10, 0, 0, DateTimeKind.Utc),
        Type = 1,
        Status = 3
    };

    private static ReportDto Dto() => new()
    {
        Name = "QuarterlyVulnerabilityReport",
        CreatorId = 2,
        Parameters = "{\"scope\":\"all\"}",
        CreationDate = new DateTime(2024, 4, 4, 10, 0, 0, DateTimeKind.Utc),
        Type = 1,
        Status = 0
    };

    // ---------- GetReportsAsync ----------

    [Fact]
    public async Task TestGetReportsAsyncReturnsAnObservableCollection()
    {
        _backend.OnGet("/Reports", new List<Report> { SavedReport(1, "First"), SavedReport(2, "Second") });

        var reports = await _service.GetReportsAsync();

        Assert.Equal(2, reports.Count);
        Assert.Equal("First", reports[0].Name);
        Assert.Equal(42, reports[1].FileId);
        Assert.Equal(3u, reports[0].Status);
        Assert.Equal("GET /Reports", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetReportsAsyncReturnsAnEmptyCollectionWhenThereAreNoReports()
    {
        _backend.OnGet("/Reports", new List<Report>());

        var reports = await _service.GetReportsAsync();

        Assert.Empty(reports);
    }

    [Fact]
    public async Task TestGetReportsAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Reports", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetReportsAsync());

        Assert.Equal("Error listing reports ", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetReportsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Reports", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetReportsAsync());

        Assert.Equal("Error listing reports", ex.RestExceptionMessage);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestGetReportsAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Reports");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetReportsAsync());
    }

    // ---------- CreateReportAsync ----------

    [Fact]
    public async Task TestCreateReportAsyncPostsTheDtoAndReturnsTheSavedReport()
    {
        _backend.OnPost("/Reports", SavedReport(15, "QuarterlyVulnerabilityReport"));

        var created = await _service.CreateReportAsync(Dto());

        Assert.Equal(15, created.Id);
        Assert.Equal("QuarterlyVulnerabilityReport", created.Name);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/Reports", _backend.LastRequest.Path);
        Assert.Contains("QuarterlyVulnerabilityReport", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateReportAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Reports", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateReportAsync(Dto()));

        Assert.Equal("Error creating reports ", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateReportAsyncWrapsARejectedRequest()
    {
        _backend.OnStatus(Method.Post, "/Reports", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateReportAsync(Dto()));

        Assert.Equal("Error creating report", ex.RestExceptionMessage);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateReportAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Reports");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateReportAsync(Dto()));
    }

    // ---------- DeleteReportAsync ----------

    [Fact]
    public async Task TestDeleteReportAsyncSendsTheDelete()
    {
        _backend.OnDelete("/Reports/15", "");

        await _service.DeleteReportAsync(15);

        Assert.Equal("DELETE", _backend.LastRequest.Method);
        Assert.Equal("/Reports/15", _backend.LastRequest.Path);
    }

    [Fact]
    public async Task TestDeleteReportAsyncWrapsATransportFailure()
    {
        // DeleteReportAsync only guards against a null response (which RestSharp never returns for
        // the untyped DeleteAsync), so a failed delete only reaches the caller as a transport error.
        _backend.OnTransportFailure(Method.Delete, "/Reports/15");

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteReportAsync(15));

        Assert.Equal("Error deleting report", ex.RestExceptionMessage);
    }
}
