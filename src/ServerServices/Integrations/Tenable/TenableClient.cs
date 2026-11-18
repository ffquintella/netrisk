using System.Text.Json;
using DAL.Entities;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Integrations.Tenable;

/// <summary>The two Tenable API keys of one run, resolved through <c>ISecretResolver</c> and never stored or logged.</summary>
public sealed record TenableCredentials(string AccessKey, string SecretKey)
{
    /// <summary>The values a message must never contain (S40 §11.6): both keys, and the header they form.</summary>
    public IEnumerable<string> Secrets() => [AccessKey, SecretKey, TenableClient.ApiKeysHeaderValue(this)];

    public override string ToString() => "TenableCredentials { *** }";
}

/// <summary>The Tenable Vulnerability Management surface NetRisk reads (M56, T265–T267; S40 §9.4).</summary>
public interface ITenableClient
{
    /// <summary>The probe of S40 §8.7: authentication, then the vulnerability and asset export privileges.</summary>
    Task<TenableTestResult> TestAsync(TenableConnection connection, TenableCredentials credentials,
        CancellationToken ct = default);

    /// <summary>Runs (or joins) a vulnerability export and hands each chunk's JSON body to <paramref name="onChunk"/>.</summary>
    /// <returns>The number of chunks read.</returns>
    Task<int> ExportVulnerabilitiesAsync(TenableConnection connection, TenableCredentials credentials, DateTime since,
        Func<string, Task> onChunk, Func<string, Task> onProgress, CancellationToken ct = default);

    /// <summary>Runs (or joins) an asset export (v2) and hands each chunk's JSON body to <paramref name="onChunk"/>.</summary>
    Task<int> ExportAssetsAsync(TenableConnection connection, TenableCredentials credentials, DateTime? updatedSince,
        Func<string, Task> onChunk, Func<string, Task> onProgress, CancellationToken ct = default);
}

/// <summary>
/// The Tenable export client. Every call goes through <see cref="IOutboundHttpClient"/> (SSRF policy, bounded reads); the
/// <c>X-ApiKeys</c> header is built only into the request and nothing here logs a request header or a URL with a key. A
/// 429 waits for its <c>Retry-After</c>; a 409 on an export start joins the export already running; at most one export
/// of each kind runs per connection because the caller holds the connection's single-flight.
/// </summary>
public class TenableClient(ILogger logger, IOutboundHttpClient http) : ITenableClient
{
    public const string ProviderLabel = "Tenable";

    /// <summary>The fixed Vulnerability Management root (S40 §6.8).</summary>
    public const string VulnerabilityManagementUrl = "https://cloud.tenable.com";

    /// <summary>Assets per vulnerability chunk (S40 §9.4 <c>num_assets</c>).</summary>
    public const int VulnerabilityChunkAssets = 500;

    /// <summary>Assets per asset chunk (S40 §9.4 <c>chunk_size</c>).</summary>
    public const int AssetChunkSize = 1000;

    /// <summary>The chunk response cap (S40 §9.4): a chunk of 500 assets' findings is far larger than an API page.</summary>
    public const long ChunkMaxBytes = 64L * 1024 * 1024;

    /// <summary>How often an export's status is asked, and for how long (S40 §9.4: every 10 s up to 60 min).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan PollLimit = TimeSpan.FromMinutes(60);

    /// <summary>Attempts per request for a 429, a 5xx or a transport failure.</summary>
    private const int MaxAttempts = 5;

    /// <summary>The longest single back-off honoured from a <c>Retry-After</c>.</summary>
    private static readonly TimeSpan MaxBackOff = TimeSpan.FromMinutes(5);

    /// <summary>The wait between attempts and between polls; replaced in tests so nothing sleeps.</summary>
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    /// <summary>The <c>X-ApiKeys</c> value (S40 §9.4). Only ever placed in a request's header dictionary.</summary>
    internal static string ApiKeysHeaderValue(TenableCredentials credentials) =>
        $"accessKey={credentials.AccessKey}; secretKey={credentials.SecretKey};";

    public async Task<TenableTestResult> TestAsync(TenableConnection connection, TenableCredentials credentials,
        CancellationToken ct = default)
    {
        var steps = new List<TenableTestStepDto>();

        var session = await SendAsync(connection, credentials, "GET", "/session", null, ct);
        steps.Add(Step("authentication", session, "TenableTest_AuthenticationFailed", credentials));
        if (!session.IsSuccess) return new TenableTestResult(false, steps);

        var vulns = await SendAsync(connection, credentials, "GET", "/vulns/export/status", null, ct);
        steps.Add(Step("vulns_export", vulns, "TenableTest_VulnsExportPrivilegeMissing", credentials));

        var assets = await SendAsync(connection, credentials, "GET", "/assets/export/status", null, ct);
        steps.Add(Step("assets_export", assets, "TenableTest_AssetsExportPrivilegeMissing", credentials));

        // ACR and AES need a Tenable One or Lumin licence, which only an asset with ratings can show; the first sync
        // confirms it and says "ACR/AES missing: check the licence" when they come back empty (S40 §6.8, §9.4).
        if (connection.CollectAcr || connection.CollectAes)
            steps.Add(new TenableTestStepDto("acr_aes_licence", true, "TenableTest_LicenceCheckedOnSync", null));

        return new TenableTestResult(steps.All(s => s.Passed), steps);
    }

    public Task<int> ExportVulnerabilitiesAsync(TenableConnection connection, TenableCredentials credentials,
        DateTime since, Func<string, Task> onChunk, Func<string, Task> onProgress, CancellationToken ct = default)
    {
        var severities = new[] { "info", "low", "medium", "high", "critical" }
            .Skip(Math.Clamp(connection.MinSeverity, 0, 4)).ToArray();

        var body = JsonSerializer.Serialize(new
        {
            num_assets = VulnerabilityChunkAssets,
            filters = new
            {
                since = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                state = new[] { "OPEN", "REOPENED", "FIXED" },
                severity = severities
            }
        });

        return RunExportAsync(connection, credentials, "vulnerabilities", "/vulns/export", "/vulns/export", body,
            onChunk, onProgress, ct);
    }

    public Task<int> ExportAssetsAsync(TenableConnection connection, TenableCredentials credentials,
        DateTime? updatedSince, Func<string, Task> onChunk, Func<string, Task> onProgress, CancellationToken ct = default)
    {
        var filters = new Dictionary<string, object>();
        if (updatedSince is { } at)
            filters["updated_at"] = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (!connection.IncludeUnlicensed) filters["is_licensed"] = true;

        var body = JsonSerializer.Serialize(new { chunk_size = AssetChunkSize, filters });

        return RunExportAsync(connection, credentials, "assets", "/assets/v2/export", "/assets/export", body,
            onChunk, onProgress, ct);
    }

    /// <summary>
    /// Start (or join) → poll the status until FINISHED → read the chunks in order. A chunk Tenable reports as failed, a
    /// CANCELLED or ERROR export, or a poll past <see cref="PollLimit"/> fails the step, so the caller keeps its cursor.
    /// </summary>
    private async Task<int> RunExportAsync(TenableConnection connection, TenableCredentials credentials, string kind,
        string startPath, string statusRoot, string body, Func<string, Task> onChunk, Func<string, Task> onProgress,
        CancellationToken ct)
    {
        var uuid = await StartExportAsync(connection, credentials, kind, startPath, statusRoot, body, onProgress, ct);

        var read = new HashSet<int>();
        var waited = TimeSpan.Zero;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var status = await SendAsync(connection, credentials, "GET", $"{statusRoot}/{uuid}/status", null, ct);
            if (!status.IsSuccess) throw Failure($"the {kind} export status", status, credentials);

            var (state, available, failed) = ParseStatus(status.Body);

            if (failed.Count > 0)
                throw new IntegrationRequestException(ProviderLabel,
                    $"Tenable reported {failed.Count} failed chunk(s) in the {kind} export; the step is retried on the next sync.");

            foreach (var chunk in available.Where(c => !read.Contains(c)).Order())
            {
                var response = await SendAsync(connection, credentials, "GET", $"{statusRoot}/{uuid}/chunks/{chunk}", null,
                    ct, ChunkMaxBytes);
                if (!response.IsSuccess) throw Failure($"{kind} chunk {chunk}", response, credentials);

                await onChunk(response.Body ?? "[]");
                read.Add(chunk);
                await onProgress($"{kind} export: chunk {chunk} read ({read.Count} so far).");
            }

            switch (state)
            {
                case "FINISHED":
                    return read.Count;
                case "CANCELLED" or "ERROR":
                    throw new IntegrationRequestException(ProviderLabel, $"Tenable ended the {kind} export as {state}.");
            }

            if (waited >= PollLimit)
                throw new IntegrationRequestException(ProviderLabel,
                    $"The {kind} export was still {state ?? "unknown"} after {PollLimit.TotalMinutes:0} minutes.");

            await onProgress($"{kind} export {state?.ToLowerInvariant() ?? "pending"}; {read.Count} chunk(s) read.");
            await DelayAsync(PollInterval, ct);
            waited += PollInterval;
        }
    }

    /// <summary>Starts the export, or on a 409 (a duplicate export, S40 §9.4) joins the newest one still running.</summary>
    private async Task<string> StartExportAsync(TenableConnection connection, TenableCredentials credentials, string kind,
        string startPath, string statusRoot, string body, Func<string, Task> onProgress, CancellationToken ct)
    {
        var started = await SendAsync(connection, credentials, "POST", startPath, body, ct);

        if (started.IsSuccess && ReadString(started.Body, "export_uuid") is { } uuid)
        {
            await onProgress($"{kind} export {uuid} requested.");
            return uuid;
        }

        if (started.StatusCode != 409) throw Failure($"the {kind} export request", started, credentials);

        var list = await SendAsync(connection, credentials, "GET", $"{statusRoot}/status", null, ct);
        if (!list.IsSuccess) throw Failure($"the {kind} export list", list, credentials);

        var running = RunningExport(list.Body)
                      ?? throw new IntegrationRequestException(ProviderLabel,
                          $"Tenable refused a duplicate {kind} export (409) but lists none running.");

        await onProgress($"{kind} export {running} already running; joining it.");
        return running;
    }

    /// <summary>One request with the retry policy: a 429 waits its <c>Retry-After</c>; a 5xx or transport error backs off.</summary>
    private async Task<OutboundHttpResponse> SendAsync(TenableConnection connection, TenableCredentials credentials,
        string method, string path, string? body, CancellationToken ct, long maxBytes = OutboundHttpRequest.DefaultMaxResponseBytes)
    {
        OutboundHttpResponse response = new() { StatusCode = 0, TransportError = "not sent" };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response = await http.SendAsync(new OutboundHttpRequest
            {
                Method = method,
                Url = connection.BaseUrl.TrimEnd('/') + path,
                Body = body,
                Headers = new Dictionary<string, string>
                {
                    ["X-ApiKeys"] = ApiKeysHeaderValue(credentials),
                    ["Accept"] = "application/json"
                },
                Timeout = TimeSpan.FromSeconds(120),
                MaxResponseBytes = maxBytes,
                AllowInvalidCertificate = false
            }, ct);

            if (!response.IsRetryable || attempt == MaxAttempts) return response;

            var wait = response.StatusCode == 429
                ? response.RetryAfter ?? TimeSpan.FromSeconds(30)
                : TimeSpan.FromSeconds(5 * attempt);
            if (wait > MaxBackOff) wait = MaxBackOff;

            logger.Information("Tenable answered {Status} to {Method} {Path}; retrying in {Seconds}s (attempt {Attempt})",
                response.StatusCode, method, path, (int)wait.TotalSeconds, attempt);

            await DelayAsync(wait, ct);
        }

        return response;
    }

    private static TenableTestStepDto Step(string key, OutboundHttpResponse response, string failureKey,
        TenableCredentials credentials) =>
        response.IsSuccess
            ? new TenableTestStepDto(key, true, null, null)
            : new TenableTestStepDto(key, false, failureKey, Describe(response, credentials));

    /// <summary>What went wrong, from the response only — status, transport error, Tenable's own message — never the request.</summary>
    internal static string Describe(OutboundHttpResponse response, TenableCredentials? credentials)
    {
        if (response.StatusCode == 0) return $"Tenable could not be reached: {Redact(response.TransportError, credentials)}";

        // Redacted before it is clipped, so a cut can never leave half a key behind.
        var said = Redact(ReadString(response.Body, "error") ?? ReadString(response.Body, "message"), credentials);
        var reason = response.StatusCode switch
        {
            401 => "Tenable rejected the API keys (401).",
            403 => "The Tenable user lacks the privilege for this call (403).",
            404 => "Tenable does not know this resource (404).",
            429 => "Tenable is rate-limiting these keys (429).",
            _ => $"Tenable answered HTTP {response.StatusCode}."
        };

        return said is null ? reason : $"{reason} {Clip(said, 200)}";
    }

    private static IntegrationRequestException Failure(string what, OutboundHttpResponse response,
        TenableCredentials credentials) =>
        new(ProviderLabel, $"Reading {what} failed: {Describe(response, credentials)}");

    /// <summary>The keys and the header they form, removed from text a remote sent back (S40 §11.6).</summary>
    internal static string? Redact(string? text, TenableCredentials? credentials)
    {
        if (string.IsNullOrEmpty(text) || credentials is null) return text;
        foreach (var secret in credentials.Secrets().Where(s => s.Length > 0).OrderByDescending(s => s.Length))
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return text;
    }

    internal static (string? State, List<int> Available, List<int> Failed) ParseStatus(string? body)
    {
        var available = new List<int>();
        var failed = new List<int>();

        try
        {
            using var document = JsonDocument.Parse(body ?? "{}");
            var root = document.RootElement;
            var state = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()?.ToUpperInvariant()
                : null;

            Read(root, "chunks_available", available);
            Read(root, "chunks_failed", failed);

            return (state, available, failed);
        }
        catch (JsonException ex)
        {
            throw new IntegrationRequestException(ProviderLabel, $"An export status was not valid JSON: {ex.Message}");
        }

        static void Read(JsonElement root, string name, List<int> into)
        {
            if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return;
            foreach (var item in array.EnumerateArray())
                if (item.TryGetInt32(out var id)) into.Add(id);
        }
    }

    /// <summary>The newest QUEUED or PROCESSING export of an <c>…/export/status</c> listing.</summary>
    internal static string? RunningExport(string? body)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "{}");
            if (!document.RootElement.TryGetProperty("exports", out var exports) || exports.ValueKind != JsonValueKind.Array)
                return null;

            return exports.EnumerateArray()
                .Where(e => ReadString(e, "status")?.ToUpperInvariant() is "QUEUED" or "PROCESSING")
                .OrderByDescending(e => e.TryGetProperty("created", out var c) && c.TryGetInt64(out var at) ? at : 0)
                .Select(e => ReadString(e, "uuid"))
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(string? body, string name)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, name) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
