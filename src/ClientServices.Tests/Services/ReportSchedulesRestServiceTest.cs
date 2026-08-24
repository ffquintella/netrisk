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

[TestSubject(typeof(ReportSchedulesRestService))]
public class ReportSchedulesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IReportSchedulesService _service;

    public ReportSchedulesRestServiceTest()
    {
        _service = ResolveWith<IReportSchedulesService>(_backend);
    }

    private static ReportSchedule Schedule(int id, string cron = "0 6 * * 1") => new()
    {
        Id = id,
        ReportTemplateVersionId = 5,
        FrequencyCron = cron,
        Timezone = "America/Sao_Paulo",
        RecipientsJson = "[\"ciso@example.org\"]",
        IsEnabled = true,
        LastRunAt = new DateTime(2024, 3, 3, 6, 0, 0, DateTimeKind.Utc),
        LastStatus = "Success"
    };

    private static ReportScheduleCreateDto CreateDto() => new()
    {
        ReportTemplateVersionId = 5,
        FrequencyCron = "0 6 * * 1",
        Timezone = "America/Sao_Paulo",
        RecipientsJson = "[\"ciso@example.org\"]",
        IsEnabled = true
    };

    private static ReportScheduleUpdateDto UpdateDto() => new()
    {
        ReportTemplateVersionId = 6,
        FrequencyCron = "30 7 * * 5",
        Timezone = "UTC",
        RecipientsJson = "[\"board@example.org\"]",
        IsEnabled = false
    };

    // ---------- GetAllAsync ----------

    [Fact]
    public async Task TestGetAllAsyncReturnsTheSchedules()
    {
        _backend.OnGet("/ReportSchedules", new List<ReportSchedule> { Schedule(1), Schedule(2, "0 8 * * *") });

        var schedules = await _service.GetAllAsync();

        Assert.Equal(2, schedules.Count);
        Assert.Equal("0 6 * * 1", schedules[0].FrequencyCron);
        Assert.Equal("0 8 * * *", schedules[1].FrequencyCron);
        Assert.Equal("America/Sao_Paulo", schedules[0].Timezone);
        Assert.True(schedules[0].IsEnabled);
        Assert.Equal("GET /ReportSchedules", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/ReportSchedules", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());

        Assert.Equal("Error listing report schedules", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/ReportSchedules", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/ReportSchedules");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task TestGetByIdAsyncReturnsTheSchedule()
    {
        _backend.OnGet("/ReportSchedules/9", Schedule(9));

        var schedule = await _service.GetByIdAsync(9);

        Assert.Equal(9, schedule.Id);
        Assert.Equal(5, schedule.ReportTemplateVersionId);
        Assert.Equal("Success", schedule.LastStatus);
        Assert.True(_backend.Sent(Method.Get, "/ReportSchedules/9"));
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenTheScheduleIsMissing()
    {
        _backend.OnStatus(Method.Get, "/ReportSchedules/9", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(9));

        Assert.Equal("Error getting report schedule 9", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/ReportSchedules/9", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(9));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task TestCreateAsyncPostsTheDtoAndReturnsTheSavedSchedule()
    {
        _backend.OnPost("/ReportSchedules", Schedule(17));

        var created = await _service.CreateAsync(CreateDto());

        Assert.Equal(17, created.Id);
        Assert.Equal("0 6 * * 1", created.FrequencyCron);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/ReportSchedules", _backend.LastRequest.Path);
        Assert.Contains("America/Sao_Paulo", _backend.LastRequest.Body);
        Assert.Contains("ciso@example.org", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/ReportSchedules", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(CreateDto()));

        Assert.Equal("Error creating report schedule", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateAsyncWrapsARejectedRequest()
    {
        _backend.OnStatus(Method.Post, "/ReportSchedules", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(CreateDto()));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task TestUpdateAsyncPutsTheDtoAndReturnsTheSavedSchedule()
    {
        _backend.OnPut("/ReportSchedules/17", Schedule(17, "30 7 * * 5"));

        var updated = await _service.UpdateAsync(17, UpdateDto());

        Assert.Equal("30 7 * * 5", updated.FrequencyCron);
        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal("/ReportSchedules/17", _backend.LastRequest.Path);
        Assert.Contains("board@example.org", _backend.LastRequest.Body);
        Assert.Contains("30 7 * * 5", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/ReportSchedules/17", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(17, UpdateDto()));

        Assert.Equal("Error updating report schedule 17", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/ReportSchedules/17", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(17, UpdateDto()));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task TestDeleteAsyncSendsTheDelete()
    {
        _backend.OnDelete("/ReportSchedules/21", "");

        await _service.DeleteAsync(21);

        Assert.Equal("DELETE", _backend.LastRequest.Method);
        Assert.Equal("/ReportSchedules/21", _backend.LastRequest.Path);
    }

    [Fact]
    public async Task TestDeleteAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Delete, "/ReportSchedules/21", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(21));

        Assert.Contains("21", ex.Message);
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/ReportSchedules/21");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(21));
    }

    // ---------- TriggerTestAsync ----------

    [Fact]
    public async Task TestTriggerTestAsyncPostsToTheTestEndpoint()
    {
        _backend.OnPost("/ReportSchedules/21/test", "");

        await _service.TriggerTestAsync(21);

        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/ReportSchedules/21/test", _backend.LastRequest.Path);
    }

    [Fact]
    public async Task TestTriggerTestAsyncThrowsWhenTheServerRefuses()
    {
        _backend.OnStatus(Method.Post, "/ReportSchedules/21/test", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.TriggerTestAsync(21));

        Assert.Contains("21", ex.Message);
    }

    [Fact]
    public async Task TestTriggerTestAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/ReportSchedules/21/test");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.TriggerTestAsync(21));
    }
}
