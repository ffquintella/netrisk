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
using Model.ClientData;
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

    // The runtime test above can only ever see the host it runs on; these pin every platform,
    // including the Intel Mac no CI runner is any more.
    [Theory]
    [InlineData("WINDOWS", Architecture.X64, "windows")]
    [InlineData("WINDOWS", Architecture.Arm64, "windows")]
    [InlineData("LINUX", Architecture.X64, "linux")]
    [InlineData("LINUX", Architecture.Arm64, "linux")]
    [InlineData("OSX", Architecture.Arm64, "mac-a64")]
    // Still reported after T341: it is the truth, and it is what lets the server refuse the client
    // rather than answer it with the Apple Silicon build.
    [InlineData("OSX", Architecture.X64, "mac-x64")]
    [InlineData(null, Architecture.X64, "unknown")]
    [InlineData("FREEBSD", Architecture.X64, "unknown")]
    public void TestOsNameForEveryPlatform(string? platform, Architecture architecture, string expected)
    {
        var osPlatform = platform is null ? (OSPlatform?)null : OSPlatform.Create(platform);

        Assert.Equal(expected, SystemRestService.OsNameFor(osPlatform, architecture));
    }

    // ---------- Retired platform: macOS on Intel (T341) ----------

    /// <summary>A client that believes it is running on an Intel Mac, whatever the host is.</summary>
    private sealed class IntelMacSystemRestService(IRestService restService) : SystemRestService(restService)
    {
        public override string GetCurrentOsName() => ClientPlatforms.MacIntel;
    }

    private IntelMacSystemRestService IntelMac()
    {
        // The stub answers as a server that predates T341 would, so a refusal can only come from
        // the client itself.
        _backend.OnGet("/System/UpdateScript/mac-x64", JsonString("update-script-payload"));
        _backend.OnGet("/System/ClientDownloadLocation/mac-x64",
            JsonString("https://localhost:5443/installers/GUIClient-Mac-a64.dmg"));
        return new IntelMacSystemRestService(_backend);
    }

    [Fact]
    public void TestAnIntelMacIsNotASupportedPlatform()
    {
        Assert.False(IntelMac().IsCurrentPlatformSupported());
    }

    [Fact]
    public void TestTheHostPlatformIsSupportedUnlessItIsAnIntelMac()
    {
        var intelMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                       && RuntimeInformation.OSArchitecture != Architecture.Arm64;

        Assert.Equal(!intelMac, _service.IsCurrentPlatformSupported());
    }

    [Fact]
    public void TestAnIntelMacRefusesToDownloadAnUpgradeScriptWithoutAskingTheServer()
    {
        var service = IntelMac();

        var ex = Assert.Throws<PlatformNotSupportedException>(() => service.DownloadUpgradeScript());

        Assert.Equal(ClientPlatforms.MacIntelUnsupportedMessage, ex.Message);
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestAnIntelMacRefusesToDownloadAnInstallerWithoutAskingTheServer()
    {
        var service = IntelMac();

        var ex = Assert.Throws<PlatformNotSupportedException>(() => service.DownloadApplication());

        Assert.Equal(ClientPlatforms.MacIntelUnsupportedMessage, ex.Message);
        Assert.Empty(_backend.Requests);
    }

    // ExecuteUpgrade goes through the same guard but is deliberately not exercised here: if the guard
    // ever regressed, the method would start a process and call Environment.Exit(0), ending the test
    // run with a success code.

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
