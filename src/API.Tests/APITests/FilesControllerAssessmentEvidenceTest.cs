using System;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// GitHub #80 (T297, S44 D4) — the generic <c>/Files</c> write routes refuse assessment evidence.
///
/// Evidence is created and deleted only through the assessment evidence endpoints, which know whether the
/// run is still open, the per-answer limit and the evidence size cap. Each test below would otherwise be a
/// way around one of those checks: a create or completion that attaches straight to an answer, a save
/// that moves a file onto one, and a delete that strips a submitted run of its evidence. In every case the
/// files service must not be reached.
/// </summary>
[TestSubject(typeof(FilesController))]
public class FilesControllerAssessmentEvidenceTest : BaseControllerTest
{
    private readonly IFilesService _files = Substitute.For<IFilesService>();
    private readonly FilesController _controller;

    public FilesControllerAssessmentEvidenceTest()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(System.IO.Path.GetTempPath());
        environment.WebRootPath.Returns(System.IO.Path.GetTempPath());

        _controller = ResolveController<FilesController>(s =>
        {
            s.AddSingleton(_files);
            s.AddSingleton(environment);
            s.AddSingleton(MockedFileAccessAuthorizer.Create());
        });
    }

    /// <summary>Owned by the mocked logged-in user (1), so the ownership guards let it through to ours.</summary>
    private static NrFile EvidenceFile(int? answerId = 50) => new()
    {
        Id = 7, Name = "badge.png", UniqueName = "u-7", Type = "3", Size = 3, User = 1,
        Content = [1, 2, 3], Timestamp = DateTime.UtcNow, AssessmentRunAnswerId = answerId
    };

    private static void AssertRefused(IActionResult? result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains(FilesController.AssessmentEvidenceRouteError, JsonSerializer.Serialize(bad.Value));
    }

    [Fact]
    public async Task TestCreateRefusesEvidence()
    {
        AssertRefused((await _controller.CreateFile(EvidenceFile())).Result);

        _files.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task TestCompletingAnUploadRefusesEvidence()
    {
        AssertRefused((await _controller.CompleteLocalFile(EvidenceFile(), "upload-1", 1)).Result);

        _files.DidNotReceiveWithAnyArgs().CompleteChunkedUpload(default!, default!, default, default!);
        _files.DidNotReceiveWithAnyArgs().CompleteChunkedUpload(default!, default!, default, default!, default);
    }

    [Fact]
    public void TestSavingCannotMoveAFileOntoAnAnswer()
    {
        AssertRefused(_controller.SaveFile("u-7", EvidenceFile()).Result);

        _files.DidNotReceiveWithAnyArgs().Save(default!, default!);
    }

    [Fact]
    public void TestDeletingEvidenceIsRefused()
    {
        _files.GetByUniqueName("u-7").Returns(EvidenceFile());

        AssertRefused(_controller.DeleteFile("u-7"));

        _files.DidNotReceiveWithAnyArgs().DeleteByUniqueName(default!);
    }

    /// <summary>The guard is specific: an ordinary attachment still goes through.</summary>
    [Fact]
    public void TestAnOrdinaryFileIsStillDeleted()
    {
        _files.GetByUniqueName("u-7").Returns(EvidenceFile(answerId: null));

        Assert.IsType<OkResult>(_controller.DeleteFile("u-7"));

        _files.Received(1).DeleteByUniqueName("u-7");
    }
}
