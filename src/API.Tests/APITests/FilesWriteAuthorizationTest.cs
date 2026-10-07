using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.File;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The <c>/Files</c> write routes end to end — the real controller over the real
/// <see cref="FilesService"/> and <see cref="FileAccessAuthorizer"/>, on an in-memory database — for
/// security findings NR-2026-034 and NR-2026-035.
///
/// End to end on purpose. NR-2026-034 was a split decision: the controller authorized against the owner
/// <em>the request body named</em> and the service then wrote the whole body over the stored row. Each
/// half looked reasonable alone, so a test that mocks either half cannot see the defect. Every refusal
/// below also asserts the database is unchanged, because a 401 written after the row was saved is not a
/// refusal.
///
/// The logged-in user is <c>testUser</c>, id 1, a non-administrator unless a test says otherwise. The
/// rules themselves, case by case, are in
/// <c>ServerServices.Tests.Track8.FileWriteAuthorizationInMemoryTest</c>.
/// </summary>
[TestSubject(typeof(FilesController))]
public class FilesWriteAuthorizationTest : BaseControllerTest
{
    private const int EntityA = 3;
    private const int EntityB = 4;

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IPermissionsService _permissions = Substitute.For<IPermissionsService>();
    private readonly FilesService _files;

    public FilesWriteAuthorizationTest()
    {
        _files = new FilesService(Serilog.Log.Logger, _dal);
        Grant();

        using var db = _dal.GetContext();
        db.FileTypes.Add(new FileType { Value = 1, Name = "text/plain" });
        db.Risks.Add(NewRisk(5, EntityA));
        db.Risks.Add(NewRisk(6, EntityB));
        db.Incidents.Add(new Incident { Id = 8, Description = "outage", CreatedById = 1, EntityId = EntityA });
        db.SaveChanges();
    }

    private static Risk NewRisk(int id, int entityId) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EntityId = entityId
    };

    /// <summary>The permissions the logged-in user holds for the rest of the test.</summary>
    private void Grant(params string[] permissions) =>
        _permissions.GetUserPermissionsAsync(Arg.Any<User>()).Returns(_ => Task.FromResult(permissions.ToList()));

    private FilesController Controller(bool admin = false)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        environment.WebRootPath.Returns(Path.GetTempPath());

        return ResolveController<FilesController>(s =>
        {
            s.AddSingleton<IDalService>(_dal);
            s.AddSingleton<IFilesService>(_files);
            s.AddSingleton<IFileAccessAuthorizer>(new FileAccessAuthorizer(Serilog.Log.Logger, _dal, _permissions));
            s.AddSingleton(admin ? MockedUsersService.Create() : MockedNonAdminUsersService.Build());
            s.AddSingleton(environment);
        });
    }

    /// <summary>Every row, whatever the current entity scope — what is actually in the table.</summary>
    private List<NrFile> StoredFiles()
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.NrFiles.ToList();
    }

    // --- NR-2026-034: PUT /Files/{name} ----------------------------------------------------------------

    /// <summary>A risk attachment in entity A with known content.</summary>
    private void SeedStoredFile(int owner, int? answerId = null)
    {
        using var db = _dal.GetContext();
        db.NrFiles.Add(new NrFile
        {
            Id = 20, Name = "original.txt", UniqueName = "u-20", Type = "1", Size = 3, User = owner,
            Content = [1, 2, 3], Timestamp = new DateTime(2026, 1, 1), EntityId = EntityA,
            RiskId = answerId is null ? 5 : null, AssessmentRunAnswerId = answerId
        });
        db.SaveChanges();
    }

    /// <summary>Renames — and also tries to rewrite the content, owner, parent and entity.</summary>
    private static NrFile TamperedBody(int claimedOwner, string uniqueName = "u-20") => new()
    {
        Id = 20, UniqueName = uniqueName, Name = "renamed.txt", Type = "1", Size = 2, User = claimedOwner,
        Content = [9, 9], Timestamp = new DateTime(2030, 1, 1), RiskId = 6, IncidentId = 8, EntityId = EntityB
    };

    private void AssertUnchangedExceptName(string expectedName, int owner)
    {
        var stored = Assert.Single(StoredFiles());
        Assert.Equal(expectedName, stored.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.Content);
        Assert.Equal(3, stored.Size);
        Assert.Equal(owner, stored.User);
        Assert.Equal(5, stored.RiskId);
        Assert.Null(stored.IncidentId);
        Assert.Equal(EntityA, stored.EntityId);
    }

    /// <summary>
    /// The finding. User 1 does not own file 20 and says, in the body, that they do. Before the fix that
    /// claim was the whole authorization, and the body then replaced the content, the owner, the parent
    /// and the entity.
    /// </summary>
    [Fact]
    public void TestANonOwnerClaimingOwnershipInTheBodyIsRefusedAndNothingChanges()
    {
        SeedStoredFile(owner: 2);

        var result = Controller().SaveFile("u-20", TamperedBody(claimedOwner: 1));

        Assert.IsType<UnauthorizedResult>(result.Result);
        AssertUnchangedExceptName("original.txt", owner: 2);
    }

    [Fact]
    public void TestTheOwnerRenamesAndTheRestOfTheBodyIsIgnored()
    {
        SeedStoredFile(owner: 1);

        var result = Controller().SaveFile("u-20", TamperedBody(claimedOwner: 1));

        Assert.IsType<OkResult>(result.Result);
        AssertUnchangedExceptName("renamed.txt", owner: 1);
    }

    [Fact]
    public void TestAnAdministratorCanRenameAnotherUsersFile()
    {
        SeedStoredFile(owner: 2);

        var result = Controller(admin: true).SaveFile("u-20", TamperedBody(claimedOwner: 2));

        Assert.IsType<OkResult>(result.Result);
        AssertUnchangedExceptName("renamed.txt", owner: 2);
    }

    /// <summary>
    /// GitHub #80's guard survives: the owner of an evidence file cannot detach or rename it through the
    /// generic route — the service refuses before anything is written.
    /// </summary>
    [Fact]
    public void TestAssessmentEvidenceIsStillRefusedByTheGenericSave()
    {
        SeedStoredFile(owner: 1, answerId: 50);

        var body = TamperedBody(claimedOwner: 1);
        body.RiskId = null;
        body.IncidentId = null;

        var result = Controller().SaveFile("u-20", body);

        Assert.IsType<BadRequestResult>(result.Result);
        var stored = Assert.Single(StoredFiles());
        Assert.Equal("original.txt", stored.Name);
        Assert.Equal(50, stored.AssessmentRunAnswerId);
    }

    /// <summary>
    /// A wrong unique name for a real id answers exactly as a refusal does. It used to be a 400 naming the
    /// mismatch, which told the caller the id existed.
    /// </summary>
    [Fact]
    public void TestAWrongUniqueNameAnswersLikeARefusal()
    {
        SeedStoredFile(owner: 1);

        var result = Controller().SaveFile("u-guessed", TamperedBody(claimedOwner: 1, uniqueName: "u-guessed"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        AssertUnchangedExceptName("original.txt", owner: 1);
    }

    [Fact]
    public void TestTheRouteAndTheBodyMustNameTheSameFile()
    {
        SeedStoredFile(owner: 1);

        var result = Controller().SaveFile("u-other", TamperedBody(claimedOwner: 1));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        AssertUnchangedExceptName("original.txt", owner: 1);
    }

    // --- NR-2026-035: POST /Files ----------------------------------------------------------------------

    private static NrFile NewUpload(Action<NrFile> configure)
    {
        var file = new NrFile { Name = "report.txt", Type = "1", Content = [1, 2, 3], UniqueName = string.Empty };
        configure(file);
        return file;
    }

    /// <summary>The finding: no permission on the risk, and the file used to be attached to it anyway.</summary>
    [Fact]
    public async Task TestCreatingOnARiskWithoutWriteAccessIsRefusedAndNothingIsStored()
    {
        var result = await Controller().CreateFile(NewUpload(f => f.RiskId = 5));

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task TestCreateIgnoresTheBodysEntityAndOwnerAndDerivesThemFromTheParentAndTheCaller()
    {
        Grant(FileAccessAuthorizer.RiskPermission);

        var result = await Controller().CreateFile(NewUpload(f =>
        {
            f.RiskId = 5;
            f.EntityId = EntityB;
            f.User = 2;
        }));

        Assert.IsType<CreatedResult>(result.Result);
        var stored = Assert.Single(StoredFiles());
        Assert.Equal(EntityA, stored.EntityId);
        Assert.Equal(1, stored.User);
        Assert.Equal(5, stored.RiskId);
    }

    /// <summary>
    /// The cross-tenant plant: a caller scoped to entity A, holding the risk permission, attaching to
    /// entity B's risk. Before the fix the file was stored — unscoped, so visible to everyone — and listed
    /// under the other entity's risk.
    /// </summary>
    [Fact]
    public async Task TestCreatingOnAnotherEntitysRiskIsRefusedEvenWithThePermission()
    {
        Grant(FileAccessAuthorizer.RiskPermission);
        _dal.Scope = EntityScope.ForEntities([EntityA]);

        var result = await Controller().CreateFile(NewUpload(f => f.RiskId = 6));

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task TestCreatingAFileWithTwoParentsIsABadRequest()
    {
        Grant(FileAccessAuthorizer.RiskPermission, FileAccessAuthorizer.IncidentPermission);

        var result = await Controller().CreateFile(NewUpload(f =>
        {
            f.RiskId = 5;
            f.IncidentId = 8;
        }));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("invalid_parameter", JsonSerializer.Serialize(bad.Value));
        Assert.Empty(StoredFiles());
    }

    /// <summary>An unknown type used to be stored and then answered 500; it is a 400 and stores nothing.</summary>
    [Fact]
    public async Task TestCreatingAFileOfAnUnknownTypeIsABadRequestAndNothingIsStored()
    {
        var result = await Controller().CreateFile(NewUpload(f => f.Type = "999"));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("invalid_parameter", JsonSerializer.Serialize(bad.Value));
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task TestCompletingAnUploadOfAnUnknownTypeIsABadRequestAndNothingIsStored()
    {
        var fileId = StageChunks([1, 2], [3, 4]);
        try
        {
            var result = await Controller().CompleteLocalFile(NewUpload(f => f.Type = "999"), fileId, 2);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Empty(StoredFiles());
        }
        finally
        {
            RemoveStaged(fileId);
        }
    }

    /// <summary>Must not break: a parentless upload is readable only by its uploader, so it needs nothing.</summary>
    [Fact]
    public async Task TestAParentlessUploadIsStillCreated()
    {
        var result = await Controller().CreateFile(NewUpload(_ => { }));

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(1, Assert.Single(StoredFiles()).User);
    }

    // --- NR-2026-035: POST /Files/local/complete -------------------------------------------------------

    private string StageChunks(params byte[][] chunks)
    {
        var fileId = Guid.NewGuid().ToString();
        for (var i = 0; i < chunks.Length; i++)
            _files.SaveChunk(new FileChunk
            {
                FileId = fileId, ChunkNumber = i + 1, TotalChunks = chunks.Length,
                ChunkData = Convert.ToBase64String(chunks[i])
            });
        return fileId;
    }

    private void RemoveStaged(string fileId)
    {
        var directory = Path.Combine(_files.GetUploadDirectory(), fileId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// The chunks are really staged, so before the fix this upload completed and was attached to the
    /// incident. Now it is refused before the chunks are reassembled.
    /// </summary>
    [Fact]
    public async Task TestCompletingAnUploadOnAnIncidentWithoutWriteAccessIsRefusedAndNothingIsStored()
    {
        var fileId = StageChunks([1, 2], [3, 4]);
        try
        {
            var result = await Controller().CompleteLocalFile(NewUpload(f => f.IncidentId = 8), fileId, 2);

            Assert.IsType<UnauthorizedResult>(result.Result);
            Assert.Empty(StoredFiles());
        }
        finally
        {
            RemoveStaged(fileId);
        }
    }

    [Fact]
    public async Task TestCompletingAnUploadWithWriteAccessStoresItUnderTheParentsEntity()
    {
        Grant(FileAccessAuthorizer.IncidentPermission);
        var fileId = StageChunks([1, 2], [3, 4]);
        try
        {
            var result = await Controller().CompleteLocalFile(NewUpload(f =>
            {
                f.IncidentId = 8;
                f.EntityId = EntityB;
                f.Size = 1;
            }), fileId, 2);

            Assert.IsType<CreatedResult>(result.Result);
            var stored = Assert.Single(StoredFiles());
            Assert.Equal(EntityA, stored.EntityId);
            Assert.Equal(8, stored.IncidentId);
            Assert.Equal(4, stored.Size);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, stored.Content);
        }
        finally
        {
            RemoveStaged(fileId);
        }
    }
}
