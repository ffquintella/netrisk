using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Exceptions;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using RestSharp;
using Xunit;
// DAL.Entities also has a `File` entity, so the same alias the service under test uses is needed
// here to keep `File` meaning System.IO.File.
using File = System.IO.File;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers <see cref="FilesRestService"/> over the stub HTTP backend. The methods that read or write
/// local content use a throwaway directory created per test instance, so nothing outside it is
/// touched.
///
/// <see cref="FilesRestService"/> reports every failure as <see cref="RestComunicationException"/>
/// (it never raises <c>InvalidHttpRequestException</c>), which is why both the "server answered
/// nothing" and the "server errored" branches assert the same type here.
/// </summary>
[TestSubject(typeof(FilesRestService))]
public class FilesRestServiceTest : BaseServiceTest, IDisposable
{
    private readonly StubRestBackend _backend = new();
    private readonly IFilesService _service;
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory("netrisk-files-test");

    public FilesRestServiceTest()
    {
        _service = ResolveWith<IFilesService>(_backend);
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

    /// <summary>Value 18 is the "force download" fallback the service falls back to.</summary>
    private static List<FileType> AllowedTypes() =>
    [
        new() { Value = 1, Name = "text/plain" },
        new() { Value = 4, Name = "application/pdf" },
        new() { Value = 18, Name = "application/force-download" }
    ];

    private string PathIn(string fileName) => Path.Combine(_tempDirectory.FullName, fileName);

    private Uri UriIn(string fileName) => new(PathIn(fileName));

    private Uri WriteTempFile(string fileName, byte[] content)
    {
        var path = PathIn(fileName);
        File.WriteAllBytes(path, content);
        return new Uri(path);
    }

    private static NrFile ServerFile(string type, byte[] content, string uniqueName = "abc-123") => new()
    {
        Id = 1,
        Name = "report.txt",
        UniqueName = uniqueName,
        Type = type,
        Size = content.Length,
        Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        User = 3,
        Content = content
    };

    // ------------------------------------------------- ConvertExtensionToType

    [Theory]
    [InlineData(".csv", "application/csv")]
    [InlineData(".docx", "application/msword")]
    [InlineData(".doc", "application/msword")]
    [InlineData(".bin", "application/octet-stream")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".gz", "application/x-gzip")]
    [InlineData(".gzip", "application/x-gzip")]
    [InlineData(".pdfx", "application/x-pdf")]
    [InlineData(".zip", "application/zip")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".jpg", "image/jpg")]
    [InlineData(".png", "image/png")]
    [InlineData(".pngx", "image/x-png")]
    [InlineData(".csvs", "text/comma-separated-values")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".rtf", "text/rtf")]
    [InlineData(".xml", "text/xml")]
    [InlineData(".unknown-extension", "application/force-download")]
    [InlineData("", "application/force-download")]
    public void TestConvertExtensionToType(string extension, string expected)
    {
        Assert.Equal(expected, _service.ConvertExtensionToType(extension));
    }

    [Theory]
    // KNOWN LIMITATION: the two OpenDocument entries are swapped — ".odt" maps to the
    // spreadsheet mime type and ".ods" to the word-processing one. Asserted as-is so the pairing
    // with ConvertTypeToExtension stays visible rather than silently drifting.
    [InlineData(".odt", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".ods", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void TestConvertExtensionToTypeKeepsTheSwappedOpenDocumentMapping(string extension, string expected)
    {
        Assert.Equal(expected, _service.ConvertExtensionToType(extension));
    }

    // ------------------------------------------------- ConvertTypeToExtension

    [Theory]
    [InlineData("application/msword", ".docx")]
    [InlineData("application/octet-stream", ".bin")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("application/x-gzip", ".gz")]
    [InlineData("application/zip", ".zip")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/jpeg", ".jpeg")]
    [InlineData("image/jpg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/x-png", ".pngx")]
    [InlineData("text/comma-separated-values", ".csv")]
    [InlineData("text/plain", ".txt")]
    [InlineData("text/rtf", ".rtf")]
    [InlineData("text/xml", ".xml")]
    [InlineData("application/unheard-of", ".bin")]
    public void TestConvertTypeToExtension(string type, string expected)
    {
        Assert.Equal(expected, _service.ConvertTypeToExtension(type));
    }

    [Theory]
    // KNOWN LIMITATION: these three do not round-trip with ConvertExtensionToType —
    // "application/csv" yields the misspelled ".cvs", "application/x-pdf" yields ".gzip", and the
    // OpenDocument pair is swapped the same way as above.
    [InlineData("application/csv", ".cvs")]
    [InlineData("application/x-pdf", ".gzip")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".odt")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".ods")]
    public void TestConvertTypeToExtensionKeepsItsNonRoundTrippingEntries(string type, string expected)
    {
        Assert.Equal(expected, _service.ConvertTypeToExtension(type));
    }

    // -------------------------------------------------- GetAllowedTypesAsync

    [Fact]
    public async Task TestGetAllowedTypesAsyncReturnsTheServerList()
    {
        _backend.OnGet("/Files/Types", AllowedTypes());

        var types = await _service.GetAllowedTypesAsync();

        Assert.Equal(3, types.Count);
        Assert.Equal("text/plain", types[0].Name);
        Assert.Equal(18, types[2].Value);
        Assert.Equal("GET /Files/Types", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllowedTypesAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Files/Types", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllowedTypesAsync());
    }

    [Fact]
    public async Task TestGetAllowedTypesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Files/Types", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllowedTypesAsync());
    }

    [Fact]
    public async Task TestGetAllowedTypesAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Files/Types");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllowedTypesAsync());
    }

    // ------------------------------------------------------------- DeleteFile

    [Fact]
    public void TestDeleteFileSendsTheDeleteRequest()
    {
        _backend.OnDelete("/Files/abc-123", "");

        _service.DeleteFile("abc-123");

        Assert.Equal("DELETE /Files/abc-123", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteFileIgnoresANotFoundAnswer()
    {
        _backend.OnStatus(Method.Delete, "/Files/gone", HttpStatusCode.NotFound);

        // KNOWN LIMITATION: the method only guards against a null response, which RestSharp's
        // untyped Delete never returns, so a 404 (the server not knowing the file) is reported to
        // the caller as a successful deletion. Asserted as current behaviour.
        _service.DeleteFile("gone");

        Assert.True(_backend.Sent(Method.Delete, "/Files/gone"));
    }

    [Fact]
    public void TestDeleteFileWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Files/boom", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.DeleteFile("boom"));
    }

    [Fact]
    public void TestDeleteFileWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Files/unreachable");

        Assert.Throws<RestComunicationException>(() => _service.DeleteFile("unreachable"));
    }

    // ------------------------------------------------------------ GetByIdAsync

    [Fact]
    public async Task TestGetByIdAsyncReturnsTheFile()
    {
        var content = "hello"u8.ToArray();
        _backend.OnGet("/Files/Id/9", ServerFile("1", content));

        var file = await _service.GetByIdAsync(9);

        Assert.Equal("report.txt", file.Name);
        Assert.Equal("1", file.Type);
        Assert.Equal(content, file.Content);
        Assert.Equal("GET /Files/Id/9", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenTheFileIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Files/Id/404", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(404));
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Files/Id/5", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(5));
    }

    // ---------------------------------------------------------- GetLocalIdAsync

    [Fact]
    public async Task TestGetLocalIdAsyncReturnsTheServerAllocatedId()
    {
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");

        var id = await _service.GetLocalIdAsync();

        Assert.Equal("local-file-1", id);
        Assert.Equal("GET /Files/local/id", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetLocalIdAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Files/local/id", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetLocalIdAsync());
    }

    [Fact]
    public async Task TestGetLocalIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Files/local/id", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetLocalIdAsync());
    }

    // --------------------------------------------------------- CreateChunkAsync

    [Fact]
    public async Task TestCreateChunkAsyncPostsTheChunk()
    {
        _backend.OnPost("/Files/local/chunk", "\"ok\"");

        await _service.CreateChunkAsync(new FileChunk
        {
            FileId = "local-file-1",
            ChunkNumber = 2,
            TotalChunks = 3,
            ChunkData = "aGVsbG8="
        });

        Assert.Equal("POST /Files/local/chunk", _backend.LastRequest.ToString());
        Assert.Contains("local-file-1", _backend.LastRequest.Body);
        Assert.Contains("aGVsbG8=", _backend.LastRequest.Body);
        Assert.Contains("\"chunkNumber\":2", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"totalChunks\":3", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestCreateChunkAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Files/local/chunk", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateChunkAsync(new FileChunk { FileId = "x", ChunkNumber = 1, TotalChunks = 1 }));
    }

    [Fact]
    public async Task TestCreateChunkAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Files/local/chunk", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateChunkAsync(new FileChunk { FileId = "x", ChunkNumber = 1, TotalChunks = 1 }));
    }

    // -------------------------------------------------------- DownloadFileAsync

    [Fact]
    public async Task TestDownloadFileAsyncWritesTheContentWhenTheExtensionAlreadyMatches()
    {
        var content = "downloaded bytes"u8.ToArray();
        _backend.OnGet("/Files/abc-123", ServerFile("1", content));
        _backend.OnGet("/Files/Types", AllowedTypes());

        await _service.DownloadFileAsync("abc-123", UriIn("saved.txt"));

        Assert.Equal(content, await File.ReadAllBytesAsync(PathIn("saved.txt")));
        Assert.Equal(2, _backend.Requests.Count);
        Assert.Equal("GET /Files/abc-123", _backend.Requests[0].ToString());
        Assert.Equal("GET /Files/Types", _backend.Requests[1].ToString());
    }

    [Fact]
    public async Task TestDownloadFileAsyncCorrectsAnExtensionThatDisagreesWithTheServerType()
    {
        var content = "text not pdf"u8.ToArray();
        _backend.OnGet("/Files/abc-123", ServerFile("1", content));
        _backend.OnGet("/Files/Types", AllowedTypes());

        await _service.DownloadFileAsync("abc-123", UriIn("saved.pdf"));

        // The server said type 1 (text/plain), so the ".pdf" the caller asked for becomes ".txt".
        Assert.True(File.Exists(PathIn("saved.txt")));
        Assert.False(File.Exists(PathIn("saved.pdf")));
        Assert.Equal(content, await File.ReadAllBytesAsync(PathIn("saved.txt")));
    }

    [Fact]
    public async Task TestDownloadFileAsyncAddsAnExtensionWhenTheTargetHasNone()
    {
        var content = "a pdf, really"u8.ToArray();
        _backend.OnGet("/Files/abc-123", ServerFile("4", content));
        _backend.OnGet("/Files/Types", AllowedTypes());

        await _service.DownloadFileAsync("abc-123", UriIn("saved"));

        Assert.True(File.Exists(PathIn("saved.pdf")));
        Assert.Equal(content, await File.ReadAllBytesAsync(PathIn("saved.pdf")));
    }

    [Fact]
    public async Task TestDownloadFileAsyncRejectsATypeTheServerDoesNotAllow()
    {
        _backend.OnGet("/Files/abc-123", ServerFile("99", "x"u8.ToArray()));
        _backend.OnGet("/Files/Types", AllowedTypes());

        var exception = await Assert.ThrowsAsync<Exception>(
            () => _service.DownloadFileAsync("abc-123", UriIn("saved.txt")));

        Assert.Equal("File type not allowed", exception.Message);
        Assert.False(File.Exists(PathIn("saved.txt")));
    }

    [Fact]
    public async Task TestDownloadFileAsyncThrowsWhenTheFileIsMissing()
    {
        _backend.OnStatus(Method.Get, "/Files/gone", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.DownloadFileAsync("gone", UriIn("saved.txt")));
    }

    [Fact]
    public async Task TestDownloadFileAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Files/boom", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.DownloadFileAsync("boom", UriIn("saved.txt")));
    }

    // ---------------------------------------------------------- UploadFileAsync

    private void StubHappyUpload(FileListing listing)
    {
        _backend.OnGet("/Files/Types", AllowedTypes());
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");
        _backend.OnPost("/Files/local/chunk", "\"ok\"");
        _backend.OnPost("/Files/local/complete", listing);
    }

    [Fact]
    public async Task TestUploadFileAsyncSendsTheTypesIdChunkAndCompleteSequence()
    {
        StubHappyUpload(new FileListing
        {
            Name = "upload.txt", UniqueName = "uniq-1", Type = "1", OwnerId = 5
        });
        var source = WriteTempFile("upload.txt", "hello world"u8.ToArray());

        var listing = await _service.UploadFileAsync(source, 5, 3, FileCollectionType.RiskFile);

        Assert.Equal("uniq-1", listing.UniqueName);
        Assert.Equal(5, listing.OwnerId);

        Assert.Equal(4, _backend.Requests.Count);
        Assert.Equal("GET /Files/Types", _backend.Requests[0].ToString());
        Assert.Equal("GET /Files/local/id", _backend.Requests[1].ToString());
        Assert.Equal("POST /Files/local/chunk", _backend.Requests[2].ToString());
        Assert.Equal("/Files/local/complete", _backend.Requests[3].Path);

        // The chunk carries the base64 of the whole (small) file, numbered 1 of 1.
        Assert.Contains("aGVsbG8gd29ybGQ=", _backend.Requests[2].Body);
        Assert.Contains("\"chunkNumber\":1", _backend.Requests[2].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"totalChunks\":1", _backend.Requests[2].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-file-1", _backend.Requests[2].Body);

        // The finalize call carries the metadata only, plus the reassembly hints on the query.
        Assert.Contains("fileId=local-file-1", _backend.Requests[3].Query);
        Assert.Contains("totalChunks=1", _backend.Requests[3].Query);
        Assert.Contains("upload.txt", _backend.Requests[3].Body);
        Assert.Contains("\"size\":11", _backend.Requests[3].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"type\":\"1\"", _backend.Requests[3].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FileCollectionType.RiskFile, "\"riskId\":7")]
    [InlineData(FileCollectionType.MitigationFile, "\"mitigationId\":7")]
    [InlineData(FileCollectionType.IncidentResponsePlanFile, "\"incidentResponsePlanId\":7")]
    [InlineData(FileCollectionType.IncidentResponsePlanTaskFile, "\"incidentResponsePlanTaskId\":7")]
    [InlineData(FileCollectionType.IncidentFile, "\"incidentId\":7")]
    public async Task TestUploadFileAsyncAssociatesTheFileWithTheRightOwner(
        FileCollectionType type, string expectedInBody)
    {
        StubHappyUpload(new FileListing { Name = "upload.txt", UniqueName = "uniq-1", OwnerId = 7 });
        var source = WriteTempFile("upload.txt", "x"u8.ToArray());

        await _service.UploadFileAsync(source, 7, 3, type);

        var body = _backend.Requests[3].Body;
        Assert.Contains(expectedInBody, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"viewType\":{(int)type}", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestUploadFileAsyncStillSendsOneChunkForAnEmptyFile()
    {
        StubHappyUpload(new FileListing { Name = "empty.txt", UniqueName = "uniq-2" });
        var source = WriteTempFile("empty.txt", []);

        await _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile);

        Assert.Equal("POST /Files/local/chunk", _backend.Requests[2].ToString());
        Assert.Contains("\"chunkData\":\"\"", _backend.Requests[2].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("totalChunks=1", _backend.Requests[3].Query);
        Assert.Contains("\"size\":0", _backend.Requests[3].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestUploadFileAsyncFallsBackToTheForceDownloadTypeForAnUnlistedMimeType()
    {
        StubHappyUpload(new FileListing { Name = "archive.zip", UniqueName = "uniq-3" });
        var source = WriteTempFile("archive.zip", "PK"u8.ToArray());

        await _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile);

        // "application/zip" is not in the allowed list, so the service settles for value 18.
        Assert.Contains("\"type\":\"18\"", _backend.Requests[3].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestUploadFileAsyncRejectsATypeThatIsNotAllowedAtAll()
    {
        _backend.OnGet("/Files/Types", new List<FileType> { new() { Value = 1, Name = "text/plain" } });
        var source = WriteTempFile("archive.zip", "PK"u8.ToArray());

        await Assert.ThrowsAsync<TypeNotAllowedException>(
            () => _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile));

        // It gives up before allocating a local id.
        Assert.False(_backend.Sent(Method.Get, "/Files/local/id"));
    }

    [Fact]
    public async Task TestUploadFileAsyncRejectsAPathThatIsNotAnExistingFile()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UploadFileAsync(UriIn("not-there.txt"), 1, 3, FileCollectionType.RiskFile));

        Assert.Equal("filePath", exception.ParamName);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public async Task TestUploadFileAsyncThrowsWhenTheFinalizeCallAnswersNothing()
    {
        _backend.OnGet("/Files/Types", AllowedTypes());
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");
        _backend.OnPost("/Files/local/chunk", "\"ok\"");
        _backend.OnStatus(Method.Post, "/Files/local/complete", HttpStatusCode.NotFound);
        var source = WriteTempFile("upload.txt", "hello"u8.ToArray());

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile));
    }

    [Fact]
    public async Task TestUploadFileAsyncWrapsAServerErrorOnTheFinalizeCall()
    {
        _backend.OnGet("/Files/Types", AllowedTypes());
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");
        _backend.OnPost("/Files/local/chunk", "\"ok\"");
        _backend.OnStatus(Method.Post, "/Files/local/complete", HttpStatusCode.InternalServerError);
        var source = WriteTempFile("upload.txt", "hello"u8.ToArray());

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile));
    }

    [Fact]
    public async Task TestUploadFileAsyncSurfacesAFailingChunkUpload()
    {
        _backend.OnGet("/Files/Types", AllowedTypes());
        _backend.OnGet("/Files/local/id", "\"local-file-1\"");
        _backend.OnStatus(Method.Post, "/Files/local/chunk", HttpStatusCode.InternalServerError);
        var source = WriteTempFile("upload.txt", "hello"u8.ToArray());

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UploadFileAsync(source, 1, 3, FileCollectionType.RiskFile));

        Assert.False(_backend.Sent(Method.Post, "/Files/local/complete"));
    }
}
