using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>Records in the documented shapes of the Tenable VM vulnerability and asset exports (v1 and v2 spellings).</summary>
internal static class TenableFixtures
{
    public const string AccessKey = "AK-7f3c9e1d2b4a6c8e0f1a3b5c7d9e1f2a";
    public const string SecretKey = "SK-0a2b4c6d8e1f3a5b7c9d1e3f5a7b9c2d";

    public static string Vuln(string findingId, string state = "OPEN", string assetUuid = "asset-1", string ip = "10.0.0.5",
        string host = "web-01", int severityId = 3, string? vpr = "8.9", string? vprV2 = null, string? epss = "0.42",
        string cves = "\"CVE-2024-0001\"", int pluginId = 19506, string? cvss3 = "9.8", string maturity = "FUNCTIONAL")
    {
        var plugin = new Dictionary<string, object?>
        {
            ["id"] = pluginId, ["name"] = "OpenSSL vulnerability", ["description"] = "desc", ["solution"] = "upgrade",
            ["cve"] = JsonSerializer.Deserialize<string[]>("[" + cves + "]"),
            ["cvss3_vector"] = new { raw = "CVSS:3.0/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H" },
            ["exploit_available"] = true, ["exploit_code_maturity"] = maturity,
            ["publication_date"] = "2024-01-02T00:00:00Z", ["see_also"] = new[] { "https://example.com/advisory" }
        };
        if (cvss3 is not null) plugin["cvss3_base_score"] = Number(cvss3);
        if (vpr is not null) plugin["vpr"] = new { score = Number(vpr) };
        if (vprV2 is not null) plugin["vpr_v2"] = new { score = Number(vprV2) };
        if (epss is not null) plugin["epss_score"] = Number(epss);

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["finding_id"] = findingId, ["state"] = state, ["severity"] = "high", ["severity_id"] = severityId,
            ["first_found"] = "2026-10-01T00:00:00.000Z", ["last_found"] = "2026-11-17T00:00:00.000Z", ["output"] = "plugin output",
            ["asset"] = new
            {
                uuid = assetUuid, hostname = host, fqdn = host + ".corp.example", ipv4 = ip, mac_address = "00:11:22:33:44:55",
                operating_system = new[] { "Linux Kernel 5.15" }
            },
            ["port"] = new { port = 443, protocol = "TCP", service = "www" },
            ["plugin"] = plugin
        });
    }

    public static string Chunk(params string[] records) => "[" + string.Join(",", records) + "]";

    public static string Asset(string id, string host = "web-01", string ip = "10.0.0.5", string? mac = null,
        double? acr = 8, double? aes = 750, string tags = "", bool v1Names = false, string? deleted = null)
    {
        var tagList = JsonSerializer.Deserialize<object[]>("[" + tags + "]");
        if (v1Names)
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = id, ["hostname"] = new[] { host }, ["ipv4"] = new[] { ip }, ["acr_score"] = acr,
                ["exposure_score"] = aes, ["tags"] = tagList
            });

        var asset = new Dictionary<string, object?>
        {
            ["id"] = id, ["hostnames"] = new[] { host }, ["fqdns"] = new[] { host + ".corp.example" }, ["ipv4s"] = new[] { ip },
            ["mac_addresses"] = mac is null ? System.Array.Empty<string>() : new[] { mac },
            ["operating_systems"] = new[] { "Linux" },
            ["ratings"] = new { acr = new { score = acr }, aes = new { score = aes } },
            ["last_assessed"] = "2026-11-16T10:00:00Z", ["tags"] = tagList
        };
        if (deleted is not null) asset["deleted_at"] = deleted;
        return JsonSerializer.Serialize(asset);
    }

    public static string Tag(string category, string value) => JsonSerializer.Serialize(new { category, value });

    public static string Started(string uuid = "exp-1") => JsonSerializer.Serialize(new { export_uuid = uuid });

    public static string Status(string state, params int[] chunks) =>
        JsonSerializer.Serialize(new { status = state, chunks_available = chunks, chunks_failed = System.Array.Empty<int>() });

    public static IEnumerable<string> Secrets() => [AccessKey, SecretKey, $"accessKey={AccessKey}; secretKey={SecretKey};"];

    public static bool Leaks(string? text) => text is not null && Secrets().Any(s => text.Contains(s));

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);
}
