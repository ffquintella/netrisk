using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Models;
using Sieve.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The export endpoint pulls its rows straight from the context and hands them to
/// <see cref="IExportService"/>, so it runs here over the in-memory provider with the real
/// <see cref="ApplicationSieveProcessor"/> and a substituted export service.
/// </summary>
[TestSubject(typeof(ExportController))]
public class ExportControllerTest : BaseControllerTest
{
    private static readonly byte[] RiskBytes = [1, 2, 3];
    private static readonly byte[] VulnerabilityBytes = [4, 5];
    private static readonly byte[] HostBytes = [6];
    private static readonly byte[] IncidentBytes = [7, 8];

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IExportService _exportService = Substitute.For<IExportService>();
    private readonly ExportController _controller;

    public ExportControllerTest()
    {
        using (var ctx = _dal.GetContext())
        {
            ctx.Risks.Add(new Risk
            {
                Id = 1,
                Status = "New",
                Subject = "A risk",
                ReferenceId = "REF-1",
                Assessment = "",
                Notes = "",
                RiskCatalogMapping = "",
                ThreatCatalogMapping = "",
                SubmissionDate = new DateTime(2024, 1, 1),
                LastUpdate = new DateTime(2024, 2, 1),
                TemplateGroupId = 0
            });

            ctx.Hosts.Add(new DAL.Entities.Host
            {
                Id = 1,
                HostName = "server-1",
                Ip = "10.0.0.1",
                Status = 1,
                Source = "test",
                RegistrationDate = new DateTime(2024, 1, 1)
            });

            ctx.Vulnerabilities.AddRange(
                new Vulnerability
                {
                    Id = 1,
                    Title = "Open port",
                    Severity = "High",
                    Status = 1,
                    DetectionCount = 1,
                    FirstDetection = new DateTime(2024, 1, 1),
                    LastDetection = new DateTime(2024, 1, 2)
                },
                new Vulnerability
                {
                    Id = 2,
                    Title = "Weak cipher",
                    Severity = "Low",
                    Status = 1,
                    DetectionCount = 2,
                    FirstDetection = new DateTime(2024, 1, 3),
                    LastDetection = new DateTime(2024, 1, 4)
                });

            ctx.Incidents.Add(new Incident { Id = 1, Name = "INC-1", Description = "An incident" });

            ctx.SaveChanges();
        }

        _exportService
            .ExportAsync(Arg.Any<IEnumerable<Risk>>(), Arg.Any<ExportFormat>(), Arg.Any<string>())
            .Returns(Task.FromResult(RiskBytes));
        _exportService
            .ExportAsync(Arg.Any<IEnumerable<Vulnerability>>(), Arg.Any<ExportFormat>(), Arg.Any<string>())
            .Returns(Task.FromResult(VulnerabilityBytes));
        _exportService
            .ExportAsync(Arg.Any<IEnumerable<DAL.Entities.Host>>(), Arg.Any<ExportFormat>(), Arg.Any<string>())
            .Returns(Task.FromResult(HostBytes));
        _exportService
            .ExportAsync(Arg.Any<IEnumerable<Incident>>(), Arg.Any<ExportFormat>(), Arg.Any<string>())
            .Returns(Task.FromResult(IncidentBytes));

        _controller = ResolveController<ExportController>(s =>
        {
            s.AddSingleton<IDalService>(_dal);
            s.AddSingleton(_exportService);
            s.Configure<SieveOptions>(options =>
            {
                options.DefaultPageSize = 100;
                options.MaxPageSize = 1000;
                options.CaseSensitive = false;
                // A filter the mapper does not know must not break an export.
                options.ThrowExceptions = false;
            });
            s.AddSingleton<ILocalizationService>(sp =>
                new LocalizationService(sp.GetRequiredService<ILoggerFactory>(), typeof(ApplicationSieveProcessor).Assembly));
            s.AddScoped<ISieveProcessor, ApplicationSieveProcessor>();
        });
    }

    [Fact]
    public async Task TestExportRisksReturnsTheGeneratedFile()
    {
        var result = await _controller.Export("csv", "risk", new SieveModel());

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(RiskBytes, file.FileContents);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("Export.csv", file.FileDownloadName);

        await _exportService.Received(1)
            .ExportAsync(Arg.Any<IEnumerable<Risk>>(), ExportFormat.Csv, "Export");
    }

    [Fact]
    public async Task TestExportVulnerabilities()
    {
        var result = await _controller.Export("xlsx", "vulnerability", new SieveModel { Sorts = "Id" }, "Vulns");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(VulnerabilityBytes, file.FileContents);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal("Vulns.xlsx", file.FileDownloadName);

        await _exportService.Received(1)
            .ExportAsync(Arg.Any<IEnumerable<Vulnerability>>(), ExportFormat.Xlsx, "Vulns");
    }

    [Fact]
    public async Task TestExportHosts()
    {
        var result = await _controller.Export("pdf", "host", new SieveModel(), "Hosts");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(HostBytes, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("Hosts.pdf", file.FileDownloadName);

        await _exportService.Received(1)
            .ExportAsync(Arg.Any<IEnumerable<DAL.Entities.Host>>(), ExportFormat.Pdf, "Hosts");
    }

    [Fact]
    public async Task TestExportIncidents()
    {
        var result = await _controller.Export("Csv", "Incident", new SieveModel(), "Incidents");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(IncidentBytes, file.FileContents);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("Incidents.csv", file.FileDownloadName);

        await _exportService.Received(1)
            .ExportAsync(Arg.Any<IEnumerable<Incident>>(), ExportFormat.Csv, "Incidents");
    }

    /// <summary>A quote in the title would break the Content-Disposition header, so it is swapped out.</summary>
    [Fact]
    public async Task TestExportSanitizesQuotesInTheReportTitle()
    {
        var result = await _controller.Export("csv", "risk", new SieveModel(), "My \"report\"");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("My 'report'.csv", file.FileDownloadName);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("")]
    [InlineData("json")]
    public async Task TestExportRejectsAnUnsupportedFormat(string format)
    {
        var result = await _controller.Export(format, "risk", new SieveModel());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal($"Unsupported format: {format}", (string)badRequest.Value);
    }

    [Fact]
    public async Task TestExportRejectsAnUnsupportedEntityType()
    {
        var result = await _controller.Export("csv", "banana", new SieveModel());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Unsupported entity type for export: banana", (string)badRequest.Value);
    }

    [Fact]
    public async Task TestExportReturnsInternalServerErrorWhenTheExportServiceFails()
    {
        _exportService
            .ExportAsync(Arg.Any<IEnumerable<Risk>>(), Arg.Any<ExportFormat>(), Arg.Any<string>())
            .Throws(new InvalidOperationException("boom"));

        var result = await _controller.Export("csv", "risk", new SieveModel());

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.Equal("Internal server error occurred during export", objectResult.Value);
    }
}
