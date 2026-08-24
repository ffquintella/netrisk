using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Assessments;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(ImportsController))]
public class ImportsControllerTest : BaseControllerTest
{
    private readonly IImportsService _importsService = Substitute.For<IImportsService>();
    private readonly ImportsController _controller;

    private static FormFile MakeFile(string fileName, string content = "{\"name\":\"template\"}")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    public ImportsControllerTest()
    {
        _importsService.ImportAssessmentFromJsonAsync(Arg.Any<string>())
            .Returns(new Assessment { Id = 11, Name = "From json", Created = new DateTime(2024, 1, 1) });
        _importsService.ImportAssessmentFromExcelAsync(Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(new Assessment { Id = 12, Name = "From excel", Created = new DateTime(2024, 1, 1) });
        _importsService.PreviewAssessmentFromJsonAsync(Arg.Any<string>())
            .Returns(new AssessmentImportPreview { Valid = true, Name = "From json", QuestionCount = 3 });
        _importsService.PreviewAssessmentFromExcelAsync(Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(new AssessmentImportPreview { Valid = true, Name = "From excel", QuestionCount = 4 });

        _controller = ResolveController<ImportsController>(s => s.AddSingleton(_importsService));
    }

    private static ImportsController FailingController()
    {
        var failing = Substitute.For<IImportsService>();
        failing.ImportAssessmentFromJsonAsync(Arg.Any<string>())
            .Returns<Task<Assessment>>(_ => throw new InvalidOperationException("boom"));
        failing.PreviewAssessmentFromJsonAsync(Arg.Any<string>())
            .Returns<Task<AssessmentImportPreview>>(_ => throw new InvalidOperationException("boom"));

        return ResolveController<ImportsController>(s => s.AddSingleton(failing));
    }

    [Fact]
    public async Task TestImportAssessmentFromJson()
    {
        var result = await _controller.ImportAssessment(null, MakeFile("template.json"));

        var created = Assert.IsType<CreatedResult>(result.Result);
        var assessment = Assert.IsType<Assessment>(created.Value);
        Assert.Equal(11, assessment.Id);
    }

    [Fact]
    public async Task TestImportAssessmentFromExcelUsesSuppliedName()
    {
        var result = await _controller.ImportAssessment("Given name", MakeFile("template.xlsx"));

        Assert.IsType<CreatedResult>(result.Result);
        await _importsService.Received(1).ImportAssessmentFromExcelAsync(Arg.Any<Stream>(), "Given name");
    }

    [Fact]
    public async Task TestImportAssessmentFromExcelFallsBackToFileName()
    {
        var result = await _controller.ImportAssessment("", MakeFile("template.xlsx"));

        Assert.IsType<CreatedResult>(result.Result);
        await _importsService.Received(1).ImportAssessmentFromExcelAsync(Arg.Any<Stream>(), "template");
    }

    [Fact]
    public async Task TestImportAssessmentRejectsMissingFile()
    {
        var result = await _controller.ImportAssessment("name", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestImportAssessmentRejectsEmptyFile()
    {
        var result = await _controller.ImportAssessment("name", MakeFile("template.json", ""));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestImportAssessmentRejectsUnsupportedExtension()
    {
        var result = await _controller.ImportAssessment("name", MakeFile("template.csv", "a,b,c"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(".csv", bad.Value.ToString());
    }

    [Fact]
    public async Task TestImportAssessmentInternalError()
    {
        var result = await FailingController().ImportAssessment(null, MakeFile("template.json"));

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }

    [Fact]
    public async Task TestPreviewAssessmentFromJson()
    {
        var result = await _controller.PreviewAssessment(null, MakeFile("template.json"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var preview = Assert.IsType<AssessmentImportPreview>(ok.Value);
        Assert.True(preview.Valid);
        Assert.Equal(3, preview.QuestionCount);
    }

    [Fact]
    public async Task TestPreviewAssessmentFromExcelUsesSuppliedName()
    {
        var result = await _controller.PreviewAssessment("Given name", MakeFile("template.xlsx"));

        Assert.IsType<OkObjectResult>(result.Result);
        await _importsService.Received(1).PreviewAssessmentFromExcelAsync(Arg.Any<Stream>(), "Given name");
    }

    [Fact]
    public async Task TestPreviewAssessmentFromExcelFallsBackToFileName()
    {
        var result = await _controller.PreviewAssessment(null, MakeFile("template.xlsx"));

        Assert.IsType<OkObjectResult>(result.Result);
        await _importsService.Received(1).PreviewAssessmentFromExcelAsync(Arg.Any<Stream>(), "template");
    }

    [Fact]
    public async Task TestPreviewAssessmentRejectsMissingFile()
    {
        var result = await _controller.PreviewAssessment("name", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestPreviewAssessmentRejectsEmptyFile()
    {
        var result = await _controller.PreviewAssessment("name", MakeFile("template.json", ""));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestPreviewAssessmentRejectsUnsupportedExtension()
    {
        var result = await _controller.PreviewAssessment("name", MakeFile("template.txt", "junk"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(".txt", bad.Value.ToString());
    }

    [Fact]
    public async Task TestPreviewAssessmentInternalError()
    {
        var result = await FailingController().PreviewAssessment(null, MakeFile("template.json"));

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, status.StatusCode);
    }
}
