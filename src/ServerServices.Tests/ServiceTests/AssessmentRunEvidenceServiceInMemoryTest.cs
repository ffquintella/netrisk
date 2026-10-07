using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// GitHub #80 (T297, S44) — a comment and evidence files on each answer of an assessment run, against
/// the in-memory database with the real entity-scope filters.
///
/// The files service is a substitute that stores the row the way <c>FilesService</c> would, so the
/// listing and deletion below read real rows while no test writes a chunk to disk; the files service's
/// own size cap and entity stamping are covered by <see cref="AssessmentEvidenceFilesTest"/>.
/// </summary>
[TestSubject(typeof(AssessmentRunEvidenceService))]
public class AssessmentRunEvidenceServiceInMemoryTest : InMemoryServiceTestBase
{
    private const int Assessment = 1;
    private const int OtherAssessment = 2;
    private const int Run = 10;
    private const int SubmittedRun = 11;
    private const int Alfa = 100;
    private const int Bravo = 101;
    private const int ForeignQuestion = 200;

    private readonly IFilesService _files = Substitute.For<IFilesService>();
    private readonly AssessmentRunEvidenceService _svc;

    private static readonly User Uploader = NewUser(1, "alice");
    private static readonly User Stranger = NewUser(2, "mallory");
    private static readonly User Admin = NewUser(3, "root", admin: true);

    public AssessmentRunEvidenceServiceInMemoryTest()
    {
        _files.GetFileTypes().Returns(new List<FileType>
        {
            new() { Value = 3, Name = "image/png" },
            new() { Value = 19, Name = "application/pdf" }
        });

        // Stands in for FilesService.CompleteChunkedUpload: persists the metadata it was handed, as the
        // real one does after reassembling the chunks, and reports the size it "read".
        _files.CompleteChunkedUpload(Arg.Any<NrFile>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<User>(),
                Arg.Any<long>())
            .Returns(call =>
            {
                var file = call.Arg<NrFile>();
                var user = call.Arg<User>();
                file.Size = 3;
                file.Content = [1, 2, 3];
                file.User = user.Value;
                file.Timestamp = DateTime.UtcNow;
                file.UniqueName = Guid.NewGuid().ToString("N");
                Seed(ctx => ctx.NrFiles.Add(file));
                return new FileListing
                {
                    Name = file.Name, UniqueName = file.UniqueName, Type = "image/png",
                    Timestamp = file.Timestamp, OwnerId = user.Value
                };
            });

        _svc = new AssessmentRunEvidenceService(Serilog.Log.Logger, GetService<IDalService>(), _files);

        Seed(ctx =>
        {
            ctx.Assessments.Add(new Assessment { Id = Assessment, Name = "FONETIC", Created = new DateTime(2026, 1, 1), EntityId = 1 });
            ctx.Assessments.Add(new Assessment { Id = OtherAssessment, Name = "Other", Created = new DateTime(2026, 1, 1), EntityId = 1 });
            ctx.AssessmentQuestions.Add(new AssessmentQuestion { Id = Alfa, AssessmentId = Assessment, Question = "ALFA", Order = 1 });
            ctx.AssessmentQuestions.Add(new AssessmentQuestion { Id = Bravo, AssessmentId = Assessment, Question = "BRAVO", Order = 2 });
            ctx.AssessmentQuestions.Add(new AssessmentQuestion { Id = ForeignQuestion, AssessmentId = OtherAssessment, Question = "Elsewhere", Order = 1 });
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = Run, AssessmentId = Assessment, EntityId = 1, Status = (int)AssessmentStatus.Open });
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = SubmittedRun, AssessmentId = Assessment, EntityId = 1, Status = (int)AssessmentStatus.Submitted });
        });
    }

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private static AssessmentEvidenceUploadRequest Upload(string name = "photo.png", string type = "3",
        string fileId = "6f1c2c4e-0d7b-4a53-9a1e-2b9d8c7e5f10", int chunks = 1) => new()
    {
        Name = name, Type = type, FileId = fileId, TotalChunks = chunks
    };

    private List<AssessmentRunAnswer> Answers()
    {
        using var db = OpenContext();
        return db.AssessmentRunAnswers.AsNoTracking().OrderBy(a => a.Id).ToList();
    }

    // --- comment -----------------------------------------------------------------------------------

    /// <summary>The issue in one test: ALFA and BRAVO of the same run each keep their own comment.</summary>
    [Fact]
    public async Task TestEachQuestionKeepsItsOwnComment()
    {
        await _svc.SaveCommentAsync(Run, Alfa, "Badge log checked");
        await _svc.SaveCommentAsync(Run, Bravo, "  Firewall rule reviewed  ");

        var answers = Answers();
        Assert.Equal(2, answers.Count);
        Assert.Equal("Badge log checked", answers.Single(a => a.AssessmentQuestionId == Alfa).Comment);
        Assert.Equal("Firewall rule reviewed", answers.Single(a => a.AssessmentQuestionId == Bravo).Comment);
    }

    /// <summary>A comment before an answer creates the row unanswered (S44 D3).</summary>
    [Fact]
    public async Task TestACommentBeforeAnAnswerLeavesTheQuestionUnanswered()
    {
        var saved = await _svc.SaveCommentAsync(Run, Alfa, "Will answer after the interview");

        Assert.Null(saved.AnswerContentJson);
        Assert.Equal(Run, saved.AssessmentRunId);
        Assert.Null(saved.AssessmentRun);
        Assert.Null(Assert.Single(Answers()).AnswerContentJson);
    }

    [Fact]
    public async Task TestACommentDoesNotTouchTheAnswer()
    {
        Seed(ctx => ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
        {
            Id = 1, AssessmentRunId = Run, AssessmentQuestionId = Alfa, AnswerContentJson = "\"Yes\"",
            LastUpdatedAt = DateTime.UtcNow
        }));

        await _svc.SaveCommentAsync(Run, Alfa, "Confirmed on site");

        var answer = Assert.Single(Answers());
        Assert.Equal("\"Yes\"", answer.AnswerContentJson);
        Assert.Equal("Confirmed on site", answer.Comment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TestABlankCommentClearsIt(string? blank)
    {
        await _svc.SaveCommentAsync(Run, Alfa, "First thought");

        await _svc.SaveCommentAsync(Run, Alfa, blank);

        Assert.Null(Assert.Single(Answers()).Comment);
    }

    [Fact]
    public async Task TestACommentOverTheLimitIsRefused()
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SaveCommentAsync(Run, Alfa, new string('x', AssessmentEvidencePolicy.MaxCommentLength + 1)));

        Assert.Equal("comment", thrown.ParameterName);
        Assert.Empty(Answers());
    }

    [Fact]
    public async Task TestACommentAtTheLimitIsAccepted()
    {
        var text = new string('x', AssessmentEvidencePolicy.MaxCommentLength);

        var saved = await _svc.SaveCommentAsync(Run, Alfa, text);

        Assert.Equal(text, saved.Comment);
    }

    [Fact]
    public async Task TestACommentOnAnUnknownRunIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveCommentAsync(999, Alfa, "x"));
    }

    /// <summary>A question id from another assessment must not create an answer row on this run.</summary>
    [Fact]
    public async Task TestACommentOnAQuestionOfAnotherAssessmentIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveCommentAsync(Run, ForeignQuestion, "x"));

        Assert.Empty(Answers());
    }

    [Fact]
    public async Task TestACommentOnASubmittedRunIsRefused()
    {
        var thrown = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _svc.SaveCommentAsync(SubmittedRun, Alfa, "too late"));

        Assert.Equal(AssessmentRunEvidenceService.RunSubmittedRule, thrown.RuleName);
    }

    /// <summary>The run is found through the caller's entity scope, so another entity's run does not exist.</summary>
    [Fact]
    public async Task TestARunOfAnotherEntityIsNotFound()
    {
        ScopeTo(2);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.SaveCommentAsync(Run, Alfa, "x"));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetRunEvidenceAsync(Run));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader));
    }

    // --- attach ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAttachingEvidenceStoresItOnTheAnswer()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload("C:\\Users\\alice\\badge.png"), Uploader);

        var answer = Assert.Single(Answers());
        Assert.Equal(Alfa, answer.AssessmentQuestionId);
        Assert.Null(answer.AnswerContentJson);

        Assert.Equal(Alfa, created.QuestionId);
        Assert.Equal(answer.Id, created.AssessmentRunAnswerId);
        Assert.Equal("badge.png", created.Name);
        Assert.Equal(3, created.Size);

        // What the files service was asked to store: the answer FK, the evidence view type, the
        // normalised name, the declared type — and the evidence cap.
        _files.Received(1).CompleteChunkedUpload(
            Arg.Is<NrFile>(f => f.AssessmentRunAnswerId == answer.Id
                                && f.ViewType == (int)FileCollectionType.AssessmentRunAnswerFile
                                && f.Name == "badge.png" && f.Type == "3"),
            "6f1c2c4e-0d7b-4a53-9a1e-2b9d8c7e5f10", 1, Uploader, AssessmentEvidencePolicy.MaxEvidenceBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    public async Task TestAnEmptyFileNameIsRefused(string name)
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload(name: name), Uploader));

        Assert.Equal("name", thrown.ParameterName);
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task TestAnUploadIdThatIsNotOneSafeSegmentIsRefused(string fileId)
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload(fileId: fileId), Uploader));

        Assert.Equal("fileId", thrown.ParameterName);
        _files.DidNotReceiveWithAnyArgs().CompleteChunkedUpload(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task TestAnUploadWithNoChunksIsRefused()
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload(chunks: 0), Uploader));

        Assert.Equal("totalChunks", thrown.ParameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("99")]
    [InlineData("image/png")]
    public async Task TestATypeTheServerDoesNotAllowIsRefused(string type)
    {
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload(type: type), Uploader));

        Assert.Equal("type", thrown.ParameterName);
        Assert.Empty(Answers());
    }

    [Fact]
    public async Task TestEvidenceOnASubmittedRunIsRefused()
    {
        var thrown = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _svc.AttachEvidenceAsync(SubmittedRun, Alfa, Upload(), Uploader));

        Assert.Equal(AssessmentRunEvidenceService.RunSubmittedRule, thrown.RuleName);
        _files.DidNotReceiveWithAnyArgs().CompleteChunkedUpload(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task TestEvidenceOnAQuestionOfAnotherAssessmentIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.AttachEvidenceAsync(Run, ForeignQuestion, Upload(), Uploader));
    }

    [Fact]
    public async Task TestTheEleventhFileOnOneAnswerIsRefused()
    {
        for (var i = 0; i < AssessmentEvidencePolicy.MaxEvidencePerAnswer; i++)
            await _svc.AttachEvidenceAsync(Run, Alfa, Upload($"photo-{i}.png"), Uploader);

        var thrown = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _svc.AttachEvidenceAsync(Run, Alfa, Upload("one-too-many.png"), Uploader));

        Assert.Equal(AssessmentRunEvidenceService.EvidenceLimitRule, thrown.RuleName);

        // The limit is per answer: BRAVO still takes evidence.
        await _svc.AttachEvidenceAsync(Run, Bravo, Upload(), Uploader);
    }

    // --- list --------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheListingCarriesEachFilesQuestionAndOnlyThisRunsFiles()
    {
        Seed(ctx => ctx.AssessmentRuns.Add(new AssessmentRun
        {
            Id = 12, AssessmentId = Assessment, EntityId = 1, Status = (int)AssessmentStatus.Open
        }));
        Seed(ctx => ctx.FileTypes.Add(new FileType { Value = 3, Name = "image/png" }));

        await _svc.AttachEvidenceAsync(Run, Alfa, Upload("alfa.png"), Uploader);
        await _svc.AttachEvidenceAsync(Run, Bravo, Upload("bravo.png"), Uploader);
        await _svc.AttachEvidenceAsync(12, Alfa, Upload("other-run.png"), Uploader);

        var listed = await _svc.GetRunEvidenceAsync(Run);

        Assert.Equal(2, listed.Count);
        Assert.Equal(Alfa, listed.Single(e => e.Name == "alfa.png").QuestionId);
        Assert.Equal(Bravo, listed.Single(e => e.Name == "bravo.png").QuestionId);
        Assert.All(listed, e => Assert.Equal("image/png", e.Type));
        Assert.All(listed, e => Assert.Equal(Uploader.Value, e.OwnerId));
    }

    [Fact]
    public async Task TestTheListingOfAnUnknownRunIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetRunEvidenceAsync(999));
    }

    /// <summary>A submitted run is read-only, not hidden: its evidence still lists.</summary>
    [Fact]
    public async Task TestTheEvidenceOfASubmittedRunStillLists()
    {
        Assert.Empty(await _svc.GetRunEvidenceAsync(SubmittedRun));
    }

    // --- delete ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheUploaderCanDeleteTheirEvidence()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader);

        await _svc.DeleteEvidenceAsync(Run, Alfa, created.UniqueName, Uploader);

        Assert.Empty(await _svc.GetRunEvidenceAsync(Run));
    }

    [Fact]
    public async Task TestAnAdministratorCanDeleteAnyonesEvidence()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader);

        await _svc.DeleteEvidenceAsync(Run, Alfa, created.UniqueName, Admin);

        Assert.Empty(await _svc.GetRunEvidenceAsync(Run));
    }

    [Fact]
    public async Task TestSomebodyElseCannotDeleteTheEvidence()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader);

        await Assert.ThrowsAsync<PermissionInvalidException>(() =>
            _svc.DeleteEvidenceAsync(Run, Alfa, created.UniqueName, Stranger));

        Assert.Single(await _svc.GetRunEvidenceAsync(Run));
    }

    /// <summary>The route names the question too, so it cannot reach another answer's file.</summary>
    [Fact]
    public async Task TestEvidenceOfAnotherQuestionIsNotFound()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.DeleteEvidenceAsync(Run, Bravo, created.UniqueName, Uploader));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.DeleteEvidenceAsync(Run, Alfa, "no-such-file", Uploader));

        Assert.Single(await _svc.GetRunEvidenceAsync(Run));
    }

    /// <summary>A file that is not evidence at all — a risk attachment — is out of this route's reach.</summary>
    [Fact]
    public async Task TestAFileThatIsNotEvidenceIsNotFound()
    {
        Seed(ctx => ctx.NrFiles.Add(new NrFile
        {
            Id = 500, Name = "risk.pdf", UniqueName = "risk-file", Type = "19", Size = 1, User = Uploader.Value,
            Content = [1], Timestamp = DateTime.UtcNow, RiskId = 7
        }));

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.DeleteEvidenceAsync(Run, Alfa, "risk-file", Admin));
    }

    [Fact]
    public async Task TestEvidenceOfASubmittedRunCannotBeDeleted()
    {
        var created = await _svc.AttachEvidenceAsync(Run, Alfa, Upload(), Uploader);
        Seed(ctx =>
        {
            var run = ctx.AssessmentRuns.Single(r => r.Id == Run);
            run.Status = (int)AssessmentStatus.Submitted;
        });

        var thrown = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _svc.DeleteEvidenceAsync(Run, Alfa, created.UniqueName, Uploader));

        Assert.Equal(AssessmentRunEvidenceService.RunSubmittedRule, thrown.RuleName);
        Assert.Single(await _svc.GetRunEvidenceAsync(Run));
    }
}
