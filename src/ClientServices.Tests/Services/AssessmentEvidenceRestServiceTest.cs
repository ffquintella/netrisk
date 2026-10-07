using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Assessments;
using Model.Exceptions;
using RestSharp;
using Xunit;
using File = System.IO.File;

namespace ClientServices.Tests.Services;

/// <summary>
/// GitHub #80 (T297, S44) — <see cref="AssessmentEvidenceRestService"/> over the stub HTTP backend: the
/// routes and bodies it sends, the chunked staging it reuses, the size check it makes before sending a
/// byte, and how each refusal from the server surfaces.
/// </summary>
[TestSubject(typeof(AssessmentEvidenceRestService))]
public class AssessmentEvidenceRestServiceTest : BaseServiceTest, IDisposable
{
    private readonly StubRestBackend _backend = new();
    private readonly IAssessmentEvidenceService _service;
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("netrisk-evidence-test");

    public AssessmentEvidenceRestServiceTest()
    {
        _service = ResolveWith<IAssessmentEvidenceService>(_backend);
    }

    public void Dispose()
    {
        try
        {
            _tempDirectory.Delete(true);
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    private Uri WriteTempFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempDirectory.FullName, fileName);
        File.WriteAllBytes(path, content);
        return new Uri(path);
    }

    private static AssessmentAnswerEvidence Evidence(int questionId) => new()
    {
        Name = "badge.png", UniqueName = "u-1", Type = "image/png", OwnerId = 1, Size = 3,
        QuestionId = questionId, AssessmentRunAnswerId = 50, Timestamp = new DateTime(2026, 10, 7)
    };

    private void StubStaging()
    {
        _backend.OnGet("/Files/Types", new List<FileType>
        {
            new() { Value = 3, Name = "image/png" },
            new() { Value = 18, Name = "application/force-download" }
        });
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");
        _backend.OnPost("/Files/local/chunk", "\"ok\"");
    }

    // --- comment -----------------------------------------------------------------------------------

    [Fact]
    public async Task TestSaveCommentPutsTheTextOnTheQuestion()
    {
        _backend.OnPut("/Assessments/runs/10/questions/100/comment", new AssessmentRunAnswer
        {
            Id = 50, AssessmentRunId = 10, AssessmentQuestionId = 100, Comment = "Badge log checked"
        });

        var saved = await _service.SaveCommentAsync(10, 100, "Badge log checked");

        Assert.Equal("Badge log checked", saved.Comment);
        Assert.Equal("PUT /Assessments/runs/10/questions/100/comment", _backend.LastRequest.ToString());
        Assert.Contains("Badge log checked", _backend.LastRequest.Body);
    }

    /// <summary>A 409 reaches the caller with the rule name the viewer reads its message from.</summary>
    [Fact]
    public async Task TestACommentOnASubmittedRunSurfacesTheRule()
    {
        _backend.OnPut("/Assessments/runs/11/questions/100/comment",
            new { error = "run_submitted", message = "The run has been submitted" }, HttpStatusCode.Conflict);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.SaveCommentAsync(11, 100, "too late"));

        Assert.Contains("run_submitted", thrown.Message);
    }

    [Fact]
    public async Task TestAnUnknownRunIsNotFound()
    {
        _backend.OnStatus(Method.Get, "/Assessments/runs/999/evidence", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.GetRunEvidenceAsync(999));
    }

    [Fact]
    public async Task TestAnUnreachableServerIsACommunicationFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Assessments/runs/10/evidence");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRunEvidenceAsync(10));
    }

    [Fact]
    public async Task TestAServerErrorIsAFailureNotAnEmptyList()
    {
        _backend.OnStatus(Method.Get, "/Assessments/runs/10/evidence", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRunEvidenceAsync(10));
    }

    // --- list --------------------------------------------------------------------------------------

    [Fact]
    public async Task TestGetRunEvidenceReadsEachFilesQuestion()
    {
        _backend.OnGet("/Assessments/runs/10/evidence", new List<AssessmentAnswerEvidence> { Evidence(100), Evidence(101) });

        var listed = await _service.GetRunEvidenceAsync(10);

        Assert.Equal([100, 101], listed.ConvertAll(e => e.QuestionId));
        Assert.Equal("image/png", listed[0].Type);
    }

    // --- upload ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestUploadStagesTheChunksAndCompletesOnTheQuestion()
    {
        StubStaging();
        _backend.OnPost("/Assessments/runs/10/questions/101/evidence", Evidence(101), HttpStatusCode.Created);
        var source = WriteTempFile("badge.png", "png"u8.ToArray());

        var created = await _service.UploadEvidenceAsync(10, 101, source);

        Assert.Equal(101, created.QuestionId);
        Assert.Equal(4, _backend.Requests.Count);
        Assert.Equal("GET /Files/Types", _backend.Requests[0].ToString());
        Assert.Equal("GET /Files/local/id", _backend.Requests[1].ToString());
        Assert.Equal("POST /Files/local/chunk", _backend.Requests[2].ToString());
        Assert.Equal("POST /Assessments/runs/10/questions/101/evidence", _backend.Requests[3].ToString());

        // The completion carries the staging id, the chunk count, the resolved type value and the name —
        // and no content, which went as chunks.
        var body = _backend.Requests[3].Body;
        Assert.Contains("\"fileId\":\"local-file-1\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"totalChunks\":1", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"type\":\"3\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"name\":\"badge.png\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("content", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The cap is checked locally, before a byte is sent. A sparse file keeps this test fast.</summary>
    [Fact]
    public async Task TestAFileOverTheCapIsRefusedBeforeAnythingIsSent()
    {
        var path = Path.Combine(_tempDirectory.FullName, "huge.png");
        using (var stream = File.Create(path))
            stream.SetLength(AssessmentEvidencePolicy.MaxEvidenceBytes + 1);

        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.UploadEvidenceAsync(10, 101, new Uri(path)));

        Assert.Equal("file", thrown.ParameterName);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestAnEmptyFileIsRefusedBeforeAnythingIsSent()
    {
        var source = WriteTempFile("empty.png", []);

        await Assert.ThrowsAsync<InvalidParameterException>(() => _service.UploadEvidenceAsync(10, 101, source));

        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestAMissingLocalFileIsAnArgumentError()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UploadEvidenceAsync(10, 101, new Uri(Path.Combine(_tempDirectory.FullName, "gone.png"))));
    }

    [Fact]
    public async Task TestTheEleventhFileSurfacesTheLimitRule()
    {
        StubStaging();
        _backend.OnPost("/Assessments/runs/10/questions/101/evidence",
            new { error = "evidence_limit_reached", message = "At most 10" }, HttpStatusCode.Conflict);
        var source = WriteTempFile("badge.png", "png"u8.ToArray());

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.UploadEvidenceAsync(10, 101, source));

        Assert.Contains("evidence_limit_reached", thrown.Message);
    }

    // --- delete ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestDeleteNamesTheRunQuestionAndFile()
    {
        _backend.OnStatus(Method.Delete, "/Assessments/runs/10/questions/100/evidence/u-1", HttpStatusCode.OK);

        await _service.DeleteEvidenceAsync(10, 100, "u-1");

        Assert.Equal("DELETE /Assessments/runs/10/questions/100/evidence/u-1", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestDeletingSomebodyElsesEvidenceIsRefused()
    {
        _backend.OnDelete("/Assessments/runs/10/questions/100/evidence/u-1",
            new { error = "not_the_uploader" }, HttpStatusCode.Forbidden);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() =>
            _service.DeleteEvidenceAsync(10, 100, "u-1"));

        Assert.Contains("not_the_uploader", thrown.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task TestDeleteRequiresAName(string uniqueName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteEvidenceAsync(10, 100, uniqueName));

        Assert.Empty(_backend.Requests);
    }
}
