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
    /// Page size for the paged reads. Vision One accepts <c>top</c> only from a fixed set —
    /// 10, 50, 100, 200, 500, 1000 — and 200 balances the page count against the payload size.
    /// </summary>
    private const int PageSize = 200;

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

            return ConnectionTestResult.Ok(
                $"Connected to Vision One in region '{connection.Region}'.", details);
        }

        return ConnectionTestResult.Fail(FailureMessage(connection, response, DevicesPath));
    }

    public async Task<List<TrendMicroDevice>> GetDevicesAsync(TrendMicroConnection connection, string? apiKey,
        CancellationToken ct = default)
    {
        var devices = new List<TrendMicroDevice>();

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"{DevicesPath}?top={PageSize}", ct))
        {
            var device = ParseDevice(item);
            if (device != null) devices.Add(device);
        }

        return devices;
    }

    public async Task<List<TrendMicroDeviceVulnerability>> GetVulnerableDevicesAsync(
        TrendMicroConnection connection, string? apiKey, CancellationToken ct = default)
    {
        // Reads the device inventory, not a dedicated CVE endpoint: this used to call
        // /v3.0/asrm/vulnerableDevices, which is not an endpoint Vision One publishes. The CVE array
        // nested on the device rows is what ParseDeviceVulnerabilities expands.
        var findings = new List<TrendMicroDeviceVulnerability>();
        var devices = 0;

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"{DevicesPath}?top={PageSize}", ct))
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

        var response = await http.SendAsync(new OutboundHttpRequest
        {
            Method = "POST",
            Url = connection.BaseUrl.TrimEnd('/') + DevicesPath + "/update",
            Body = payload,
            Headers = { ["Authorization"] = "Bearer " + apiKey }
        }, ct);

        if (response.IsSuccess) return true;

        logger.Warning("Vision One refused an update for device {Device}: {Reason}",
            deviceId, FailureMessage(connection, response, DevicesPath + "/update"));

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
        string firstPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var url = connection.BaseUrl.TrimEnd('/') + firstPath;
        var page = 0;

        while (url != null && page++ < MaxPages)
        {
            var response = await http.SendAsync(new OutboundHttpRequest
            {
                Method = "GET",
                Url = url,
                Headers = { ["Authorization"] = "Bearer " + apiKey },
                Timeout = TimeSpan.FromSeconds(60)
            }, ct);

            if (!response.IsSuccess)
                throw new IntegrationRequestException("Trend Micro Vision One",
                    FailureMessage(connection, response, Path(url)));

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

        var vulnerabilities = FirstArray(item, "vulnerabilities", "cveList", "cves", "detectedVulnerabilities");

        if (vulnerabilities == null) return results;

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

            var patchRule = FirstString(entry, "virtualPatchRuleId", "ipsRuleId", "ruleId");
            var patched = FirstBool(entry, "virtualPatchApplied", "isVirtualPatched", "vulnerabilityProtection")
                          ?? !string.IsNullOrWhiteSpace(patchRule);

            var finding = new TrendMicroDeviceVulnerability
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                CveId = cve,
                Title = FirstString(entry, "title", "name", "summary") ?? cve,
                Description = FirstString(entry, "description", "detail", "summary"),
                CvssScore = FirstNumber(entry, "cvssScore", "cvss", "cvssBaseScore", "baseScore"),
                Severity = FirstString(entry, "severity", "riskLevel", "cvssSeverity"),
                EpssScore = FirstNumber(entry, "epssScore", "epss", "exploitProbability"),
                ExploitAvailable = FirstBool(entry, "exploitAvailable", "hasExploit", "exploitStatus") ?? false,
                VirtualPatchApplied = patched,
                VirtualPatchRuleId = patchRule
            };

            var first = FirstString(entry, "firstDetectedDateTime", "firstDetected", "detectedDateTime");
            if (DateTime.TryParse(first, out var firstParsed)) finding.FirstDetected = firstParsed.ToUniversalTime();

            var last = FirstString(entry, "lastDetectedDateTime", "lastDetected", "updatedDateTime");
            if (DateTime.TryParse(last, out var lastParsed)) finding.LastDetected = lastParsed.ToUniversalTime();

            results.Add(finding);
        }

        return results;
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

    private Task<OutboundHttpResponse> GetAsync(TrendMicroConnection connection, string? apiKey, string path,
        CancellationToken ct) =>
        http.SendAsync(new OutboundHttpRequest
        {
            Method = "GET",
            Url = connection.BaseUrl.TrimEnd('/') + path,
            Headers = { ["Authorization"] = "Bearer " + apiKey }
        }, ct);

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
        string path)
    {
        if (response.StatusCode == 0)
            return $"Vision One could not be reached: {response.TransportError}";

        var detail = DescribeError(response.Body);
        var said = detail == null ? string.Empty : $" Vision One said: {detail}";

        return response.StatusCode switch
        {
            401 => "Vision One rejected the API key (401). Keys are region-bound — check that this key was "
                   + $"created in the '{connection.Region}' console.{said}",
            403 => $"Vision One accepted the key but refused {path} (403). The role behind the key needs "
                   + "read access to Attack Surface Risk Management (Cyber Risk Exposure Management), the "
                   + "role's data and app objects must include the assets, and the tenant needs the matching "
                   + $"entitlement.{said}",
            404 => $"Vision One returned 404 for {path}. Check the region's API base URL.{said}",
            429 => $"Vision One is rate-limiting this key (429). Try again shortly.{said}",
            _ => $"Vision One answered HTTP {response.StatusCode} for {path}.{said}"
        };
    }

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

    private static string Path(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
}
