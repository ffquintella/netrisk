using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Burp Suite importer, accepting both the Professional XML export and the Enterprise JSON export.
///
/// The two formats describe the same findings with different field names, so both are handled here
/// rather than in two importers that would share their whole severity map and CWE extraction.
/// </summary>
public class BurpImporter : IVulnerabilityReportImporter
{
    public string Name => "burp";
    public string DisplayName => "Burp Suite";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".xml", ".json"];
    public IReadOnlyList<string> SupportedMimeTypes => ["text/xml", "application/xml", "application/json"];

    /// <summary>
    /// Burp's scale. "Information" is its informational band; note that Burp has no Critical, so
    /// High is the ceiling for XML reports.
    /// </summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = NormalizedSeverity.Critical,
            ["high"] = NormalizedSeverity.High,
            ["medium"] = NormalizedSeverity.Medium,
            ["low"] = NormalizedSeverity.Low,
            ["information"] = NormalizedSeverity.None,
            ["informational"] = NormalizedSeverity.None,
            ["info"] = NormalizedSeverity.None,
            ["false positive"] = NormalizedSeverity.None
        });

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "<issues") ||
        ImporterHelpers.Sniff(sample, "\"issue_type\"", "\"severity\"") ||
        ImporterHelpers.Sniff(sample, "\"issue_events\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        var isJson = LooksLikeJson(report);

        return isJson
            ? await ImportJsonAsync(report, ctx, ct)
            : ImportXml(report, ctx, ct);
    }

    private static bool LooksLikeJson(Stream stream)
    {
        var text = ImporterHelpers.PeekText(stream, 4096).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith('{') || text.StartsWith('[');
    }

    private ImportResult ImportXml(Stream report, ImportContext ctx, CancellationToken ct)
    {
        var severities = Severities.WithOverrides(ctx.Options);

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument doc;
        using (var reader = XmlReader.Create(report, settings))
        {
            doc = XDocument.Load(reader);
        }

        var issues = doc.Descendants("issue").ToList();
        if (issues.Count == 0 && doc.Root?.Name.LocalName != "issues")
            throw new InvalidDataException("Not a Burp XML report: no <issue> elements.");

        var result = new ImportResult
        {
            DetectedTool = "burp",
            DetectedToolVersion = doc.Root?.Attribute("burpVersion")?.Value,
            // Burp scans the crawl surface it reached, which is not the whole application.
            IsFullScan = false
        };

        var index = -1;
        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"issue[{index}]";

            try
            {
                var name = issue.Element("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    result.AddWarning("Issue has no name.", reference, skipped: true);
                    continue;
                }

                var severityRaw = issue.Element("severity")?.Value;
                var severity = severities.Map(severityRaw, NormalizedSeverity.Medium);

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var hostElement = issue.Element("host");
                var url = hostElement?.Value;
                var ip = hostElement?.Attribute("ip")?.Value;
                var path = issue.Element("path")?.Value;

                var classifications = issue.Element("vulnerabilityClassifications")?.Value;

                var finding = new NormalizedFinding
                {
                    Tool = "burp",
                    ToolVersion = result.DetectedToolVersion,
                    // serialNumber is per-report, not per-finding-across-reports, so it is not a
                    // dedup identity; the issue type plus the path is.
                    RuleId = issue.Element("type")?.Value,
                    Title = ImporterHelpers.Clip(name, 250)!,
                    Description = ImporterHelpers.Clip(
                        Join(ZapImporter.StripHtml(issue.Element("issueBackground")?.Value),
                            ZapImporter.StripHtml(issue.Element("issueDetail")?.Value)),
                        65500),
                    Solution = Join(ZapImporter.StripHtml(issue.Element("remediationBackground")?.Value),
                        ZapImporter.StripHtml(issue.Element("remediationDetail")?.Value)),
                    Severity = severity,
                    RawSeverity = severityRaw,
                    Host = BuildHost(url, ip),
                    Location = CombineUrl(url, path),
                    Evidence = ZapImporter.StripHtml(issue.Element("issueDetail")?.Value),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                };

                foreach (var cwe in ImporterHelpers.ExtractCwes(classifications).Distinct())
                    finding.Cwes.Add(cwe);

                foreach (var cve in ImporterHelpers.ExtractCves($"{name} {classifications}").Distinct())
                    finding.Cves.Add(cve);

                // Burp reports its own confidence per issue; "Tentative" findings are the ones a
                // triager should look at first, and losing that wastes their time.
                var confidence = issue.Element("confidence")?.Value;
                if (!string.IsNullOrWhiteSpace(confidence))
                    finding.Evidence = $"Confidence: {confidence}\n\n{finding.Evidence}".Trim();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse issue: {ex.Message}", reference, skipped: true);
            }
        }

        return result;
    }

    private async Task<ImportResult> ImportJsonAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        var severities = Severities.WithOverrides(ctx.Options);
        var result = new ImportResult { DetectedTool = "burp", IsFullScan = false };

        // Burp Enterprise nests the findings under issue_events; a plain array of issues is also
        // accepted because that is what its API returns directly.
        var issues = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : ImporterHelpers.Array(root, "issue_events", "issues", "findings").ToList();

        if (issues.Count == 0)
            throw new InvalidDataException("Not a Burp JSON report: no issues found.");

        var index = -1;
        foreach (var entry in issues)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"[{index}]";

            try
            {
                // issue_events wrap the issue itself one level down.
                var issue = ImporterHelpers.Child(entry, "issue") ?? entry;

                var typeBlock = ImporterHelpers.Child(issue, "issue_type");
                var name = ImporterHelpers.Text(issue, "name") ??
                           (typeBlock != null ? ImporterHelpers.Text(typeBlock.Value, "name") : null);

                if (string.IsNullOrWhiteSpace(name))
                {
                    result.AddWarning("Issue has no name.", reference, skipped: true);
                    continue;
                }

                var severityRaw = ImporterHelpers.Text(issue, "severity");
                var severity = severities.Map(severityRaw, NormalizedSeverity.Medium);

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var origin = ImporterHelpers.Text(issue, "origin");
                var path = ImporterHelpers.Text(issue, "path");

                var finding = new NormalizedFinding
                {
                    Tool = "burp",
                    ToolUniqueId = ImporterHelpers.Text(issue, "serial_number", "id"),
                    RuleId = typeBlock != null
                        ? ImporterHelpers.Text(typeBlock.Value, "type_index", "id")
                        : ImporterHelpers.Text(issue, "type_index"),
                    Title = ImporterHelpers.Clip(name, 250)!,
                    Description = ImporterHelpers.Clip(
                        Join(ZapImporter.StripHtml(ImporterHelpers.Text(issue, "description")),
                            ZapImporter.StripHtml(ImporterHelpers.Text(issue, "detail")),
                            typeBlock != null
                                ? ZapImporter.StripHtml(ImporterHelpers.Text(typeBlock.Value, "description_html"))
                                : null),
                        65500),
                    Solution = Join(ZapImporter.StripHtml(ImporterHelpers.Text(issue, "remediation")),
                        typeBlock != null
                            ? ZapImporter.StripHtml(ImporterHelpers.Text(typeBlock.Value, "remediation_html"))
                            : null),
                    Severity = severity,
                    RawSeverity = severityRaw,
                    Host = BuildHost(origin, null),
                    Location = CombineUrl(origin, path),
                    Evidence = ZapImporter.StripHtml(ImporterHelpers.Text(issue, "evidence", "detail")),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                };

                var vulnClassifications = typeBlock != null
                    ? ImporterHelpers.Text(typeBlock.Value, "vulnerability_classifications_html",
                        "vulnerability_classifications")
                    : null;

                foreach (var cwe in ImporterHelpers.ExtractCwes(vulnClassifications).Distinct())
                    finding.Cwes.Add(cwe);

                if (ImporterHelpers.Text(issue, "confidence") is { } confidence)
                    finding.Evidence = $"Confidence: {confidence}\n\n{finding.Evidence}".Trim();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse issue: {ex.Message}", reference, skipped: true);
            }
        }

        return result;
    }

    private static NormalizedHost? BuildHost(string? url, string? ip)
    {
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(ip)) return null;

        string? hostName = null;
        string? port = null;
        string? scheme = null;

        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            hostName = uri.Host;
            scheme = uri.Scheme;
            port = uri.IsDefaultPort ? null : uri.Port.ToString();
        }

        return new NormalizedHost
        {
            Ip = ip,
            HostName = hostName ?? ip,
            Fqdn = hostName,
            ServiceName = scheme,
            Protocol = scheme,
            Port = port
        };
    }

    private static string? CombineUrl(string? origin, string? path)
    {
        if (string.IsNullOrWhiteSpace(origin)) return path;
        if (string.IsNullOrWhiteSpace(path)) return origin;
        return origin.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
    }

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        return kept.Count == 0 ? null : string.Join("\n\n", kept);
    }
}
