using System.Globalization;
using System.Text.Json;
using Contracts.Importers;
using Model.Integrations;

namespace ServerServices.Integrations.Tenable;

/// <summary>The asset fields a Tenable vulnerability record carries, enough to find the host it sits on.</summary>
public sealed record TenableAssetRef(string? Uuid, string? HostName, string? Fqdn, string? Ip, string? Mac, string? Os);

/// <summary>One record of the vulnerability export, normalized but not yet persisted.</summary>
public sealed record TenableVulnerability(string State, TenableAssetRef Asset, NormalizedFinding Finding, double? Epss)
{
    public bool IsFixed => string.Equals(State, "FIXED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One record of the asset export (v2): its identities, tags and ratings.</summary>
public sealed class TenableAsset
{
    public required string Id { get; init; }
    public List<string> HostNames { get; } = [];
    public List<string> Fqdns { get; } = [];
    public List<string> Ipv4s { get; } = [];
    public List<string> MacAddresses { get; } = [];
    public string? OperatingSystem { get; init; }

    /// <summary>The asset's tags in canonical <c>category: value</c> form (<see cref="VendorTagText"/>).</summary>
    public List<string> Tags { get; } = [];

    /// <summary>Asset Criticality Rating, 1–10; null without the licence.</summary>
    public double? Acr { get; init; }

    /// <summary>Asset Exposure Score, 0–1000; null without the licence.</summary>
    public double? Aes { get; init; }

    public DateTime? LastAssessedAt { get; init; }

    /// <summary>A deleted or terminated asset, which the sync skips.</summary>
    public bool Gone { get; init; }

    public string? DisplayName => HostNames.FirstOrDefault() ?? Fqdns.FirstOrDefault() ?? Ipv4s.FirstOrDefault();
}

/// <summary>
/// Maps the Tenable Vulnerability Management export formats onto NetRisk's (M56, T266–T267; S40 §9.4). Pure functions:
/// the finding identity is Tenable's <c>finding_id</c>, the rule is the plugin id, VPR v2 is preferred over VPR, EPSS and
/// exploit maturity are carried, severity is Tenable's 0–4. Field names are read tolerantly (v1 and v2 spellings), since
/// the export schema has moved more than once. Nothing here produces a criticality: ACR is a reading, never a host field.
/// </summary>
public static class TenableNormalizer
{
    /// <summary>The importer name and dedup identity of Tenable VM findings (<c>vulnerabilities.import_source</c>).</summary>
    public const string ImporterName = "tenable-vm";

    public static List<TenableVulnerability> ParseVulnerabilities(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [];

        var parsed = new List<TenableVulnerability>(items.Count);
        foreach (var item in items)
            if (ToVulnerability(item) is { } vulnerability)
                parsed.Add(vulnerability);
        return parsed;
    }

    internal static TenableVulnerability? ToVulnerability(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var plugin = Object(item, "plugin");
        var asset = Object(item, "asset");
        var port = Object(item, "port");

        var findingId = String(item, "finding_id");
        var pluginId = plugin is { } p ? Text(p, "id") : null;
        var assetRef = new TenableAssetRef(
            asset is { } a1 ? String(a1, "uuid") : null,
            asset is { } a2 ? String(a2, "hostname") ?? String(a2, "netbios_name") : null,
            asset is { } a3 ? String(a3, "fqdn") : null,
            asset is { } a4 ? String(a4, "ipv4") : null,
            asset is { } a5 ? String(a5, "mac_address") : null,
            asset is { } a6 ? FirstString(a6, "operating_system") : null);

        if (findingId is null && (pluginId is null || assetRef.Uuid is null)) return null;

        var vprV2 = plugin is { } pv2 ? Object(pv2, "vpr_v2") : null;
        var vpr = plugin is { } pv ? Object(pv, "vpr") : null;
        var vprScore = (vprV2 is { } v2 ? Number(v2, "score") : null) ?? (vpr is { } v1 ? Number(v1, "score") : null);
        var maturity = (plugin is { } pm ? String(pm, "exploit_code_maturity") : null)
                       ?? (vprV2 is { } d2 && Object(d2, "drivers") is { } dr2 ? String(dr2, "exploit_code_maturity") : null)
                       ?? (vpr is { } d1 && Object(d1, "drivers") is { } dr1 ? String(dr1, "exploit_code_maturity") : null);

        var epss = (plugin is { } pe ? Number(pe, "epss_score") ?? Number(pe, "epss") : null)
                   ?? (vprV2 is { } ve ? Number(ve, "epss_score") : null);
        if (epss is > 1 and <= 100) epss /= 100;
        if (epss is < 0 or > 1) epss = null;

        var cvss3 = plugin is { } pc3 ? Number(pc3, "cvss3_base_score") : null;
        var cvss2 = plugin is { } pc2 ? Number(pc2, "cvss_base_score") : null;
        var severityId = (int?)Number(item, "severity_id");
        var severityWord = String(item, "severity");
        var state = (String(item, "state") ?? "OPEN").ToUpperInvariant();

        var finding = new NormalizedFinding
        {
            Tool = ImporterName,
            ToolUniqueId = findingId ?? $"{assetRef.Uuid}:{pluginId}:{(port is { } pk ? Text(pk, "port") : "0")}",
            RuleId = pluginId,
            Title = (plugin is { } pn ? String(pn, "name") : null) ?? $"Tenable plugin {pluginId}",
            Description = plugin is { } pd ? String(pd, "description") ?? String(pd, "synopsis") : null,
            Solution = plugin is { } ps ? String(ps, "solution") : null,
            Severity = MapSeverity(severityId, severityWord, cvss3 ?? cvss2),
            RawSeverity = severityWord ?? severityId?.ToString(CultureInfo.InvariantCulture),
            Cvss3BaseScore = cvss3,
            Cvss3Vector = plugin is { } pvec ? CvssVector(pvec, "cvss3_vector") : null,
            CvssBaseScore = cvss2,
            CvssVector = plugin is { } pvec2 ? CvssVector(pvec2, "cvss_vector") : null,
            VprScore = vprScore,
            ExploitAvailable = plugin is { } px ? Bool(px, "exploit_available") : null,
            ExploitCodeMaturity = maturity,
            FirstSeen = Date(item, "first_found"),
            LastSeen = Date(item, "last_found"),
            VulnerabilityPublicationDate = plugin is { } pp ? Date(pp, "publication_date") : null,
            PatchPublicationDate = plugin is { } ppp ? Date(ppp, "patch_publication_date") : null,
            RawStatus = state.ToLowerInvariant(),
            Evidence = String(item, "output"),
            Host = new NormalizedHost
            {
                Ip = assetRef.Ip,
                HostName = assetRef.HostName,
                Fqdn = assetRef.Fqdn,
                MacAddress = assetRef.Mac,
                OperatingSystem = assetRef.Os,
                Port = port is { } pt ? Text(pt, "port") is { } n && n != "0" ? n : null : null,
                Protocol = port is { } pr ? String(pr, "protocol")?.ToLowerInvariant() : null,
                ServiceName = port is { } sv ? String(sv, "service") : null
            }
        };

        if (plugin is { } pcve && pcve.TryGetProperty("cve", out var cves) && cves.ValueKind == JsonValueKind.Array)
            foreach (var cve in cves.EnumerateArray())
                if (cve.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cve.GetString()))
                    finding.Cves.Add(cve.GetString()!.Trim().ToUpperInvariant());

        if (plugin is { } psee && psee.TryGetProperty("see_also", out var refs) && refs.ValueKind == JsonValueKind.Array)
            foreach (var reference in refs.EnumerateArray())
                if (reference.ValueKind == JsonValueKind.String) finding.References.Add(reference.GetString()!);

        if (assetRef.Uuid is not null) finding.ToolFields["assetUuid"] = assetRef.Uuid;

        return new TenableVulnerability(state, assetRef, finding, epss);
    }

    /// <summary>Tenable's 0–4 severity first, then its word, then CVSS bands.</summary>
    public static NormalizedSeverity MapSeverity(int? severityId, string? word, double? cvss)
    {
        if (severityId is >= 0 and <= 4) return (NormalizedSeverity)severityId.Value;

        return (word ?? "").Trim().ToLowerInvariant() switch
        {
            "critical" => NormalizedSeverity.Critical,
            "high" => NormalizedSeverity.High,
            "medium" => NormalizedSeverity.Medium,
            "low" => NormalizedSeverity.Low,
            "info" or "informational" => NormalizedSeverity.None,
            _ => cvss switch
            {
                null => NormalizedSeverity.Medium,
                >= 9.0 => NormalizedSeverity.Critical,
                >= 7.0 => NormalizedSeverity.High,
                >= 4.0 => NormalizedSeverity.Medium,
                > 0 => NormalizedSeverity.Low,
                _ => NormalizedSeverity.None
            }
        };
    }

    public static List<TenableAsset> ParseAssets(string json)
    {
        using var document = JsonDocument.Parse(json);
        var parsed = new List<TenableAsset>();
        if (document.RootElement.ValueKind != JsonValueKind.Array) return parsed;

        foreach (var item in document.RootElement.EnumerateArray())
            if (ToAsset(item) is { } asset)
                parsed.Add(asset);
        return parsed;
    }

    internal static TenableAsset? ToAsset(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = String(item, "id") ?? String(item, "uuid");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var ratings = Object(item, "ratings");
        var acr = (ratings is { } r1 && Object(r1, "acr") is { } acrNode ? Number(acrNode, "score") : null)
                  ?? Number(item, "acr_score");
        var aes = (ratings is { } r2 && Object(r2, "aes") is { } aesNode ? Number(aesNode, "score") : null)
                  ?? Number(item, "exposure_score");

        var asset = new TenableAsset
        {
            Id = id,
            OperatingSystem = FirstString(item, "operating_systems") ?? FirstString(item, "operating_system"),
            Acr = acr is >= 0 and <= 10 ? acr : null,
            Aes = aes is >= 0 and <= 1000 ? aes : null,
            LastAssessedAt = Date(item, "last_assessed") ?? Date(item, "last_scan_time") ?? Date(item, "last_seen"),
            Gone = Date(item, "deleted_at") is not null || Date(item, "terminated_at") is not null
        };

        AddAll(item, asset.HostNames, "hostnames", "hostname", "netbios_names");
        AddAll(item, asset.Fqdns, "fqdns", "fqdn");
        AddAll(item, asset.Ipv4s, "ipv4s", "ipv4");
        AddAll(item, asset.MacAddresses, "mac_addresses", "mac_address");

        if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            foreach (var tag in tags.EnumerateArray())
            {
                var text = tag.ValueKind switch
                {
                    JsonValueKind.String => VendorTagText.Canonical(tag.GetString()),
                    JsonValueKind.Object => VendorTagText.FromKeyValue(
                        String(tag, "category") ?? String(tag, "key") ?? String(tag, "tag_key"),
                        String(tag, "value") ?? String(tag, "tag_value")),
                    _ => null
                };
                if (text is not null && text.Length <= VendorTagText.MaxLength &&
                    !asset.Tags.Contains(text, StringComparer.OrdinalIgnoreCase))
                    asset.Tags.Add(text);
            }

        return asset;
    }

    // --- JSON helpers --------------------------------------------------------------------------------------------------

    private static void AddAll(JsonElement item, List<string> into, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            var values = value.ValueKind switch
            {
                JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()),
                JsonValueKind.String => [value.GetString()],
                _ => []
            };
            foreach (var text in values)
                if (!string.IsNullOrWhiteSpace(text) && !into.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase))
                    into.Add(text.Trim());
        }
    }

    private static JsonElement? Object(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    /// <summary>A string, or a number written as its invariant text (plugin ids and ports come either way).</summary>
    private static string? Text(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : null;

    private static string? FirstString(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            : null;

    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTime? Date(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
            return at.UtcDateTime;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds > 0)
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return null;
    }

    private static string? CvssVector(JsonElement plugin, string name) =>
        !plugin.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : value.ValueKind == JsonValueKind.Object ? String(value, "raw")
        : null;
}
