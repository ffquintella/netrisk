using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The controller reads and writes the database directly, so it gets an
/// <see cref="InMemoryDalService"/> over a database name unique to each test instance — xUnit
/// builds one instance per test method, which keeps the seeded rows of one test out of another.
/// </summary>
[TestSubject(typeof(ReportTemplatesController))]
public class ReportTemplatesControllerTest : BaseControllerTest
{
    private static readonly DateTime Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] PreviewImage = { 8, 9, 10 };

    private readonly InMemoryDalService _dalService = new InMemoryDalService(Guid.NewGuid().ToString());
    private readonly IQuestPdfRenderingService _renderingService = Substitute.For<IQuestPdfRenderingService>();
    private readonly ReportTemplatesController _controller;

    public ReportTemplatesControllerTest()
    {
        _renderingService
            .RenderPreviewImageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(PreviewImage);

        Seed();

        _controller = ResolveController<ReportTemplatesController>(services =>
        {
            services.AddSingleton<IDalService>(_dalService);
            services.AddSingleton(_renderingService);
        });
    }

    /// <summary>Template 1 owns one version; template 2 owns none, for the first-version branch.</summary>
    private void Seed()
    {
        using var context = _dalService.GetContext();

        // ReportTemplate.Owner is a required navigation, so the read actions' Include(t => t.Owner)
        // inner-joins: without this row the templates below come back as an empty list.
        context.Users.Add(new User
        {
            Value = 1,
            Enabled = true,
            Name = "testUser",
            Login = "testUser",
            Email = "testUser@teste.com",
            Type = "local",
            Password = "testUser"u8.ToArray(),
            RoleId = 1,
            Admin = true
        });

        context.ReportTemplates.Add(new ReportTemplate
        {
            Id = 1,
            Name = "Quarterly Risk Report",
            Description = "Board facing",
            OwnerId = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp
        });

        context.ReportTemplates.Add(new ReportTemplate
        {
            Id = 2,
            Name = "Ad hoc Report",
            OwnerId = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp
        });

        context.ReportTemplateVersions.Add(new ReportTemplateVersion
        {
            Id = 1,
            TemplateId = 1,
            Version = 1,
            LayoutJson = "{\"sections\":[]}",
            BrandingJson = "{\"color\":\"blue\"}",
            CreatedAt = Timestamp
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task TestPreviewReturnsThePngRenderedByTheService()
    {
        var request = new PreviewTemplateRequest
        {
            LayoutJson = "{\"sections\":[]}",
            BrandingJson = "{\"color\":\"red\"}",
            ReportTitle = "My Preview"
        };

        var result = await _controller.Preview(request);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(PreviewImage, file.FileContents);

        await _renderingService.Received(1)
            .RenderPreviewImageAsync("{\"sections\":[]}", "{\"color\":\"red\"}", "My Preview");
    }

    [Fact]
    public async Task TestPreviewFallsBackToEmptyJsonAndDefaultTitle()
    {
        var result = await _controller.Preview(new PreviewTemplateRequest());

        Assert.IsType<FileContentResult>(result);

        await _renderingService.Received(1).RenderPreviewImageAsync(string.Empty, string.Empty, "Report Preview");
    }

    [Fact]
    public async Task TestGetAllReturnsEveryTemplate()
    {
        var result = await _controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var templates = Assert.IsType<List<ReportTemplate>>(ok.Value);

        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, t => t.Name == "Quarterly Risk Report");
    }

    [Fact]
    public async Task TestGetByIdReturnsTheTemplate()
    {
        var result = await _controller.GetById(1);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var template = Assert.IsType<ReportTemplate>(ok.Value);

        Assert.Equal(1, template.Id);
        Assert.Equal("Quarterly Risk Report", template.Name);
        Assert.Single(template.Versions);
    }

    [Fact]
    public async Task TestGetByIdReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.GetById(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Report template with ID 999 not found", notFound.Value);
    }

    [Fact]
    public async Task TestCreateStoresTheTemplateAndItsFirstVersion()
    {
        var request = new CreateTemplateRequest
        {
            Name = "New Template",
            Description = "First cut",
            LayoutJson = "{\"sections\":[1]}",
            BrandingJson = "{\"color\":\"green\"}"
        };

        var result = await _controller.Create(request);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var template = Assert.IsType<ReportTemplate>(created.Value);

        Assert.Equal("New Template", template.Name);
        Assert.Equal("First cut", template.Description);
        Assert.Equal(1, template.OwnerId);
        Assert.Equal($"ReportTemplates/{template.Id}", created.Location);

        using var context = _dalService.GetContext();
        var version = Assert.Single(context.ReportTemplateVersions.Where(v => v.TemplateId == template.Id).ToList());
        Assert.Equal(1, version.Version);
        Assert.Equal("{\"sections\":[1]}", version.LayoutJson);
        Assert.Equal("{\"color\":\"green\"}", version.BrandingJson);
    }

    [Fact]
    public async Task TestUpdateRenamesTheTemplateAndAppendsANewVersion()
    {
        var request = new UpdateTemplateRequest
        {
            Name = "Renamed",
            Description = "Revised",
            LayoutJson = "{\"sections\":[2]}",
            BrandingJson = "{\"color\":\"black\"}"
        };

        var result = await _controller.Update(1, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var template = Assert.IsType<ReportTemplate>(ok.Value);

        Assert.Equal("Renamed", template.Name);
        Assert.Equal("Revised", template.Description);

        using var context = _dalService.GetContext();
        var versions = context.ReportTemplateVersions
            .Where(v => v.TemplateId == 1)
            .OrderBy(v => v.Version)
            .ToList();

        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[1].Version);
        Assert.Equal("{\"sections\":[2]}", versions[1].LayoutJson);
    }

    /// <summary>A template with no versions yet must get version 1, not version 0.</summary>
    [Fact]
    public async Task TestUpdateOfATemplateWithoutVersionsStartsAtVersionOne()
    {
        var request = new UpdateTemplateRequest
        {
            Name = "Ad hoc Report v2",
            LayoutJson = "{}",
            BrandingJson = "{}"
        };

        var result = await _controller.Update(2, request);

        Assert.IsType<OkObjectResult>(result.Result);

        using var context = _dalService.GetContext();
        var version = Assert.Single(context.ReportTemplateVersions.Where(v => v.TemplateId == 2).ToList());
        Assert.Equal(1, version.Version);
    }

    [Fact]
    public async Task TestUpdateReturnsNotFoundForAnUnknownId()
    {
        var request = new UpdateTemplateRequest { Name = "x", LayoutJson = "{}", BrandingJson = "{}" };

        var result = await _controller.Update(999, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal("Report template with ID 999 not found", notFound.Value);
    }

    [Fact]
    public async Task TestDeleteRemovesTheTemplate()
    {
        var result = await _controller.Delete(2);

        Assert.IsType<NoContentResult>(result);

        using var context = _dalService.GetContext();
        Assert.Empty(context.ReportTemplates.Where(t => t.Id == 2).ToList());
    }

    [Fact]
    public async Task TestDeleteReturnsNotFoundForAnUnknownId()
    {
        var result = await _controller.Delete(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Report template with ID 999 not found", notFound.Value);
    }
}
