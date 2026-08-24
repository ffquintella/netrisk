using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// OWASP ZAP JSON report importer.
///
/// ZAP groups its output by alert and lists every affected URL as an "instance" underneath. Each
/// instance is imported as its own finding: a CSP header missing on one endpoint and missing on
/// forty are different amounts of work, and collapsing them loses the per-endpoint remediation
/// tracking that is the point of importing a DAST scan at all.
/// </summary>
public class ZapImporter : IVulnerabilityReportImporter
{
    /// <summary>
    /// Cap on instances expanded per alert. A crawl of a large site can report an alert on tens of
    /// thousands of URLs, and importing each as a row helps nobody. Truncation is always reported
    /// as a warning — a silent cap reads as "everything imported" when it was not.
    /// </summary>
    public const int DefaultMaxInstancesPerAlert = 100;

    public const string MaxInstancesOption = "maxInstancesPerAlert";

    public string Name => "zap";
    public string DisplayName => "OWASP ZAP";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json"];

    /// <summary>ZAP's <c>riskcode</c> scale. It has no Critical band; High is its ceiling.</summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = NormalizedSeverity.None,
            ["1"] = NormalizedSeverity.Low,
            ["2"] = NormalizedSeverity.Medium,
            ["3"] = NormalizedSeverity.High,
            ["informational"] = NormalizedSeverity.None,
            ["low"] = NormalizedSeverity.Low,
            ["medium"] = NormalizedSeverity.Medium,
            ["high"] = NormalizedSeverity.High
        });

    public bool CanHandle(Stream sample) => ImporterHelpers.Sniff(sample, "\"site\"", "\"alerts\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        var severities = Severities.WithOverrides(ctx.Options);
        var maxInstances = ReadMaxInstances(ctx);

        var result = new ImportResult
        {
            DetectedTool = "zap",
            DetectedToolVersion = ImporterHelpers.Text(root, "@version"),
            // A ZAP scan covers the URLs it crawled, not the whole application; nothing here
            // licenses auto-closing findings the crawl simply did not reach this time.
            IsFullScan = false,
            ScanDate = ImporterHelpers.Date(root, "@generated")
        };

        var sites = ImporterHelpers.Array(root, "site").ToList();
        if (sites.Count == 0)
            throw new InvalidDataException("Not a ZAP report: no 'site' array.");

        var siteIndex = -1;
        foreach (var site in sites)
        {
            siteIndex++;
            var host = new NormalizedHost
            {
                HostName = ImporterHelpers.Text(site, "@host"),
                Port = ImporterHelpers.Text(site, "@port"),
                Protocol = ImporterHelpers.Text(site, "@ssl") == "true" ? "https" : "http",
                ServiceName = ImporterHelpers.Text(site, "@ssl") == "true" ? "https" : "http"
            };

            var alertIndex = -1;
            foreach (var alert in ImporterHelpers.Array(site, "alerts"))
            {
                ct.ThrowIfCancellationRequested();
                alertIndex++;
                var reference = $"site[{siteIndex}].alerts[{alertIndex}]";

                try
                {
                    var title = ImporterHelpers.Text(alert, "alert", "name");
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        result.AddWarning("Alert has no name.", reference, skipped: true);
                        continue;
                    }

                    var severity = severities.Map(ImporterHelpers.Text(alert, "riskcode"),
                        NormalizedSeverity.Medium);

                    if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                    {
                        result.FilteredCount++;
                        continue;
                    }

                    var instances = ImporterHelpers.Array(alert, "instances").ToList();

                    // An alert with no instances still gets one finding, attributed to the site —
                    // ZAP omits instances for some passive rules and dropping them loses real
                    // findings.
                    var uris = instances
                        .Select(i => ImporterHelpers.Text(i, "uri"))
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (uris.Count == 0) uris.Add(null);

                    if (uris.Count > maxInstances)
                    {
                        result.AddWarning(
                            $"Alert '{title}' reported {uris.Count} affected URLs; imported the first " +
                            $"{maxInstances}. Raise the '{MaxInstancesOption}' option to import all of them.",
                            reference);
                        uris = uris.Take(maxInstances).ToList();
                    }

                    foreach (var uri in uris)
                    {
                        var instance = uri == null
                            ? (JsonElement?)null
                            : instances.FirstOrDefault(i =>
                                string.Equals(ImporterHelpers.Text(i, "uri"), uri, StringComparison.OrdinalIgnoreCase));

                        result.Findings.Add(BuildFinding(alert, instance, host, uri, title!, severity,
                            result.DetectedToolVersion, ctx));
                    }
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not parse alert: {ex.Message}", reference, skipped: true);
                }
            }
        }

        return result;
    }

    private static NormalizedFinding BuildFinding(JsonElement alert, JsonElement? instance, NormalizedHost host,
        string? uri, string title, NormalizedSeverity severity, string? toolVersion, ImportContext ctx)
    {
        var cwe = ImporterHelpers.Text(alert, "cweid");

        var evidenceParts = new List<string>();
        if (instance != null)
        {
            foreach (var field in new[] { "method", "param", "attack", "evidence", "otherinfo" })
                if (ImporterHelpers.Text(instance.Value, field) is { } value)
                    evidenceParts.Add($"{field}: {value}");
        }

        var finding = new NormalizedFinding
        {
            Tool = "zap",
            ToolVersion = toolVersion,
            // alertRef distinguishes sub-variants of the same plugin (10038-1 vs 10038-2); it is
            // the rule identity ZAP itself considers stable, so prefer it over the bare plugin id.
            RuleId = ImporterHelpers.Text(alert, "alertRef") ?? ImporterHelpers.Text(alert, "pluginid"),
            Title = ImporterHelpers.Clip(title, 250)!,
            Description = StripHtml(ImporterHelpers.Text(alert, "desc")),
            Solution = StripHtml(ImporterHelpers.Text(alert, "solution")),
            Severity = severity,
            RawSeverity = ImporterHelpers.Text(alert, "riskdesc") ?? ImporterHelpers.Text(alert, "riskcode"),
            Host = host.IsEmpty ? null : host,
            Location = uri,
            Evidence = evidenceParts.Count == 0 ? null : string.Join("\n", evidenceParts),
            FirstSeen = ctx.ImportedAt,
            LastSeen = ctx.ImportedAt
        };

        if (!string.IsNullOrWhiteSpace(cwe) && cwe != "-1") finding.Cwes.Add($"CWE-{cwe}");

        var references = StripHtml(ImporterHelpers.Text(alert, "reference"));
        if (!string.IsNullOrWhiteSpace(references))
            finding.References.AddRange(references.Split('\n', StringSplitOptions.RemoveEmptyEntries |
                                                             StringSplitOptions.TrimEntries));

        foreach (var cve in ImporterHelpers.ExtractCves($"{title} {references}").Distinct())
            finding.Cves.Add(cve);

        return finding;
    }

    private static int ReadMaxInstances(ImportContext ctx)
    {
        if (ctx.Options.TryGetValue(MaxInstancesOption, out var raw) &&
            int.TryParse(raw, out var parsed) && parsed > 0)
            return parsed;

        return DefaultMaxInstancesPerAlert;
    }

    /// <summary>
    /// ZAP writes its descriptions as HTML fragments. Stored as-is they render as markup in every
    /// grid and export, so the tags are reduced to line breaks and text.
    /// </summary>
    internal static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        var withBreaks = System.Text.RegularExpressions.Regex.Replace(html,
            "<\\s*(br|/p|/div|/li)\\s*/?\\s*>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var stripped = System.Text.RegularExpressions.Regex.Replace(withBreaks, "<[^>]+>", string.Empty);

        return System.Net.WebUtility.HtmlDecode(stripped)
            .Replace("\r\n", "\n")
            .Trim();
    }
}
