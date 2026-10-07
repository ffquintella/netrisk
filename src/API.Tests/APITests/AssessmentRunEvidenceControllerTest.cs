using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Assessments;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;
using M = API.Tests.Mock.MockedAssessmentRunEvidenceService;

namespace API.Tests.APITests;

/// <summary>
/// GitHub #80 (T297, S44) — the comment and evidence endpoints of an assessment run: each answers its
/// happy path, and each domain exception maps onto its status code (400, 403, 404, 409, 500).
/// </summary>
[TestSubject(typeof(AssessmentRunEvidenceController))]
public class AssessmentRunEvidenceControllerTest : BaseControllerTest
{
    private readonly IAssessmentRunEvidenceService _service = M.Create();
    private readonly AssessmentRunEvidenceController _controller;

    public AssessmentRunEvidenceControllerTest()
    {
        _controller = ResolveController<AssessmentRunEvidenceController>(s => s.AddSingleton(_service));
    }

    private static AssessmentEvidenceUploadRequest Upload() => new()
    {
        FileId = "6f1c2c4e-0d7b-4a53-9a1e-2b9d8c7e5f10", TotalChunks = 1, Name = "badge.png", Type = "3"
    };

    private static int StatusOf(IActionResult? result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => -1
    };

    /// <summary>
    /// Every action is behind the assessments policy, declared once on the class — the same gate as the
    /// run endpoints of <see cref="AssessmentsController"/>.
    /// </summary>
    [Fact]
    public void TestTheControllerRequiresAssessmentAccess()
    {
        var authorize = typeof(AssessmentRunEvidenceController).GetCustomAttributes<AuthorizeAttribute>().ToList();

        Assert.Contains(authorize, a => a.Policy == "RequireAssessmentAccess");
        Assert.Empty(typeof(AssessmentRunEvidenceController).GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.All(typeof(AssessmentRunEvidenceController).GetMethods(),
            m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
    }

    // --- comment -----------------------------------------------------------------------------------

    [Fact]
    public async Task TestSaveCommentReturnsTheAnswer()
    {
        var result = await _controller.SaveComment(M.Open, 100,
            new AssessmentAnswerCommentRequest { Comment = "Badge log checked" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var answer = Assert.IsType<AssessmentRunAnswer>(ok.Value);
        Assert.Equal(100, answer.AssessmentQuestionId);
        Assert.Equal("Badge log checked", answer.Comment);
        await _service.Received(1).SaveCommentAsync(M.Open, 100, "Badge log checked");
    }

    /// <summary>A missing body is a cleared comment, not an error.</summary>
    [Fact]
    public async Task TestSaveCommentWithoutABodyClearsIt()
    {
        var result = await _controller.SaveComment(M.Open, 100, null);

        Assert.IsType<OkObjectResult>(result.Result);
        await _service.Received(1).SaveCommentAsync(M.Open, 100, null);
    }

    [Theory]
    [InlineData(M.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(M.Missing, StatusCodes.Status404NotFound)]
    [InlineData(M.Submitted, StatusCodes.Status409Conflict)]
    [InlineData(M.Broken, StatusCodes.Status500InternalServerError)]
    public async Task TestSaveCommentMapsFailures(int runId, int status)
    {
        var result = await _controller.SaveComment(runId, 100, new AssessmentAnswerCommentRequest { Comment = "x" });

        Assert.Equal(status, StatusOf(result.Result));
    }

    [Fact]
    public async Task TestASubmittedRunNamesTheRule()
    {
        var result = await _controller.SaveComment(M.Submitted, 100, new AssessmentAnswerCommentRequest());

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("run_submitted", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
    }

    // --- list --------------------------------------------------------------------------------------

    [Fact]
    public async Task TestGetRunEvidenceListsEveryFile()
    {
        var result = await _controller.GetRunEvidence(M.Open);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<AssessmentAnswerEvidence>>(ok.Value);
        Assert.Equal([100, 101], list.Select(e => e.QuestionId));
    }

    [Theory]
    [InlineData(M.Missing, StatusCodes.Status404NotFound)]
    [InlineData(M.Broken, StatusCodes.Status500InternalServerError)]
    public async Task TestGetRunEvidenceMapsFailures(int runId, int status)
    {
        var result = await _controller.GetRunEvidence(runId);

        Assert.Equal(status, StatusOf(result.Result));
    }

    // --- attach ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAttachEvidenceReturnsCreated()
    {
        var result = await _controller.AttachEvidence(M.Open, 101, Upload());

        var created = Assert.IsType<CreatedResult>(result.Result);
        var evidence = Assert.IsType<AssessmentAnswerEvidence>(created.Value);
        Assert.Equal(101, evidence.QuestionId);
        Assert.Equal("Files/u-1", created.Location);

        // The authenticated user is handed down — the uploader is never taken from the body.
        await _service.Received(1).AttachEvidenceAsync(M.Open, 101, Arg.Any<AssessmentEvidenceUploadRequest>(),
            Arg.Is<User>(u => u.Value == 1));
    }

    [Fact]
    public async Task TestAttachEvidenceWithoutABodyIsBadRequest()
    {
        var result = await _controller.AttachEvidence(M.Open, 101, null);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result.Result));
        await _service.DidNotReceiveWithAnyArgs().AttachEvidenceAsync(default, default, default!, default!);
    }

    [Theory]
    [InlineData(M.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(M.Missing, StatusCodes.Status404NotFound)]
    [InlineData(M.Submitted, StatusCodes.Status409Conflict)]
    [InlineData(M.Broken, StatusCodes.Status500InternalServerError)]
    public async Task TestAttachEvidenceMapsFailures(int runId, int status)
    {
        var result = await _controller.AttachEvidence(runId, 101, Upload());

        Assert.Equal(status, StatusOf(result.Result));
    }

    // --- delete ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestDeleteEvidenceReturnsOk()
    {
        var result = await _controller.DeleteEvidence(M.Open, 100, "u-1");

        Assert.IsType<OkResult>(result);
        await _service.Received(1).DeleteEvidenceAsync(M.Open, 100, "u-1", Arg.Is<User>(u => u.Value == 1));
    }

    [Theory]
    [InlineData(M.NotUploader, StatusCodes.Status403Forbidden)]
    [InlineData(M.Missing, StatusCodes.Status404NotFound)]
    [InlineData(M.Submitted, StatusCodes.Status409Conflict)]
    [InlineData(M.Broken, StatusCodes.Status500InternalServerError)]
    public async Task TestDeleteEvidenceMapsFailures(int runId, int status)
    {
        var result = await _controller.DeleteEvidence(runId, 100, "u-1");

        Assert.Equal(status, StatusOf(result));
    }
}
