using System;
using System.IO;
using System.Linq;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.File;
using Serilog;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track7;

/// <summary>
/// Track 7 finding NR-2026-006 — arbitrary file write through the chunked-upload endpoints.
///
/// <c>POST /Files/local/chunk</c> takes the file id from the request body. <c>FilesService</c> then
/// did <c>Path.Combine(_baseUploadPath, chunk.FileId)</c>, called <c>Directory.CreateDirectory</c>
/// on the result and wrote the base64-decoded chunk into it. A file id of
/// <c>"../../../../something"</c> therefore let any authenticated user create directories and write
/// files anywhere the API process could reach, and <c>local/complete</c> then reassembled them into
/// a <c>.dat</c> file of the caller's choosing.
///
/// These tests assert the rejection, not the write: they must not depend on the process being able
/// to write outside its own temporary directory.
/// </summary>
[TestSubject(typeof(FilesService))]
public class FilesServiceUploadPathTest
{
    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());

    private FilesService Service() => new(new LoggerConfiguration().CreateLogger(), _dal);

    /// <summary>
    /// <c>FilesService.Create</c> projects the stored type onto its display name, so the file-type
    /// row has to exist or the create fails for reasons unrelated to what is under test.
    /// </summary>
    private void SeedFileType()
    {
        using var context = _dal.GetContext();
        context.FileTypes.Add(new FileType { Value = 1, Name = "Report" });
        context.SaveChanges();
    }

    private static FileChunk Chunk(string fileId, int number = 1) => new()
    {
        FileId = fileId,
        ChunkNumber = number,
        // "netrisk" — content is irrelevant; the test never gets as far as writing it.
        ChunkData = Convert.ToBase64String("netrisk"u8.ToArray())
    };

    [Theory]
    [InlineData("../../../../tmp/netrisk-escape")]
    [InlineData("..")]
    [InlineData("../sibling")]
    [InlineData("/etc/cron.d")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void SaveChunkRefusesAFileIdThatIsNotOneSafeSegment(string fileId)
    {
        var thrown = Assert.Throws<InvalidParameterException>(() => Service().SaveChunk(Chunk(fileId)));

        Assert.Equal("fileId", thrown.ParameterName);
    }

    [Fact]
    public void SaveChunkAcceptsAGuidFileIdAndWritesInsideTheUploadDirectory()
    {
        var service = Service();
        var fileId = Guid.NewGuid().ToString();

        service.SaveChunk(Chunk(fileId));

        var directory = Path.Combine(service.GetUploadDirectory(), fileId);
        Assert.True(Directory.Exists(directory));
        Assert.Equal(new[] { "1.part" },
            Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());

        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void SaveChunkRefusesAChunkNumberOutsideTheAllowedRange(int chunkNumber)
    {
        var thrown = Assert.Throws<InvalidParameterException>(
            () => Service().SaveChunk(Chunk(Guid.NewGuid().ToString(), chunkNumber)));

        Assert.Equal("ChunkNumber", thrown.ParameterName);
    }

    [Theory]
    [InlineData("../../../../tmp/netrisk-escape")]
    [InlineData("/etc")]
    public void TheOtherChunkOperationsRefuseTheSameIds(string fileId)
    {
        var service = Service();

        Assert.Throws<InvalidParameterException>(() => service.CombineChunks(fileId, 1));
        Assert.Throws<InvalidParameterException>(() => service.DeleteChunks(fileId, 1));
        Assert.Throws<InvalidParameterException>(() => service.CountChunks(fileId));
        Assert.Throws<InvalidParameterException>(
            () => service.CompleteChunkedUpload(new NrFile { Name = "x", Type = "1" }, fileId, 1,
                new User { Value = 1, Name = "tester", Email = "t@e.st", Type = "local" }));
    }

    /// <summary>
    /// Finding NR-2026-017: the download route <c>GET /Files/{name}</c> has no per-file ownership
    /// check, so the unique name is the capability. It used to be
    /// <c>SHA1(fileName + 15 predictable characters)</c>; it must now be unguessable, which at
    /// minimum means two files with the same name never share it.
    /// </summary>
    [Fact]
    public void UniqueNamesAreUnpredictableAcrossFilesWithTheSameName()
    {
        SeedFileType();
        var service = Service();
        var user = new User { Value = 1, Name = "tester", Email = "t@e.st", Type = "local" };

        var names = Enumerable.Range(0, 20)
            .Select(_ => service.Create(
                new NrFile { Name = "report.pdf", Type = "1", Content = [1, 2, 3] }, user).UniqueName)
            .ToHashSet();

        Assert.Equal(20, names.Count);
        // SHA-256 hex, so nothing shorter than the old SHA-1 output can have slipped through.
        Assert.All(names, n => Assert.Equal(64, n!.Length));
    }
}
