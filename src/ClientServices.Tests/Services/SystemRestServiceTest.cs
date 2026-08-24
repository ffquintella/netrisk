using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(SystemRestService))]
public class SystemRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly ISystemService _service;

    public SystemRestServiceTest()
    {
        _service = ResolveWith<ISystemService>(_backend);
    }

    /// <summary>JSON-quoted, because the service reads the body through <c>Get&lt;string&gt;</c>.</summary>
    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    // ---------- GetClientAssemblyVersion ----------

    [Fact]
    public void TestGetClientAssemblyVersionReturnsTheEntryAssemblyVersion()
    {
        var version = _service.GetClientAssemblyVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        // Asserting the shape rather than a literal: the value tracks Directory.Build.props.
        Assert.True(Version.TryParse(version, out var parsed));
        Assert.Equal(version, parsed!.ToString());
    }

    // ---------- GetTempPath ----------

    [Fact]
    public void TestGetTempPathCreatesTheClientTempDirectory()
    {
        var path = _service.GetTempPath();

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NRGUIClient", "Temp"),
            path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void TestGetTempPathIsStable()
    {
        Assert.Equal(_service.GetTempPath(), _service.GetTempPath());
    }

    // ---------- GetCurrentOsName ----------

    [Fact]
    public void TestGetCurrentOsNameMatchesTheRuntime()
    {
        var os = _service.GetCurrentOsName();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Equal("windows", os);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Assert.Equal("linux", os);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.Equal(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-a64" : "mac-x64", os);
        else
            Assert.Equal("unknown", os);
    }

    // ---------- NeedsUpgrade ----------

    // NOTE: NeedsUpgrade / NeedsUpgradeAsync are compiled out under DEBUG (`#if DEBUG return false;`),
    // so in a Debug test run they short-circuit without touching HTTP. The stub answers with the
    // client's own version so the assertion holds in a Release run too, where the request is made.

    [Fact]
    public void TestNeedsUpgradeIsFalseWhenTheServerReportsTheSameVersion()
    {
        _backend.OnGet("/System/ClientVersion", JsonString(_service.GetClientAssemblyVersion()));

        Assert.False(_service.NeedsUpgrade());
    }

    [Fact]
    public async Task TestNeedsUpgradeAsyncIsFalseWhenTheServerReportsTheSameVersion()
    {
        _backend.OnGet("/System/ClientVersion", JsonString(_service.GetClientAssemblyVersion()));

        Assert.False(await _service.NeedsUpgradeAsync());
    }

    // ---------- DownloadUpgradeScript ----------

    [Fact]
    public void TestDownloadUpgradeScriptWritesTheScriptToTheTempFolder()
    {
        var os = _service.GetCurrentOsName();
        _backend.OnGet($"/System/UpdateScript/{os}", JsonString("update-script-payload"));

        _service.DownloadUpgradeScript();

        var scriptPath = Path.Combine(_service.GetTempPath(), os == "windows" ? "update.bat" : "update.sh");
        Assert.True(File.Exists(scriptPath));
        Assert.Contains("update-script-payload", File.ReadAllText(scriptPath));
        Assert.True(_backend.Sent(Method.Get, $"/System/UpdateScript/{os}"));
    }

    [Fact]
    public void TestDownloadUpgradeScriptThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, $"/System/UpdateScript/{_service.GetCurrentOsName()}", HttpStatusCode.NotFound);

        var ex = Assert.Throws<Exception>(() => _service.DownloadUpgradeScript());

        Assert.Equal("Error getting update script", ex.Message);
    }

    [Fact]
    public void TestDownloadUpgradeScriptWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, $"/System/UpdateScript/{_service.GetCurrentOsName()}",
            HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.DownloadUpgradeScript());

        Assert.Equal("Error getting update script", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestDownloadUpgradeScriptWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, $"/System/UpdateScript/{_service.GetCurrentOsName()}");

        Assert.Throws<RestComunicationException>(() => _service.DownloadUpgradeScript());
    }

    // ---------- DownloadApplication ----------

    [Fact]
    public void TestDownloadApplicationThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, $"/System/ClientDownloadLocation/{_service.GetCurrentOsName()}",
            HttpStatusCode.NotFound);

        var ex = Assert.Throws<Exception>(() => _service.DownloadApplication());

        Assert.Equal("Error getting client download location", ex.Message);
    }

    [Fact]
    public void TestDownloadApplicationWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, $"/System/ClientDownloadLocation/{_service.GetCurrentOsName()}",
            HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.DownloadApplication());

        Assert.Equal("Error client download location", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestDownloadApplicationWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, $"/System/ClientDownloadLocation/{_service.GetCurrentOsName()}");

        Assert.Throws<RestComunicationException>(() => _service.DownloadApplication());
    }

    // ---------- DownloadFile ----------
    // DownloadFile is public on the implementation but not on ISystemService, so the concrete type
    // is needed here.

    [Fact]
    public void TestDownloadFileWritesTheResponseBodyToDisk()
    {
        const string payload = "BINARY-APPLICATION-PAYLOAD";
        _backend.OnGet("/System/ClientDownload/app.bin", payload);

        var outputPath = Path.Combine(Path.GetTempPath(), $"netrisk-download-{Guid.NewGuid():N}.bin");

        ((SystemRestService)_service).DownloadFile(
            new Uri("https://localhost:5443/System/ClientDownload/app.bin"), outputPath);

        Assert.True(_backend.Sent(Method.Get, "/System/ClientDownload/app.bin"));
        Assert.Equal(payload, Encoding.UTF8.GetString(File.ReadAllBytes(outputPath)));
    }

    [Fact]
    public void TestDownloadFileWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/System/ClientDownload/app.bin");

        var outputPath = Path.Combine(Path.GetTempPath(), $"netrisk-download-{Guid.NewGuid():N}.bin");

        var ex = Assert.Throws<RestComunicationException>(() => ((SystemRestService)_service).DownloadFile(
            new Uri("https://localhost:5443/System/ClientDownload/app.bin"), outputPath));

        Assert.Equal("Error downloading file", ex.RestExceptionMessage);
        Assert.False(File.Exists(outputPath));
    }
}
