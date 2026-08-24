using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
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

[TestSubject(typeof(ReportTemplatesRestService))]
public class ReportTemplatesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IReportTemplatesService _service;

    public ReportTemplatesRestServiceTest()
    {
        _service = ResolveWith<IReportTemplatesService>(_backend);
    }

    private static ReportTemplate Template(int id, string name) => new()
    {
        Id = id,
        Name = name,
        Description = "desc-" + id,
        OwnerId = 3,
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ReportTemplateCreateDto CreateDto() => new()
    {
        Name = "QuarterlyBoardPack",
        Description = "for the board",
        LayoutJson = "{\"blocks\":[\"cover\"]}",
        BrandingJson = "{\"color\":\"#101010\"}"
    };

    private static ReportTemplateUpdateDto UpdateDto() => new()
    {
        Name = "RenamedBoardPack",
        Description = "still for the board",
        LayoutJson = "{\"blocks\":[\"summary\"]}",
        BrandingJson = "{\"color\":\"#202020\"}"
    };

    // ---------- GetAllAsync ----------

    [Fact]
    public async Task TestGetAllAsyncReturnsTheTemplates()
    {
        _backend.OnGet("/ReportTemplates", new List<ReportTemplate> { Template(1, "Alpha"), Template(2, "Beta") });

        var templates = await _service.GetAllAsync();

        Assert.Equal(2, templates.Count);
        Assert.Equal("Alpha", templates[0].Name);
        Assert.Equal(2, templates[1].Id);
        Assert.Equal(3, templates[0].OwnerId);
        Assert.Equal("GET /ReportTemplates", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/ReportTemplates", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());

        Assert.Equal("Error listing report templates", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/ReportTemplates", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/ReportTemplates");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task TestGetByIdAsyncReturnsTheTemplate()
    {
        _backend.OnGet("/ReportTemplates/12", Template(12, "Gamma"));

        var template = await _service.GetByIdAsync(12);

        Assert.Equal(12, template.Id);
        Assert.Equal("Gamma", template.Name);
        Assert.Equal("desc-12", template.Description);
        Assert.True(_backend.Sent(Method.Get, "/ReportTemplates/12"));
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenTheTemplateIsMissing()
    {
        _backend.OnStatus(Method.Get, "/ReportTemplates/404", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(404));

        Assert.Equal("Error getting report template 404", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/ReportTemplates/7", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(7));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task TestCreateAsyncPostsTheDtoAndReturnsTheSavedTemplate()
    {
        _backend.OnPost("/ReportTemplates", Template(31, "QuarterlyBoardPack"));

        var created = await _service.CreateAsync(CreateDto());

        Assert.Equal(31, created.Id);
        Assert.Equal("QuarterlyBoardPack", created.Name);
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/ReportTemplates", _backend.LastRequest.Path);
        Assert.Contains("QuarterlyBoardPack", _backend.LastRequest.Body);
        Assert.Contains("cover", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/ReportTemplates", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(CreateDto()));

        Assert.Equal("Error creating report template", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateAsyncWrapsARejectedRequest()
    {
        _backend.OnStatus(Method.Post, "/ReportTemplates", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(CreateDto()));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task TestCreateAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/ReportTemplates");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.CreateAsync(CreateDto()));
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task TestUpdateAsyncPutsTheDtoAndReturnsTheSavedTemplate()
    {
        _backend.OnPut("/ReportTemplates/31", Template(31, "RenamedBoardPack"));

        var updated = await _service.UpdateAsync(31, UpdateDto());

        Assert.Equal("RenamedBoardPack", updated.Name);
        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal("/ReportTemplates/31", _backend.LastRequest.Path);
        Assert.Contains("RenamedBoardPack", _backend.LastRequest.Body);
        Assert.Contains("summary", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/ReportTemplates/31", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(31, UpdateDto()));

        Assert.Equal("Error updating report template 31", ex.RestExceptionMessage);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/ReportTemplates/31", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.UpdateAsync(31, UpdateDto()));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ---------- RenderPreviewAsync ----------

    [Fact]
    public async Task TestRenderPreviewAsyncReturnsTheRenderedBytes()
    {
        // ExecutePostAsync hands back the raw body, so the "PNG" here is just a recognisable payload.
        _backend.OnPost("/ReportTemplates/preview", "RENDERED-PREVIEW-BYTES");

        var bytes = await _service.RenderPreviewAsync("{\"blocks\":[\"cover\"]}", "{\"color\":\"#303030\"}", "Preview Title");

        Assert.Equal("RENDERED-PREVIEW-BYTES", Encoding.UTF8.GetString(bytes));
        Assert.Equal("POST", _backend.LastRequest.Method);
        Assert.Equal("/ReportTemplates/preview", _backend.LastRequest.Path);
        Assert.Contains("Preview Title", _backend.LastRequest.Body);
        Assert.Contains("cover", _backend.LastRequest.Body);
        Assert.Contains("#303030", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestRenderPreviewAsyncThrowsOnANonSuccessStatus()
    {
        // The untyped ExecutePostAsync never throws on status, so the service's own
        // IsSuccessStatusCode guard is what raises here.
        _backend.OnStatus(Method.Post, "/ReportTemplates/preview", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.RenderPreviewAsync("{}", "{}", "T"));

        Assert.Equal("Error rendering report template preview", ex.RestExceptionMessage);
    }

    [Fact]
    public async Task TestRenderPreviewAsyncThrowsOnATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/ReportTemplates/preview");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RenderPreviewAsync("{}", "{}", "T"));
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task TestDeleteAsyncSendsTheDelete()
    {
        _backend.OnDelete("/ReportTemplates/44", "");

        await _service.DeleteAsync(44);

        Assert.Equal("DELETE", _backend.LastRequest.Method);
        Assert.Equal("/ReportTemplates/44", _backend.LastRequest.Path);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TestDeleteAsyncThrowsWhenTheServerRefuses(HttpStatusCode status)
    {
        _backend.OnStatus(Method.Delete, "/ReportTemplates/44", status);

        var ex = await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(44));

        Assert.Contains("44", ex.Message);
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/ReportTemplates/44");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(44));
    }
}
