using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Serilog;
using ServerServices.Integrations.Tenable;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>
/// M56, T265 (S40 §9.4, §12.2): the export protocol — start, poll, chunks — with the <c>X-ApiKeys</c> header, a 429 that
/// waits its <c>Retry-After</c>, a 409 that joins the running export, the 64 MiB chunk cap and the probe's steps.
/// </summary>
[TestSubject(typeof(TenableClient))]
public class TenableClientTest
{
    private readonly FakeOutboundHttpClient _http = new();
    private readonly List<TimeSpan> _waits = [];
    private readonly TenableClient _client;
    private static readonly TenableCredentials Keys = new(TenableFixtures.AccessKey, TenableFixtures.SecretKey);

    private static readonly TenableConnection Connection = new()
    {
        Id = 1, Name = "Tenable", BaseUrl = TenableClient.VulnerabilityManagementUrl, MinSeverity = 1, CollectAcr = true
    };

    public TenableClientTest()
    {
        _client = new TenableClient(new LoggerConfiguration().CreateLogger(), _http)
        {
            DelayAsync = (wait, _) =>
            {
                _waits.Add(wait);
                return Task.CompletedTask;
            }
        };
    }

    private async Task<List<string>> ExportVulnsAsync()
    {
        var chunks = new List<string>();
        await _client.ExportVulnerabilitiesAsync(Connection, Keys, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            c => { chunks.Add(c); return Task.CompletedTask; }, _ => Task.CompletedTask);
        return chunks;
    }

    [Fact]
    public async Task TestAnExportIsStartedPolledAndReadChunkByChunk()
    {
        _http.EnqueueJson(TenableFixtures.Started())
            .EnqueueJson(TenableFixtures.Status("PROCESSING", 1))
            .EnqueueJson("[1]")
            .EnqueueJson(TenableFixtures.Status("FINISHED", 1, 2))
            .EnqueueJson("[2]");

        Assert.Equal(["[1]", "[2]"], await ExportVulnsAsync());

        var start = _http.Requests[0];
        Assert.Equal(("POST", "https://cloud.tenable.com/vulns/export"), (start.Method, start.Url));
        Assert.Contains("\"num_assets\":500", start.Body);
        Assert.Contains("\"severity\":[\"low\",\"medium\",\"high\",\"critical\"]", start.Body);
        Assert.Contains("\"FIXED\"", start.Body);
        Assert.All(_http.Requests, r => Assert.Equal($"accessKey={Keys.AccessKey}; secretKey={Keys.SecretKey};", r.Headers["X-ApiKeys"]));
        Assert.Equal(TenableClient.ChunkMaxBytes, _http.Requests.Single(r => r.Url.EndsWith("/chunks/2")).MaxResponseBytes);
        Assert.Equal([TenableClient.PollInterval], _waits);
    }

    [Fact]
    public async Task TestA429WaitsForItsRetryAfter()
    {
        _http.EnqueueFailure(429, retryAfter: "17")
            .EnqueueJson(TenableFixtures.Started())
            .EnqueueJson(TenableFixtures.Status("FINISHED", 1))
            .EnqueueJson("[]");

        await ExportVulnsAsync();

        Assert.Equal(TimeSpan.FromSeconds(17), _waits[0]);
        Assert.Equal(2, _http.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public async Task TestA409JoinsTheExportAlreadyRunning()
    {
        _http.EnqueueFailure(409, """{"error":"Duplicate export not allowed"}""")
            .EnqueueJson("""{"exports":[{"uuid":"old","status":"FINISHED","created":1},{"uuid":"live","status":"PROCESSING","created":2}]}""")
            .EnqueueJson(TenableFixtures.Status("FINISHED", 1))
            .EnqueueJson("[]");

        await ExportVulnsAsync();

        Assert.Contains(_http.Requests, r => r.Url.EndsWith("/vulns/export/live/status"));
        Assert.Equal(1, _http.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public async Task TestAFailedChunkOrAnErroredExportFailsTheStep()
    {
        _http.EnqueueJson(TenableFixtures.Started())
            .EnqueueJson("""{"status":"PROCESSING","chunks_available":[],"chunks_failed":[3]}""");
        await Assert.ThrowsAsync<IntegrationRequestException>(ExportVulnsAsync);

        _http.Reset();
        _http.EnqueueJson(TenableFixtures.Started()).EnqueueJson(TenableFixtures.Status("ERROR"));
        await Assert.ThrowsAsync<IntegrationRequestException>(ExportVulnsAsync);
    }

    [Fact]
    public async Task TestAnExportStillRunningAfterTheLimitFails()
    {
        _http.EnqueueJson(TenableFixtures.Started());
        _http.DefaultResponse = new OutboundHttpResponse { StatusCode = 200, Body = TenableFixtures.Status("PROCESSING") };

        await Assert.ThrowsAsync<IntegrationRequestException>(ExportVulnsAsync);

        Assert.Equal((int)(TenableClient.PollLimit / TenableClient.PollInterval), _waits.Count);
    }

    [Fact]
    public async Task TestTheAssetExportUsesV2WithTheCursorAndTheLicenceFilter()
    {
        _http.EnqueueJson(TenableFixtures.Started("a")).EnqueueJson(TenableFixtures.Status("FINISHED", 1)).EnqueueJson("[]");

        await _client.ExportAssetsAsync(Connection, Keys, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => Task.CompletedTask, _ => Task.CompletedTask);

        var start = _http.Requests[0];
        Assert.Equal("https://cloud.tenable.com/assets/v2/export", start.Url);
        Assert.Contains("\"chunk_size\":1000", start.Body);
        Assert.Contains($"\"updated_at\":{new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}", start.Body);
        Assert.Contains("\"is_licensed\":true", start.Body);
        Assert.Contains(_http.Requests, r => r.Url.EndsWith("/assets/export/a/chunks/1"));
    }

    [Fact]
    public async Task TestTheProbeReportsEachStepFromTheResponseOnly()
    {
        _http.EnqueueJson("""{"id":1}""").EnqueueFailure(403, """{"error":"Forbidden"}""").EnqueueJson("""{"exports":[]}""");

        var result = await _client.TestAsync(Connection, Keys);

        Assert.False(result.Success);
        Assert.Equal(["authentication", "vulns_export", "assets_export", "acr_aes_licence"], result.Steps.Select(s => s.StepKey));
        var refused = result.Steps[1];
        Assert.Equal(("TenableTest_VulnsExportPrivilegeMissing", false), (refused.MessageKey, refused.Passed));
        Assert.Contains("403", refused.Detail);
        Assert.False(result.Steps.Any(s => TenableFixtures.Leaks(s.Detail)));
    }

    [Fact]
    public async Task TestARejectedAuthenticationStopsTheProbe()
    {
        _http.EnqueueFailure(401, """{"error":"Invalid Credentials"}""");

        var result = await _client.TestAsync(Connection, Keys);

        Assert.Equal(("authentication", false), (Assert.Single(result.Steps).StepKey, result.Success));
    }

    [Fact]
    public void TestTheCredentialsNeverPrintThemselves() =>
        Assert.False(TenableFixtures.Leaks(Keys.ToString()));
}
