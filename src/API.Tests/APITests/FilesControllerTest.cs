using System;
using System.Collections.Generic;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(FilesController))]
public class FilesControllerTest : BaseControllerTest
{
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IWebHostEnvironment _environment = Substitute.For<IWebHostEnvironment>();
    private readonly FilesController _controller;

    public FilesControllerTest()
    {
        var temporaryRoot = System.IO.Directory.CreateTempSubdirectory("netrisk-files-controller-test").FullName;
        _environment.ContentRootPath = temporaryRoot;
        _environment.WebRootPath = temporaryRoot;

        _controller = ResolveController<FilesController>(s =>
        {
            s.AddSingleton(_filesService);
            s.AddSingleton(_environment);
        });
    }

    private static NrFile MakeFile(int id, string uniqueName)
    {
        return new NrFile
        {
            Id = id,
            Name = "evidence.txt",
            UniqueName = uniqueName,
            Type = "text/plain",
            Size = 3,
            // Matches the mocked logged user, so the ownership guards let the call through.
            User = 1,
            Content = new byte[] { 1, 2, 3 }
        };
    }

    private static FileListing MakeListing(string uniqueName)
    {
        return new FileListing
        {
            Name = "evidence.txt",
            UniqueName = uniqueName,
            Type = "text/plain",
            OwnerId = 1
        };
    }

    #region GetAll

    [Fact]
    public void TestGetAll()
    {
        _filesService.GetAll().Returns(new List<FileListing>
        {
            MakeListing("unique-1"),
            MakeListing("unique-2")
        });

        var result = _controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var listings = Assert.IsType<List<FileListing>>(okResult.Value);

        Assert.Equal(2, listings.Count);
    }

    [Fact]
    public void TestGetAllUnauthorizedWhenServiceRejects()
    {
        _filesService.GetAll()
            .Returns<List<FileListing>>(_ => throw new UserNotAuthorizedException("testUser", 1, "list files"));

        var result = _controller.GetAll();

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetAllReturns500OnError()
    {
        _filesService.GetAll().Returns<List<FileListing>>(_ => throw new Exception("boom"));

        var result = _controller.GetAll();

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetFileTypes

    [Fact]
    public void TestGetFileTypes()
    {
        _filesService.GetFileTypes().Returns(new List<FileType>
        {
            new FileType { Value = 1, Name = "Risk" },
            new FileType { Value = 2, Name = "Incident" }
        });

        var result = _controller.GetFileTypes();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var types = Assert.IsType<List<FileType>>(okResult.Value);

        Assert.Equal(2, types.Count);
        Assert.Equal("Risk", types[0].Name);
    }

    [Fact]
    public void TestGetFileTypesReturns500OnError()
    {
        _filesService.GetFileTypes().Returns<List<FileType>>(_ => throw new Exception("boom"));

        var result = _controller.GetFileTypes();

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region CreateFile

    [Fact]
    public void TestCreateFile()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.Create(incoming, Arg.Any<User>()).Returns(MakeListing("unique-1"));

        var result = _controller.CreateFile(incoming);

        var createdResult = Assert.IsType<CreatedResult>(result.Result);
        var listing = Assert.IsType<FileListing>(createdResult.Value);

        Assert.Equal("unique-1", listing.UniqueName);
        Assert.Equal("Files/unique-1", createdResult.Location);
    }

    [Fact]
    public void TestCreateFileUnauthorizedWhenServiceRejects()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.Create(incoming, Arg.Any<User>())
            .Returns<FileListing>(_ => throw new UserNotAuthorizedException("testUser", 1, "create files"));

        var result = _controller.CreateFile(incoming);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestCreateFileReturns500OnError()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.Create(incoming, Arg.Any<User>())
            .Returns<FileListing>(_ => throw new Exception("boom"));

        var result = _controller.CreateFile(incoming);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetUniqueFileId

    [Fact]
    public void TestGetUniqueFileId()
    {
        var result = _controller.GetUniqueFileId();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var id = Assert.IsType<string>(okResult.Value);

        Assert.True(Guid.TryParse(id, out _));
    }

    #endregion

    #region CreateLocalFileChunk

    [Fact]
    public void TestCreateLocalFileChunk()
    {
        var chunk = new FileChunk
        {
            ChunkNumber = 1,
            TotalChunks = 2,
            FileId = "upload-1",
            ChunkData = "AQID"
        };

        var result = _controller.CreateLocalFileChunk(chunk);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Chunk uploaded successfully.", (string)okResult.Value);
        _filesService.Received(1).SaveChunk(chunk);
    }

    [Fact]
    public void TestCreateLocalFileChunkReturns500OnError()
    {
        var chunk = new FileChunk { ChunkNumber = 1, TotalChunks = 2, FileId = "upload-2" };
        _filesService.When(x => x.SaveChunk(chunk)).Do(_ => throw new Exception("boom"));

        var result = _controller.CreateLocalFileChunk(chunk);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region CompleteLocalFile

    [Fact]
    public void TestCompleteLocalFile()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.CompleteChunkedUpload(incoming, "upload-1", 2, Arg.Any<User>())
            .Returns(MakeListing("unique-1"));

        var result = _controller.CompleteLocalFile(incoming, "upload-1", 2);

        var createdResult = Assert.IsType<CreatedResult>(result.Result);
        var listing = Assert.IsType<FileListing>(createdResult.Value);

        Assert.Equal("unique-1", listing.UniqueName);
        Assert.Equal("Files/unique-1", createdResult.Location);
    }

    [Fact]
    public void TestCompleteLocalFileUnauthorizedWhenServiceRejects()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.CompleteChunkedUpload(incoming, "upload-1", 2, Arg.Any<User>())
            .Returns<FileListing>(_ => throw new UserNotAuthorizedException("testUser", 1, "create files"));

        var result = _controller.CompleteLocalFile(incoming, "upload-1", 2);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestCompleteLocalFileMissingChunksReturns400()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.CompleteChunkedUpload(incoming, "upload-1", 2, Arg.Any<User>())
            .Returns<FileListing>(_ => throw new DataNotFoundException("chunks", "upload-1"));

        var result = _controller.CompleteLocalFile(incoming, "upload-1", 2);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }

    [Fact]
    public void TestCompleteLocalFileReturns500OnError()
    {
        var incoming = MakeFile(0, "unique-1");
        _filesService.CompleteChunkedUpload(incoming, "upload-1", 2, Arg.Any<User>())
            .Returns<FileListing>(_ => throw new Exception("boom"));

        var result = _controller.CompleteLocalFile(incoming, "upload-1", 2);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region SaveFile

    [Fact]
    public void TestSaveFile()
    {
        var file = MakeFile(3, "unique-1");

        var result = _controller.SaveFile("unique-1", file);

        Assert.IsType<OkResult>(result.Result);
        _filesService.Received(1).Save(file);
    }

    [Fact]
    public void TestSaveFileUnauthorizedWhenServiceRejects()
    {
        var file = MakeFile(3, "unique-1");
        _filesService.When(x => x.Save(file))
            .Do(_ => throw new UserNotAuthorizedException("testUser", 1, "update files"));

        var result = _controller.SaveFile("unique-1", file);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestSaveFileBadRequestOnInvalidOperation()
    {
        var file = MakeFile(3, "unique-1");
        _filesService.When(x => x.Save(file)).Do(_ => throw new InvalidOperationException("bad state"));

        var result = _controller.SaveFile("unique-1", file);

        Assert.IsType<BadRequestResult>(result.Result);
    }

    [Fact]
    public void TestSaveFileReturns500OnError()
    {
        var file = MakeFile(3, "unique-1");
        _filesService.When(x => x.Save(file)).Do(_ => throw new Exception("boom"));

        var result = _controller.SaveFile("unique-1", file);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region DeleteFile

    [Fact]
    public void TestDeleteFile()
    {
        _filesService.GetByUniqueName("unique-1").Returns(MakeFile(3, "unique-1"));

        var result = _controller.DeleteFile("unique-1");

        Assert.IsType<OkResult>(result);
        _filesService.Received(1).DeleteByUniqueName("unique-1");
    }

    [Fact]
    public void TestDeleteFileUnauthorizedWhenServiceRejects()
    {
        _filesService.GetByUniqueName("unique-1")
            .Returns<NrFile>(_ => throw new UserNotAuthorizedException("testUser", 1, "delete files"));

        var result = _controller.DeleteFile("unique-1");

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public void TestDeleteFileBadRequestOnInvalidOperation()
    {
        _filesService.GetByUniqueName("unique-1").Returns(MakeFile(3, "unique-1"));
        _filesService.When(x => x.DeleteByUniqueName("unique-1"))
            .Do(_ => throw new InvalidOperationException("bad state"));

        var result = _controller.DeleteFile("unique-1");

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public void TestDeleteFileReturns500OnError()
    {
        _filesService.GetByUniqueName("unique-1").Returns<NrFile>(_ => throw new Exception("boom"));

        var result = _controller.DeleteFile("unique-1");

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetByUniqueName

    [Fact]
    public void TestGetByUniqueName()
    {
        _filesService.GetByUniqueName("unique-1").Returns(MakeFile(3, "unique-1"));

        var result = _controller.GetByUniqueName("unique-1");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var file = Assert.IsType<NrFile>(okResult.Value);

        Assert.Equal(3, file.Id);
        Assert.Equal("unique-1", file.UniqueName);
    }

    [Fact]
    public void TestGetByUniqueNameUnauthorizedWhenServiceRejects()
    {
        _filesService.GetByUniqueName("unique-1")
            .Returns<NrFile>(_ => throw new UserNotAuthorizedException("testUser", 1, "see files"));

        var result = _controller.GetByUniqueName("unique-1");

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetByUniqueNameNotFoundIsUnauthorized()
    {
        _filesService.GetByUniqueName("missing")
            .Returns<NrFile>(_ => throw new DataNotFoundException("files", "missing"));

        var result = _controller.GetByUniqueName("missing");

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetByUniqueNameReturns500OnError()
    {
        _filesService.GetByUniqueName("unique-1").Returns<NrFile>(_ => throw new Exception("boom"));

        var result = _controller.GetByUniqueName("unique-1");

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetById

    [Fact]
    public void TestGetById()
    {
        _filesService.GetById(3).Returns(MakeFile(3, "unique-1"));

        var result = _controller.GetById(3);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var file = Assert.IsType<NrFile>(okResult.Value);

        Assert.Equal(3, file.Id);
        // The controller strips the payload before returning the metadata.
        Assert.Empty(file.Content);
    }

    [Fact]
    public void TestGetByIdUnauthorizedWhenServiceRejects()
    {
        _filesService.GetById(3)
            .Returns<NrFile>(_ => throw new UserNotAuthorizedException("testUser", 1, "see files"));

        var result = _controller.GetById(3);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetByIdNotFoundIsUnauthorized()
    {
        _filesService.GetById(999)
            .Returns<NrFile>(_ => throw new DataNotFoundException("files", "999"));

        var result = _controller.GetById(999);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void TestGetByIdReturns500OnError()
    {
        _filesService.GetById(3).Returns<NrFile>(_ => throw new Exception("boom"));

        var result = _controller.GetById(3);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion
}
