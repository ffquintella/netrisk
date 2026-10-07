using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.Exceptions;
using Model.File;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// GitHub #80 (T297, S44) — what the files layer does for assessment evidence: the bounded chunked
/// upload (D5), the entity stamp (D7), the refusal to rewrite evidence through the generic save (D4), the
/// listing by answer, and the read rule on <c>GET /Files/{name}</c> (D4) — plus the pure policy both sides
/// share.
///
/// The bounded-upload tests write real chunks into the files service's staging directory, as
/// <c>FilesServiceUploadPathTest</c> does, and assert that a refusal leaves nothing behind.
/// </summary>
[TestSubject(typeof(FilesService))]
public class AssessmentEvidenceFilesTest : InMemoryServiceTestBase
{
    private static readonly User Uploader = NewUser(1, "alice");

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private FilesService Files() => new(Serilog.Log.Logger, GetService<IDalService>());

    /// <summary>An assessment of entity 7 with one open run and one answer (id 50) to question 100.</summary>
    private void SeedAnswer()
    {
        Seed(ctx =>
        {
            ctx.FileTypes.Add(new FileType { Value = 3, Name = "image/png" });
            ctx.Assessments.Add(new Assessment { Id = 1, Name = "FONETIC", Created = new DateTime(2026, 1, 1), EntityId = 7 });
            ctx.AssessmentQuestions.Add(new AssessmentQuestion { Id = 100, AssessmentId = 1, Question = "ALFA", Order = 1 });
            ctx.AssessmentRuns.Add(new AssessmentRun { Id = 10, AssessmentId = 1, EntityId = 7, Status = (int)AssessmentStatus.Open });
            ctx.AssessmentRunAnswers.Add(new AssessmentRunAnswer
            {
                Id = 50, AssessmentRunId = 10, AssessmentQuestionId = 100, LastUpdatedAt = DateTime.UtcNow
            });
        });
    }

    private static NrFile Evidence(string name = "photo.png") => new()
    {
        Name = name, Type = "3", Content = [], AssessmentRunAnswerId = 50,
        ViewType = (int)FileCollectionType.AssessmentRunAnswerFile
    };

    private static string StageChunks(FilesService files, params byte[][] chunks)
    {
        var fileId = Guid.NewGuid().ToString();
        for (var i = 0; i < chunks.Length; i++)
            files.SaveChunk(new FileChunk
            {
                FileId = fileId, ChunkNumber = i + 1, TotalChunks = chunks.Length,
                ChunkData = Convert.ToBase64String(chunks[i])
            });
        return fileId;
    }

    // --- bounded upload (D5) -----------------------------------------------------------------------

    [Fact]
    public void TestABoundedUploadOverTheCapIsRefusedAndLeavesNothingBehind()
    {
        SeedAnswer();
        var files = Files();
        var fileId = StageChunks(files, [1, 2, 3, 4], [5, 6, 7, 8]);

        var thrown = Assert.Throws<InvalidParameterException>(() =>
            files.CompleteChunkedUpload(Evidence(), fileId, 2, Uploader, maxBytes: 7));

        Assert.Equal("file", thrown.ParameterName);
        Assert.False(Directory.Exists(Path.Combine(files.GetUploadDirectory(), fileId)));
        using var db = OpenContext();
        Assert.Empty(db.NrFiles);
    }

    [Fact]
    public void TestABoundedEmptyUploadIsRefused()
    {
        SeedAnswer();
        var files = Files();
        // One chunk with no bytes in it — not zero chunks.
        var fileId = StageChunks(files, new byte[0]);

        var thrown = Assert.Throws<InvalidParameterException>(() =>
            files.CompleteChunkedUpload(Evidence(), fileId, 1, Uploader, maxBytes: 100));

        Assert.Equal("file", thrown.ParameterName);
        Assert.False(Directory.Exists(Path.Combine(files.GetUploadDirectory(), fileId)));
    }

    /// <summary>
    /// At the cap is accepted, and the stored row is evidence of the answer stamped with the assessment's
    /// entity (D7) — 7, not anything the client could have sent.
    /// </summary>
    [Fact]
    public void TestABoundedUploadAtTheCapIsStoredAsEvidenceOfTheAssessmentsEntity()
    {
        SeedAnswer();
        var files = Files();
        var fileId = StageChunks(files, [1, 2, 3, 4], [5, 6, 7, 8]);

        var listing = files.CompleteChunkedUpload(Evidence(), fileId, 2, Uploader, maxBytes: 8);

        using var db = OpenContext();
        var stored = db.NrFiles.Single(f => f.UniqueName == listing.UniqueName);
        Assert.Equal(8, stored.Size);
        Assert.Equal(50, stored.AssessmentRunAnswerId);
        Assert.Equal(7, stored.EntityId);
        Assert.Equal(Uploader.Value, stored.User);
        Assert.Equal("image/png", listing.Type);
    }

    [Fact]
    public void TestTheUnboundedUploadIsUnchanged()
    {
        SeedAnswer();
        var files = Files();
        var fileId = StageChunks(files, new byte[64]);

        var listing = files.CompleteChunkedUpload(new NrFile { Name = "big.png", Type = "3", Content = [] },
            fileId, 1, Uploader);

        Assert.False(string.IsNullOrEmpty(listing.UniqueName));
    }

    // --- generic save (D4) -------------------------------------------------------------------------

    [Fact]
    public void TestTheGenericSaveRefusesToRewriteEvidence()
    {
        SeedAnswer();
        var files = Files();
        var created = files.Create(Evidence(), Uploader);
        var stored = files.GetByUniqueName(created.UniqueName);

        stored.Name = "renamed.png";
        stored.AssessmentRunAnswerId = null;

        Assert.Throws<InvalidOperationException>(() => files.Save(stored, Uploader));
        Assert.Equal("photo.png", files.GetByUniqueName(created.UniqueName).Name);
    }

    [Fact]
    public void TestTheGenericSaveRefusesToMoveAFileOntoAnAnswer()
    {
        SeedAnswer();
        var files = Files();
        var created = files.Create(new NrFile { Name = "risk.png", Type = "3", Content = [1], RiskId = 4 }, Uploader);
        var stored = files.GetByUniqueName(created.UniqueName);

        stored.AssessmentRunAnswerId = 50;

        Assert.Throws<InvalidOperationException>(() => files.Save(stored, Uploader));
        Assert.Null(files.GetByUniqueName(created.UniqueName).AssessmentRunAnswerId);
    }

    [Fact]
    public async Task TestTheListingByAnswerFindsTheEvidence()
    {
        SeedAnswer();
        var files = Files();
        files.Create(Evidence("one.png"), Uploader);
        files.Create(new NrFile { Name = "unrelated.png", Type = "3", Content = [1], RiskId = 4 }, Uploader);

        var listed = await files.GetObjectFileListingsAsync(50, FileCollectionType.AssessmentRunAnswerFile);

        Assert.Equal("one.png", Assert.Single(listed).Name);
    }

    // --- download authorization (D4) ---------------------------------------------------------------

    private IFileAccessAuthorizer Authorizer => GetService<IFileAccessAuthorizer>();

    /// <summary>Seeds a user holding <c>assessments</c> (id 4) and one holding nothing (id 5).</summary>
    private void SeedReaders()
    {
        Seed(ctx =>
        {
            var permission = new Permission { Id = 1, Key = "assessments", Name = "Assessments", Description = "", Order = 1 };
            ctx.Permissions.Add(permission);
            var assessor = NewUser(4, "assessor");
            assessor.Permissions.Add(permission);
            ctx.Users.Add(assessor);
            ctx.Users.Add(NewUser(5, "stranger"));
        });
    }

    private static NrFile StoredEvidence() => new()
    {
        Id = 900, Name = "photo.png", UniqueName = "u-900", Type = "3", Size = 1, User = Uploader.Value,
        Content = [1], Timestamp = DateTime.UtcNow, AssessmentRunAnswerId = 50, EntityId = 7
    };

    [Fact]
    public async Task TestEvidenceIsReadableWithTheAssessmentsPermission()
    {
        SeedAnswer();
        SeedReaders();

        await Authorizer.EnsureCanReadAsync(StoredEvidence(), NewUser(4, "assessor"));
    }

    [Fact]
    public async Task TestEvidenceIsRefusedWithoutTheAssessmentsPermission()
    {
        SeedAnswer();
        SeedReaders();

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanReadAsync(StoredEvidence(), NewUser(5, "stranger")));
    }

    /// <summary>
    /// The permission is not enough on its own: the answer has to be visible in the caller's entity
    /// scope, or the file is treated as having no parent — readable only by its uploader.
    /// </summary>
    [Fact]
    public async Task TestEvidenceOfAnAnswerOutsideTheCallersScopeIsRefusedEvenWithThePermission()
    {
        SeedAnswer();
        SeedReaders();
        ScopeTo(8);

        await Assert.ThrowsAsync<UserNotAuthorizedException>(() =>
            Authorizer.EnsureCanReadAsync(StoredEvidence(), NewUser(4, "assessor")));
    }

    // --- the shared policy -------------------------------------------------------------------------

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("C:\\Users\\alice\\photo.png", "photo.png")]
    [InlineData("/home/alice/photo.png", "photo.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("  pho\u0000to\n.png  ", "photo.png")]
    [InlineData("..", null)]
    [InlineData("dir/", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TestTheFileNameIsReducedToOneCleanSegment(string? name, string? expected)
    {
        Assert.Equal(expected, AssessmentEvidencePolicy.NormalizeFileName(name));
    }

    [Fact]
    public void TestALongFileNameIsCutKeepingItsExtension()
    {
        var normalized = AssessmentEvidencePolicy.NormalizeFileName(new string('a', 300) + ".pdf");

        Assert.NotNull(normalized);
        Assert.Equal(AssessmentEvidencePolicy.MaxFileNameLength, normalized!.Length);
        Assert.EndsWith(".pdf", normalized);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(AssessmentEvidencePolicy.MaxEvidenceBytes, true)]
    [InlineData(AssessmentEvidencePolicy.MaxEvidenceBytes + 1, false)]
    public void TestTheSizeBounds(long bytes, bool accepted)
    {
        Assert.Equal(accepted, AssessmentEvidencePolicy.IsSizeAccepted(bytes));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  kept  ", "kept")]
    public void TestTheCommentIsTrimmedAndBlankIsNull(string? comment, string? expected)
    {
        Assert.Equal(expected, AssessmentEvidencePolicy.NormalizeComment(comment));
    }
}
