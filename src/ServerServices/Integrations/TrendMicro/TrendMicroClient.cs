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
    /// <summary>Page size. Vision One caps ASRM list endpoints at 200.</summary>
    private const int PageSize = 200;

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
            $"/v3.0/asrm/attackSurfaceDevices?top=1", ct);

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

        return response.StatusCode switch
        {
            0 => ConnectionTestResult.Fail($"Vision One could not be reached: {response.TransportError}"),
            401 => ConnectionTestResult.Fail(
                "Vision One rejected the API key (401). Keys are region-bound — check that this key was "
                + $"created in the '{connection.Region}' console."),
            403 => ConnectionTestResult.Fail(
                "Vision One accepted the key but refused the request (403). The key needs the Attack "
                + "Surface Risk Management permission."),
            404 => ConnectionTestResult.Fail(
                "Vision One returned 404 for the ASRM endpoint. Check the region's API base URL."),
            429 => ConnectionTestResult.Fail("Vision One is rate-limiting this key (429). Try again shortly."),
            _ => ConnectionTestResult.Fail($"Vision One answered HTTP {response.StatusCode}.")
        };
    }

    public async Task<List<TrendMicroDevice>> GetDevicesAsync(TrendMicroConnection connection, string? apiKey,
        CancellationToken ct = default)
    {
        var devices = new List<TrendMicroDevice>();

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"/v3.0/asrm/attackSurfaceDevices?top={PageSize}", ct))
        {
            var device = ParseDevice(item);
            if (device != null) devices.Add(device);
        }

        return devices;
    }

    public async Task<List<TrendMicroDevice>> GetHighRiskDevicesAsync(TrendMicroConnection connection,
        string? apiKey, CancellationToken ct = default)
    {
        var devices = new List<TrendMicroDevice>();

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"/v3.0/asrm/highRiskDevices?top={PageSize}", ct))
        {
            var device = ParseDevice(item);
            if (device != null) devices.Add(device);
        }

        return devices;
    }

    public async Task<List<TrendMicroDeviceVulnerability>> GetVulnerableDevicesAsync(
        TrendMicroConnection connection, string? apiKey, CancellationToken ct = default)
    {
        var findings = new List<TrendMicroDeviceVulnerability>();

        await foreach (var item in EnumerateAsync(connection, apiKey,
                           $"/v3.0/asrm/vulnerableDevices?top={PageSize}", ct))
        {
            findings.AddRange(ParseDeviceVulnerabilities(item));
        }

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
            Url = connection.BaseUrl.TrimEnd('/') + "/v3.0/asrm/attackSurfaceDevices/update",
            Body = payload,
            Headers = { ["Authorization"] = "Bearer " + apiKey }
        }, ct);

        if (response.IsSuccess) return true;

        logger.Warning("Vision One refused an update for device {Device}: HTTP {Status}",
            deviceId, response.StatusCode);

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
                    response.StatusCode == 0
                        ? $"Vision One could not be reached: {response.TransportError}"
                        : $"Vision One answered HTTP {response.StatusCode} for {Path(url)}.");

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
            OperatingSystem = FirstString(item, "osName", "os", "operatingSystem", "platform"),
            OsVersion = FirstString(item, "osVersion", "osBuild", "version"),
            RiskLevel = FirstString(item, "riskLevel", "riskScoreLevel")
        };

        device.IpAddresses.AddRange(StringList(item, "ip", "ips", "ipAddresses", "ipAddress"));
        device.MacAddresses.AddRange(StringList(item, "mac", "macAddresses", "macAddress"));

        device.Criticality = NormalizeCriticality(item);

        var risk = FirstNumber(item, "riskScore", "assetRiskScore", "cyberRiskScore");
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

    private static string Path(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
}
