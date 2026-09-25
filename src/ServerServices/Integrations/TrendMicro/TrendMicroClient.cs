using System.Text.Json;
using DAL.Entities;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.TrendMicro;

/// <summary>
/// The Vision One v3.0 REST surface NetRisk uses (Track 4 milestone 4.4).
///
/// A thin, injectable client rather than calls scattered through the sync service: the parsing is the
/// part most likely to be wrong (Vision One's field names differ between the ASRM endpoints and it
/// pages with an opaque <c>nextLink</c>), and having it in one place makes it testable against
/// captured payloads without a network.
///
/// Field extraction is deliberately tolerant. Vision One has changed attribute names between preview
/// and GA more than once, so each value is read from a list of candidate names and a missing one
/// yields null rather than an exception — an inventory sync that drops one optional field is better
/// than one that fails entirely.
/// </summary>
public class TrendMicroClient(ILogger logger, IOutboundHttpClient http) : ITrendMicroClient
{
    /// <summary>The ASRM inventory endpoint, named once because three call sites report failures against it.</summary>
    private const string DevicesPath = "/v3.0/asrm/attackSurfaceDevices";

    /// <summary>
    /// The per-device CVE endpoint — "Get CVEs detected in a device" in Vision One's own words.
    ///
    /// This is the only Vision One path that carries CVE identities per asset. The inventory endpoint
    /// above carries <c>cveCount</c> and nothing else, so a sync that reads the inventory for CVEs
    /// reports zero findings on a tenant with tens of thousands of them — which is exactly what
    /// happened between 2.19.6 and this fix. Two things differ from the inventory endpoint and both
    /// bite: the role needs *Dashboards &amp; Reports → Reports → View* rather than (only) the ASRM
    /// permission, and the tenant needs Flex credits allocated to Cyber Risk Exposure Management.
    /// </summary>
    private const string VulnerableDevicesPath = "/v3.0/asrm/vulnerableDevices";

    /// <summary>
    /// Page size for the paged reads. Vision One accepts <c>top</c> only from a fixed set —
    /// 10, 50, 100, 200, 500, 1000 — and 200 balances the page count against the payload size.
    /// </summary>
    private const int PageSize = 200;

    /// <summary>
    /// Page size for <see cref="VulnerableDevicesPath"/>, whose accepted set stops at 200 — it takes
    /// 10, 50, 100, 200 and nothing above. Separate from <see cref="PageSize"/> so that raising the
    /// inventory page size to the 500 or 1000 that endpoint allows cannot silently turn every CVE read
    /// into a 400.
    ///
    /// 50 rather than the 200 the endpoint allows, because this endpoint nests every CVE record under
    /// its device: a real tenant answered <c>top=200</c> with a 41,787,890-byte page — around 209 KB
    /// per device — which the outbound response cap refused, failing the whole sync. A quarter of the
    /// rows is a quarter of the page, and the extra round trips are cheap next to a sync that cannot
    /// complete. The cap is raised too (<see cref="PagedResponseBytes"/>); page size alone is not a
    /// bound, since one device's CVE list has no documented ceiling.
    /// </summary>
    private const int VulnerablePageSize = 50;

    /// <summary>
    /// Response cap for the paged ASRM reads, above the 16 MiB default.
    ///
    /// The default is sized for "a page of JSON from a third-party API" and Vision One's vulnerable-device
    /// pages are an order of magnitude past that, because each row carries the device's full CVE list
    /// rather than a count. The number is chosen to keep a page that is several times larger than the
    /// worst one observed readable while still bounding a misconfigured or hostile remote — the reason
    /// the cap exists at all — and it applies only to these two endpoints, not to the seam's default.
    /// </summary>
    private const long PagedResponseBytes = 96L * 1024 * 1024;

    /// <summary>
    /// Page size for the connection test. The obvious <c>top=1</c> is not in Vision One's accepted
    /// set, so it answers 400 — a test that reports a broken connection for a healthy key.
    /// 10 is the smallest value the API actually takes.
    /// </summary>
    private const int ProbePageSize = 10;

    /// <summary>
    /// How much of a Vision One error body is kept. Enough for a code and a sentence; the sync log column
    /// is bounded and an HTML error page would otherwise fill it.
    /// </summary>
    private const int MaxErrorDetail = 400;

    /// <summary>
    /// Hard cap on pages followed in one sync. A runaway <c>nextLink</c> loop would otherwise page
    /// forever; 500 pages at 200 rows is 100,000 devices, which is beyond any real tenant.
    /// </summary>
    private const int MaxPages = 500;

    /// <summary>
    /// How many times one Vision One request is attempted before the sync gives up on it.
    ///
    /// Three, and only for the failures where trying again can plausibly work — a timeout, a 429, a
    /// 5xx, a connection that never landed. A 401 or a 403 is a configuration problem and retrying it
    /// only delays the operator finding out, so <see cref="OutboundHttpResponse.IsRetryable"/> decides.
    /// The crawls this protects are long (a tenant of 17,000 devices is hundreds of pages), which makes
    /// a single transient failure near the end expensive: it used to discard the whole run.
    /// </summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Waits before the second and third attempts, used when the remote did not ask for a specific
    /// back-off of its own. Short enough not to stall a sync, long enough to outlast the blip.
    /// </summary>
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)];

    /// <summary>
    /// Ceiling on an honoured <c>Retry-After</c>. Vision One's rate limiter has answered with minutes,
    /// and a sync job that sleeps for an hour holds the connection's single-flight lock the whole time.
    /// </summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Base per-request timeout for the paged reads.
    ///
    /// Each retry gets this again on top (60s, then 120s, then 180s). A vulnerable-devices page is tens
    /// of megabytes on a large tenant, and the observed failure was exactly this: the first attempt ran
    /// out of time on a page Vision One was still sending. Growing the budget per attempt fixes the slow
    /// page without giving every healthy page an unbounded one.
    /// </summary>
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The sleep between attempts. A hook rather than a bare <c>Task.Delay</c> so the retry tests run in
    /// microseconds instead of half a minute; nothing outside the tests replaces it.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        (delay, ct) => Task.Delay(delay, ct);

    public async Task<ConnectionTestResult> TestAsync(TrendMicroConnection connection, string? apiKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ConnectionTestResult.Fail("No API key is configured for this connection.");

        // A one-row read of the endpoint the sync actually uses. A /whoami-style probe would pass with
        // a token that lacks the ASRM permission, which is the failure that matters here.
        var response = await GetAsync(connection, apiKey,
            $"{DevicesPath}?top={ProbePageSize}", ct);

        if (response.IsSuccess)
        {
            var details = new Dictionary<string, string> { ["Region"] = connection.Region };

            try
            {
                using var document = JsonDocument.Parse(response.Body!);
                if (document.RootElement.TryGetProperty("totalCount", out var total))
                    details["Devices visible"] = total.ToString();
            }
            catch (JsonException)
            {
                // Cosmetic.
            }

            // The inventory probe alone would pass for a key that cannot read CVEs, which is the exact
            // configuration that imports 16,000 hosts and no findings — the two endpoints need
            // different role permissions. A connection asked to sync vulnerabilities is only healthy
            // if both answer, so both are probed.
            if (connection.SyncVulnerabilities)
            {
                var cves = await GetAsync(connection, apiKey,
                    $"{VulnerableDevicesPath}?top={ProbePageSize}&cveDetectionStatus=affected", ct);

                if (!cves.IsSuccess)
                    return ConnectionTestResult.Fail(
                        "Vision One accepted the key for the device inventory but not for CVEs, so this "
                        + "connection would import hosts and no findings. "
                        + FailureMessage(connection, cves, VulnerableDevicesPath));

                details["CVE access"] = "granted";
            }

            return ConnectionTestResult.Ok(
                $"Connected to Vision One in region '{connection.Region}'.", details);
        }

        return ConnectionTestResult.Fail(FailureMessage(connection, response, DevicesPath));
    }

    public async Task<List<TrendMicroDevice>> GetDevicesAsync(TrendMicroConnection connection, string? apiKey,
        Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        var devices = new List<TrendMicroDevice>();

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"{DevicesPath}?top={PageSize}", progress, ct))
        {
            var device = ParseDevice(item);
            if (device != null) devices.Add(device);
        }

        return devices;
    }

    public async Task<List<TrendMicroDeviceVulnerability>> GetVulnerableDevicesAsync(
        TrendMicroConnection connection, string? apiKey, Func<string, Task>? progress = null,
        CancellationToken ct = default)
    {
        // cveDetectionStatus=affected is the default, but it is stated: the alternative ("any") returns
        // every discovered device with an empty cveRecords array, which is a full second crawl of the
        // tenant for rows this method discards.
        var findings = new List<TrendMicroDeviceVulnerability>();
        var devices = 0;

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"{VulnerableDevicesPath}?top={VulnerablePageSize}&cveDetectionStatus=affected",
                           progress, ct))
        {
            devices++;
            findings.AddRange(ParseDeviceVulnerabilities(item));
        }

        // Silence here would read as "this tenant has no vulnerabilities", which is the one conclusion
        // the caller must not draw from a payload that simply carried no CVE array.
        if (devices > 0 && findings.Count == 0)
            logger.Warning(
                "Vision One returned {Devices} devices for connection {Connection} and no vulnerability "
                + "data on any of them; confirm the CVE payload shape before trusting an empty result",
                devices, connection.Name);

        return findings;
    }

    public async Task<bool> UpdateDeviceAsync(TrendMicroConnection connection, string? apiKey,
        string deviceId, int? criticality, string? note, CancellationToken ct = default)
    {
        // Vision One's ASRM update endpoint takes an array of operations, one per device.
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = deviceId,
                assetCriticality = criticality,
                description = note
            }
        });

        var (response, attempts) = await SendWithRetryAsync(
            _ => new OutboundHttpRequest
            {
                Method = "POST",
                Url = connection.BaseUrl.TrimEnd('/') + DevicesPath + "/update",
                Body = payload,
                Headers = { ["Authorization"] = "Bearer " + apiKey }
            },
            connection, "the device write-back", null, ct);

        if (response.IsSuccess) return true;

        logger.Warning("Vision One refused an update for device {Device}: {Reason}",
            deviceId, FailureMessage(connection, response, DevicesPath + "/update", attempts));

        return false;
    }

    /// <summary>
    /// Walks a paged ASRM list endpoint, following <c>nextLink</c>.
    ///
    /// <c>nextLink</c> is an absolute URL Vision One builds itself, so it is used verbatim rather than
    /// having a page number reconstructed from it — rebuilding the query is how a paged sync silently
    /// re-reads page one forever.
    /// </summary>
    private async IAsyncEnumerable<JsonElement> EnumerateAsync(TrendMicroConnection connection, string? apiKey,
        string firstPath, Func<string, Task>? progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var url = connection.BaseUrl.TrimEnd('/') + firstPath;
        var page = 0;

        while (url != null && page++ < MaxPages)
        {
            var currentPage = page;
            var currentUrl = url;

            var (response, attempts) = await SendWithRetryAsync(
                attempt => new OutboundHttpRequest
                {
                    Method = "GET",
                    Url = currentUrl,
                    Headers = { ["Authorization"] = "Bearer " + apiKey },
                    // More time on each retry: the failure this exists for is a page Vision One was
                    // still sending when the first budget ran out.
                    Timeout = PageTimeout * attempt,
                    MaxResponseBytes = PagedResponseBytes
                },
                connection, $"page {currentPage} of {Path(currentUrl)}", progress, ct);

            if (!response.IsSuccess)
                throw new IntegrationRequestException("Trend Micro Vision One",
                    $"Reading page {currentPage} of {Path(currentUrl)} failed. "
                    + FailureMessage(connection, response, Path(currentUrl), attempts));

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(response.Body!);
            }
            catch (JsonException ex)
            {
                throw new IntegrationRequestException("Trend Micro Vision One",
                    $"Vision One returned a body that is not JSON: {ex.Message}");
            }

            using (document)
            {
                if (document.RootElement.TryGetProperty("items", out var items)
                    && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                        // Cloned: the JsonDocument is disposed at the end of this block, and an element
                        // that outlives its document reads freed memory.
                        yield return item.Clone();
                }

                url = document.RootElement.TryGetProperty("nextLink", out var next)
                    ? next.GetString()
                    : null;
            }
        }

        if (page >= MaxPages)
            logger.Warning("Stopped paging Vision One after {Pages} pages; the result set was truncated",
                MaxPages);
    }

    /// <summary>
    /// Maps one ASRM device object. Attribute names are read from candidate lists because Vision One
    /// has used more than one spelling for several of them.
    /// </summary>
    internal static TrendMicroDevice? ParseDevice(JsonElement item)
    {
        var id = FirstString(item, "id", "agentGuid", "deviceId", "endpointId");

        // Without an id there is nothing to key the host on, so the row is dropped rather than
        // creating a host that the next sync duplicates.
        if (string.IsNullOrWhiteSpace(id)) return null;

        var device = new TrendMicroDevice
        {
            Id = id,
            Name = FirstString(item, "name", "endpointName", "deviceName", "hostname"),
            Fqdn = FirstString(item, "fqdn", "dnsName"),
            OperatingSystem = FirstString(item, "osName", "osPlatform", "os", "operatingSystem", "platform"),
            OsVersion = FirstString(item, "osVersion", "osBuild", "version"),
            RiskLevel = FirstString(item, "riskLevel", "riskScoreLevel")
        };

        device.IpAddresses.AddRange(StringList(item, "ip", "ips", "ipAddresses", "ipAddress"));
        device.MacAddresses.AddRange(StringList(item, "mac", "macAddresses", "macAddress"));

        device.Criticality = NormalizeCriticality(item);

        // latestRiskScore first: it is the name in Vision One's own filter and orderBy documentation
        // for attackSurfaceDevices. The others are kept for the shapes earlier previews returned.
        var risk = FirstNumber(item, "latestRiskScore", "riskScore", "assetRiskScore", "cyberRiskScore");
        if (risk != null) device.RiskScore = (int)Math.Clamp(Math.Round(risk.Value), 0, 100);

        var lastSeen = FirstString(item, "lastSeenDateTime", "lastUsedIp", "lastActivity");
        if (DateTime.TryParse(lastSeen, out var parsed)) device.LastSeen = parsed.ToUniversalTime();

        return device;
    }

    /// <summary>
    /// Expands one vulnerable-device object into one finding per CVE.
    ///
    /// Vision One reports vulnerabilities nested under the device, and a per-CVE finding is what NetRisk
    /// tracks — one finding per device listing thirty CVEs cannot be triaged or given an SLA.
    /// </summary>
    internal static List<TrendMicroDeviceVulnerability> ParseDeviceVulnerabilities(JsonElement item)
    {
        var results = new List<TrendMicroDeviceVulnerability>();

        var deviceId = FirstString(item, "id", "agentGuid", "deviceId", "endpointId") ?? string.Empty;
        var deviceName = FirstString(item, "name", "endpointName", "deviceName", "hostname");

        // cveRecords is the name on /v3.0/asrm/vulnerableDevices; the rest are shapes earlier previews
        // of this integration were written against and cost nothing to keep reading.
        var vulnerabilities = FirstArray(item, "cveRecords", "vulnerabilities", "cveList", "cves",
            "detectedVulnerabilities");

        if (vulnerabilities == null) return results;

        // vulnerableDevices dates the scan, not the individual CVE, so the device's own timestamp is
        // the only "when was this last seen" the endpoint offers.
        var deviceLastScanned = FirstString(item, "lastScannedDateTime", "lastDetectDateTime");

        foreach (var entry in vulnerabilities.Value.EnumerateArray())
        {
            // A bare string list of CVE ids is one of the shapes Vision One returns.
            if (entry.ValueKind == JsonValueKind.String)
            {
                var bare = entry.GetString();
                if (string.IsNullOrWhiteSpace(bare)) continue;

                results.Add(new TrendMicroDeviceVulnerability
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    CveId = bare
                });

                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object) continue;

            var cve = FirstString(entry, "cveId", "cve", "id", "name");
            if (string.IsNullOrWhiteSpace(cve)) continue;

            var status = FirstString(entry, "mitigationStatus", "status");

            // "closed" is Vision One's word for the console's "Remediated", and "dismissed" is a CVE an
            // analyst discarded. Neither is an open finding, and ingesting one would create a NetRisk
            // finding nothing ever closes — this import is deliberately not a full scan, so the
            // lifecycle service will not resolve it. "accepted" is kept on purpose: an acceptance
            // recorded in Vision One is not an acceptance recorded in NetRisk, and the triager has to
            // see the CVE to make that decision here.
            if (IsResolved(status)) continue;

            // protectionRules is, in Vision One's own words, "the list of prevention rules associated
            // with the CVE" — rules that exist for it, not rules proven to be enforced on this device.
            // Reading one as a compensating control is how VirtualPatchClosesFinding would mitigate
            // findings that nothing protects, so the applied state comes from mitigationStatus and the
            // rule id is only recorded next to it.
            var patchRule = FirstRuleId(entry);
            var patched = FirstBool(entry, "virtualPatchApplied", "isVirtualPatched", "vulnerabilityProtection")
                          ?? string.Equals(status, "mitigated", StringComparison.OrdinalIgnoreCase);

            var finding = new TrendMicroDeviceVulnerability
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                CveId = cve,
                Title = FirstString(entry, "title", "name", "summary") ?? cve,
                Description = FirstString(entry, "description", "detail", "summary")
                              ?? DescribeComponents(entry),
                CvssScore = FirstNumber(entry, "cvssScore", "cvss", "cvssBaseScore", "baseScore"),
                // eventRiskLevel is the risk level Vision One assigns the event this CVE raised, which
                // is the closest thing the payload has to a severity; globalExploitActivityLevel is a
                // last resort because it describes the CVE in the world, not on this device.
                Severity = FirstString(entry, "severity", "eventRiskLevel", "riskLevel", "cvssSeverity",
                    "globalExploitActivityLevel"),
                EpssScore = FirstNumber(entry, "epssScore", "epss", "exploitProbability"),
                ExploitAvailable = ExploitObserved(entry),
                MitigationStatus = status,
                VirtualPatchApplied = patched,
                VirtualPatchRuleId = patchRule
            };

            var first = FirstString(entry, "firstDetectedDateTime", "firstDetected", "detectedDateTime");
            if (DateTime.TryParse(first, out var firstParsed)) finding.FirstDetected = firstParsed.ToUniversalTime();

            var last = FirstString(entry, "lastDetectedDateTime", "lastDetected", "updatedDateTime")
                       ?? deviceLastScanned;
            if (DateTime.TryParse(last, out var lastParsed)) finding.LastDetected = lastParsed.ToUniversalTime();

            results.Add(finding);
        }

        return results;
    }

    /// <summary>
    /// Whether a mitigation status means the CVE is no longer an open finding on this device.
    /// Vision One's <c>closed</c> is the console's "Remediated".
    /// </summary>
    private static bool IsResolved(string? status) =>
        string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "dismissed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first prevention-rule id on a CVE record.
    ///
    /// Vision One calls the array <c>protectionRules</c> on <c>vulnerableDevices</c> and
    /// <c>preventionRules</c> on the CVE-centric endpoints; both hold <c>{id, product, name}</c>.
    /// </summary>
    internal static string? FirstRuleId(JsonElement entry)
    {
        var flat = FirstString(entry, "virtualPatchRuleId", "ipsRuleId", "ruleId");
        if (flat != null) return flat;

        var rules = FirstArray(entry, "protectionRules", "preventionRules");

        if (rules == null) return null;

        foreach (var rule in rules.Value.EnumerateArray())
        {
            if (rule.ValueKind == JsonValueKind.String)
            {
                var bare = rule.GetString();
                if (!string.IsNullOrWhiteSpace(bare)) return bare;
                continue;
            }

            if (rule.ValueKind != JsonValueKind.Object) continue;

            var id = FirstString(rule, "id", "ruleId");
            if (id != null) return id;
        }

        return null;
    }

    /// <summary>
    /// Whether Vision One has observed this CVE being exploited.
    ///
    /// <c>globalExploitActivityLevel</c> is the field that carries it — Vision One documents its
    /// <c>high</c> as the console's "Actively exploited" — and <c>exploitAttemptCount</c> counts
    /// attempts seen in this tenant. Either one is exploitation in the wild; a payload with neither
    /// says nothing, which is not the same as "no exploit exists".
    /// </summary>
    internal static bool ExploitObserved(JsonElement entry)
    {
        var flag = FirstBool(entry, "exploitAvailable", "hasExploit", "exploitStatus");
        if (flag != null) return flag.Value;

        if ((FirstNumber(entry, "exploitAttemptCount", "exploitAttemptsCount") ?? 0) > 0) return true;

        return string.Equals(FirstString(entry, "globalExploitActivityLevel"), "high",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The affected software as a sentence, used when Vision One supplies no description — which on
    /// <c>vulnerableDevices</c> is always, because the endpoint carries no description field at all.
    /// Without this a Vision One finding reads as a bare CVE id, and the component and its path are
    /// the part a triager needs in order to act.
    /// </summary>
    internal static string? DescribeComponents(JsonElement entry)
    {
        var details = FirstArray(entry, "affectedComponentDetails");

        if (details != null)
        {
            var described = details.Value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => (Name: FirstString(e, "name"), Path: FirstString(e, "filePath")))
                .Where(c => c.Name != null || c.Path != null)
                .Select(c => c.Path == null
                    ? c.Name!
                    : c.Name == null
                        ? c.Path
                        : $"{c.Name} ({c.Path})")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (described.Count > 0)
                return "Affected components reported by Vision One: " + string.Join(", ", described) + ".";
        }

        var names = StringList(entry, "affectedComponents");

        return names.Count == 0
            ? null
            : "Affected components reported by Vision One: " + string.Join(", ", names) + ".";
    }

    /// <summary>
    /// Normalizes an asset-criticality value to 1–5, or null when the device carries none.
    ///
    /// Vision One expresses criticality both as a word ("critical", "high") and as a number, and the
    /// numeric form has appeared on a 0–100 scale as well as 1–5. A 0–100 value is banded rather than
    /// truncated, because truncating 80 to 5 and 20 to 5 alike would flatten the distinction the
    /// customer configured.
    /// </summary>
    internal static int? NormalizeCriticality(JsonElement item)
    {
        var word = FirstString(item, "assetCriticality", "criticality", "importanceScore");

        if (word == null) return null;

        // The word form first: FirstNumber cannot read "critical", so checking the number first would
        // silently drop every word-valued criticality.
        if (!double.TryParse(word, System.Globalization.CultureInfo.InvariantCulture, out var raw))
            return word.Trim().ToLowerInvariant() switch
            {
                "critical" => 5,
                "high" => 4,
                "medium" or "normal" => 3,
                "low" => 2,
                _ => 1
            };

        if (raw > 5) return (int)Math.Clamp(Math.Ceiling(raw / 20.0), 1, 5);

        return (int)Math.Clamp(Math.Round(raw), 1, 5);
    }

    /// <summary>
    /// The one-shot read behind the connection test.
    ///
    /// Deliberately not retried, unlike the sync's paged reads: an operator is watching a spinner, and
    /// answering "could not be reached — timed out" after twenty-five seconds of invisible back-off is
    /// worse than answering it after one attempt. They can press the button again; a nightly sync
    /// cannot.
    /// </summary>
    private Task<OutboundHttpResponse> GetAsync(TrendMicroConnection connection, string? apiKey, string path,
        CancellationToken ct) =>
        http.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Headers = { ["Authorization"] = "Bearer " + apiKey }
        }, ct);

    /// <summary>
    /// Sends one Vision One request, trying again up to <see cref="MaxAttempts"/> times while the
    /// failure is one that trying again can fix.
    ///
    /// Retrying here, at the single request, rather than at the sync: a crawl of a large tenant is
    /// hundreds of pages and tens of minutes, and restarting the whole run because page 212 timed out
    /// throws away everything already read — which is the failure in the field, a run that reached the
    /// CVE step after an hour and ended "Vision One could not be reached: the request timed out".
    ///
    /// What is *not* retried matters as much: <see cref="OutboundHttpResponse.IsRetryable"/> is false
    /// for 401/403/404/400, so a bad key or a missing permission still fails on the first answer with
    /// the diagnosis <see cref="FailureMessage"/> builds, instead of being repeated three times.
    /// </summary>
    /// <param name="build">Builds the request for a given attempt number (1-based), so the timeout can grow.</param>
    /// <param name="what">What is being read, for the log and the progress trail — e.g. "page 12 of /v3.0/…".</param>
    /// <param name="progress">Optional sink for the run's progress trail; a retry an operator cannot see reads as a hang.</param>
    /// <returns>The last response, and how many attempts it took to get it.</returns>
    private async Task<(OutboundHttpResponse Response, int Attempts)> SendWithRetryAsync(
        Func<int, OutboundHttpRequest> build, TrendMicroConnection connection, string what,
        Func<string, Task>? progress, CancellationToken ct)
    {
        OutboundHttpResponse response = null!;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            response = await http.SendAsync(build(attempt), ct);

            if (response.IsSuccess || !response.IsRetryable || attempt == MaxAttempts)
                return (response, attempt);

            // The operator's cancellation is not a transient failure.
            ct.ThrowIfCancellationRequested();

            var wait = Wait(response, attempt);
            var cause = ShortCause(response);

            logger.Warning(
                "Vision One {What} for connection {Connection} failed on attempt {Attempt} of {Max} "
                + "({Cause}); retrying in {Seconds}s",
                what, connection.Name, attempt, MaxAttempts, cause, wait.TotalSeconds);

            if (progress != null)
                await progress($"{what}: attempt {attempt} of {MaxAttempts} failed ({cause}); "
                               + $"retrying in {wait.TotalSeconds:0}s.");

            await DelayAsync(wait, ct);
        }

        return (response, MaxAttempts);
    }

    /// <summary>
    /// How long to wait before the next attempt: the remote's own <c>Retry-After</c> when it sent one
    /// — honouring it is the difference between a rate limit that clears and one that keeps being
    /// re-triggered — and a fixed back-off otherwise, bounded by <see cref="MaxBackoff"/>.
    /// </summary>
    private static TimeSpan Wait(OutboundHttpResponse response, int attempt)
    {
        var requested = response.RetryAfter;

        var wait = requested is { } asked && asked > TimeSpan.Zero
            ? asked
            : Backoff[Math.Min(attempt - 1, Backoff.Length - 1)];

        return wait > MaxBackoff ? MaxBackoff : wait;
    }

    /// <summary>
    /// One phrase naming why an attempt failed, for the log line and the progress trail. The full
    /// diagnosis is <see cref="FailureMessage"/>'s job; this is what fits on a line an operator scans.
    /// </summary>
    internal static string ShortCause(OutboundHttpResponse response) =>
        response.StatusCode == 0
            ? TransportCause(response.TransportError)
            : $"HTTP {response.StatusCode}";

    /// <summary>
    /// Classifies a transport failure — status 0 — into the cause an operator can act on.
    ///
    /// "Vision One could not be reached" is true of a timeout, a DNS failure, a proxy refusing the
    /// connection, a TLS interception and a page larger than the response cap, and those have five
    /// different fixes. The seam already carries the underlying sentence; this names the class of
    /// problem in front of it so the sync log says what to do rather than only what happened.
    /// </summary>
    internal static string TransportCause(string? transportError)
    {
        var text = transportError ?? string.Empty;

        bool Has(string fragment) => text.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        if (Has("timed out") || Has("timeout"))
            return "timed out — Vision One was still answering when the request budget ran out";

        if (Has("byte limit") || Has("over the"))
            return "the page was larger than the response limit";

        if (Has("no such host") || Has("name or service not known") || Has("name resolution"))
            return "the region's host name did not resolve";

        if (Has("ssl") || Has("certificate") || Has("tls"))
            return "the TLS handshake failed — check for an intercepting proxy";

        if (Has("refused") || Has("unreachable") || Has("forcibly closed") || Has("reset"))
            return "the connection was refused or dropped";

        if (Has("blocked") || Has("not allowed") || Has("policy"))
            return "the destination was refused by the outbound policy";

        return string.IsNullOrWhiteSpace(text) ? "no answer" : text;
    }

    // --- tolerant JSON readers --------------------------------------------------------------

    internal static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var value)) continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                    break;
                case JsonValueKind.Number:
                    return value.ToString();
                case JsonValueKind.Array:
                    var first = value.EnumerateArray()
                        .FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
                    if (first.ValueKind == JsonValueKind.String) return first.GetString();
                    break;
            }
        }

        return null;
    }

    internal static double? FirstNumber(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var value)) continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;

            // Numbers arriving as strings is common enough in this API to be worth handling.
            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
                return parsed;
        }

        return null;
    }

    internal static bool? FirstBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGet(element, name, out var value)) continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (bool.TryParse(text, out var parsed)) return parsed;
                    // "enabled"/"applied" are how this API says true in a string field.
                    if (string.Equals(text, "enabled", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(text, "applied", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(text, "disabled", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(text, "notApplied", StringComparison.OrdinalIgnoreCase)) return false;
                    break;
            }
        }

        return null;
    }

    internal static List<string> StringList(JsonElement element, params string[] names)
    {
        var values = new List<string>();

        foreach (var name in names)
        {
            if (!TryGet(element, name, out var value)) continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) values.Add(text.Trim());
                    break;
                case JsonValueKind.Array:
                    values.AddRange(value.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v.Trim()));
                    break;
                case JsonValueKind.Object:
                    // Nested {value: [...]} wrappers appear on some ASRM attributes.
                    values.AddRange(StringList(value, "value", "values", "items"));
                    break;
            }

            if (values.Count > 0) break;
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static JsonElement? FirstArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;

        return null;
    }

    /// <summary>Case-insensitive property lookup; this API is inconsistent about casing.</summary>
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.Object) return false;

        if (element.TryGetProperty(name, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            value = property.Value;
            return true;
        }

        return false;
    }

    // --- failure reporting ------------------------------------------------------------------

    /// <summary>
    /// One operator-facing sentence for a failed Vision One call: what the status code means here, plus
    /// whatever Vision One itself said about it.
    ///
    /// Shared by the connection test, the paged reads and the write-back. The paged reads used to report
    /// a bare "HTTP 403" while the test button explained the same failure in full, so the message that
    /// reached the log and the connection's <c>LastSyncError</c> — the one an operator actually sees — was
    /// the only one with no diagnosis in it.
    /// </summary>
    internal static string FailureMessage(TrendMicroConnection connection, OutboundHttpResponse response,
        string path, int attempts = 1)
    {
        // Named, not implied: a message that reads the same after one attempt and after three hides
        // whether the failure is a blip that outlasted the retries or a standing configuration problem.
        var tried = attempts > 1 ? $" Tried {attempts} times." : string.Empty;

        if (response.StatusCode == 0)
            return $"Vision One could not be reached — {TransportCause(response.TransportError)}. "
                   + $"({response.TransportError}){tried}";

        var detail = DescribeError(response.Body);
        var said = (detail == null ? string.Empty : $" Vision One said: {detail}") + tried;

        return response.StatusCode switch
        {
            401 => "Vision One rejected the API key (401). Keys are region-bound — check that this key was "
                   + $"created in the '{connection.Region}' console.{said}",
            403 => $"Vision One accepted the key but refused {path} (403). {PermissionHint(path)} The "
                   + "role's data and app objects must include the assets, and the tenant needs the "
                   + $"matching entitlement.{said}",
            404 => $"Vision One returned 404 for {path}. Check the region's API base URL.{said}",
            429 => $"Vision One is rate-limiting this key (429). Try again shortly.{said}",
            _ => $"Vision One answered HTTP {response.StatusCode} for {path}.{said}"
        };
    }

    /// <summary>
    /// The role permission the refused endpoint actually requires.
    ///
    /// Not the same for both endpoints, which is a trap worth naming in the message: the ASRM
    /// permission is enough to list the inventory and is *not* enough to read the CVEs, so an operator
    /// who granted it and saw hosts import will reasonably assume the key is fine.
    /// </summary>
    private static string PermissionHint(string path) =>
        path.Contains("vulnerableDevices", StringComparison.OrdinalIgnoreCase)
            ? "The role behind the key needs the \"Dashboards & Reports → Reports → View\" permission "
              + "(the Attack Surface Risk Management permission alone does not cover this endpoint), and "
              + "the tenant needs Flex credits allocated to Cyber Risk Exposure Management."
            : "The role behind the key needs read access to Attack Surface Risk Management (Cyber Risk "
              + "Exposure Management).";

    /// <summary>
    /// Reduces a Vision One error body to one line, or null when the body carries nothing worth saying.
    ///
    /// The status code alone cannot tell the three 403s apart — a role without the ASRM permission, a role
    /// whose data scope excludes the assets, and a tenant without the entitlement all answer 403, and only
    /// the body's <c>error.code</c> distinguishes them. Vision One nests the useful part under
    /// <c>error</c> (sometimes with an <c>innerError</c>), but has also answered with a flat
    /// <c>{"code","message"}</c> and with an <c>errors</c> array, so all three shapes are read; a body that
    /// is not JSON at all is a gateway's error page, which is still worth seeing.
    /// </summary>
    internal static string? DescribeError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGet(root, "error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var nested = DescribeOne(error);
                    if (nested != null) return nested;
                }

                if (TryGet(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    var lines = errors.EnumerateArray()
                        .Select(DescribeOne)
                        .Where(line => line != null)
                        .ToList();

                    if (lines.Count > 0) return Shorten(string.Join("; ", lines));
                }

                var flat = DescribeOne(root);
                if (flat != null) return flat;
            }
        }
        catch (JsonException)
        {
            // Not JSON. Falls through to the raw body below rather than being dropped.
        }

        var collapsed = Collapse(body);

        // An empty object is a successful parse with nothing in it; "Vision One said: {}" is noise.
        return collapsed.Any(char.IsLetterOrDigit) ? Shorten(collapsed) : null;
    }

    /// <summary>One <c>{code, message}</c> object as a line, naming the innerError when it adds something.</summary>
    private static string? DescribeOne(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object) return null;

        var code = FirstString(error, "code", "errorCode");
        var message = FirstString(error, "message", "msg", "detail", "description");

        var head = code == null
            ? message
            : message == null
                ? code
                : $"{code}: {message}";

        if (head == null) return null;

        var inner = TryGet(error, "innerError", out var innerError)
                    && innerError.ValueKind == JsonValueKind.Object
            ? FirstString(innerError, "code", "service")
            : null;

        return Shorten(inner == null ? head : $"{head} (innerError {inner})");
    }

    private static string Collapse(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string Shorten(string text) =>
        text.Length <= MaxErrorDetail ? text : text[..MaxErrorDetail] + "…";

    /// <summary>
    /// The path part of a URL, for a message. The query is dropped on a relative path too: it carries
    /// Vision One's opaque paging token, which is long, useless to an operator and not worth logging.
    /// </summary>
    private static string Path(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : url.Split('?')[0];
}
