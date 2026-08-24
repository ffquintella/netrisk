using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// OpenVAS / Greenbone (GVM) XML report importer.
///
/// GVM reports are read with LINQ to XML rather than a typed deserializer: the schema differs
/// between GMP versions in ways that break strict binding, and most of what matters (CVSS vector,
/// summary, solution) is packed into a single pipe-delimited <c>&lt;tags&gt;</c> string that needs
/// parsing anyway.
/// </summary>
public class OpenVasImporter : IVulnerabilityReportImporter
{
    public string Name => "openvas";
    public string DisplayName => "OpenVAS / Greenbone";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".xml"];
    public IReadOnlyList<string> SupportedMimeTypes => ["text/xml", "application/xml"];

    /// <summary>
    /// GVM's <c>threat</c> vocabulary. "Log" is its informational level and "Debug"/"False
    /// Positive" are diagnostics — none of the three is a finding.
    /// </summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = NormalizedSeverity.Critical,
            ["high"] = NormalizedSeverity.High,
            ["medium"] = NormalizedSeverity.Medium,
            ["low"] = NormalizedSeverity.Low,
            ["log"] = NormalizedSeverity.None,
            ["debug"] = NormalizedSeverity.None,
            ["false positive"] = NormalizedSeverity.None
        });

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "<report", "<nvt") ||
        ImporterHelpers.Sniff(sample, "get_reports_response");

    public Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        var severities = Severities.WithOverrides(ctx.Options);

        // Untrusted XML: no DTDs, no external entity resolution.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument doc;
        using (var reader = XmlReader.Create(report, settings))
        {
            doc = XDocument.Load(reader);
        }

        var results = doc.Descendants("result").ToList();
        if (results.Count == 0 && doc.Descendants("nvt").Any() == false)
            throw new InvalidDataException("Not an OpenVAS report: no <result> elements.");

        var result = new ImportResult
        {
            DetectedTool = "openvas",
            // A GVM scan of a target list enumerates that target list exhaustively.
            IsFullScan = true,
            ScanDate = ParseDate(doc.Descendants("scan_end").FirstOrDefault()?.Value)
        };

        var index = -1;
        foreach (var element in results)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"result[{index}]";

            try
            {
                var nvt = element.Element("nvt");
                var name = element.Element("name")?.Value ?? nvt?.Element("name")?.Value;

                if (string.IsNullOrWhiteSpace(name))
                {
                    result.AddWarning("Result has no name.", reference, skipped: true);
                    continue;
                }

                var threat = element.Element("threat")?.Value;
                var cvssBase = ParseDouble(nvt?.Element("cvss_base")?.Value) ??
                               ParseDouble(element.Element("severity")?.Value);

                var severity = severities.Map(threat, SeverityMapper.FromCvssScore(cvssBase));

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var tags = ParseTags(nvt?.Element("tags")?.Value);

                var hostElement = element.Element("host");
                // The <host> element mixes its IP as text with child elements; Nodes() rather than
                // Value is what separates the address from the hostname child.
                var ip = hostElement?.Nodes().OfType<XText>().FirstOrDefault()?.Value.Trim();
                var hostname = hostElement?.Element("hostname")?.Value;

                var portRaw = element.Element("port")?.Value;
                var (port, protocol) = SplitPort(portRaw);

                var finding = new NormalizedFinding
                {
                    Tool = "openvas",
                    // The NVT OID is Greenbone's stable identifier for the check.
                    RuleId = nvt?.Attribute("oid")?.Value,
                    Title = ImporterHelpers.Clip(name, 250)!,
                    Description = ImporterHelpers.Clip(
                        Join(tags.GetValueOrDefault("summary"),
                            tags.GetValueOrDefault("insight"),
                            tags.GetValueOrDefault("impact"),
                            tags.GetValueOrDefault("affected")),
                        65500),
                    Solution = tags.GetValueOrDefault("solution") ?? nvt?.Element("solution")?.Value,
                    Severity = severity,
                    RawSeverity = threat ?? cvssBase?.ToString(CultureInfo.InvariantCulture),
                    CvssVector = tags.GetValueOrDefault("cvss_base_vector"),
                    CvssBaseScore = cvssBase,
                    Cvss3Vector = Cvss3Vector(nvt),
                    Cvss3BaseScore = Cvss3Score(nvt),
                    Host = new NormalizedHost
                    {
                        Ip = ip,
                        HostName = hostname ?? ip,
                        Fqdn = hostname,
                        Port = port,
                        Protocol = protocol,
                        ServiceName = ServiceName(portRaw)
                    },
                    Location = portRaw,
                    // GVM's <description> is the scanner's evidence for this specific host, not a
                    // description of the vulnerability class — that lives in the NVT tags above.
                    Evidence = element.Element("description")?.Value,
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                };

                foreach (var refElement in nvt?.Element("refs")?.Elements("ref") ?? [])
                {
                    var type = refElement.Attribute("type")?.Value;
                    var id = refElement.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    if (string.Equals(type, "cve", StringComparison.OrdinalIgnoreCase))
                        finding.Cves.Add(id.Trim().ToUpperInvariant());
                    else
                        finding.References.Add(id.Trim());
                }

                // Older GVM versions put CVEs in a <cve> element instead of <refs>.
                foreach (var cve in ImporterHelpers.ExtractCves(nvt?.Element("cve")?.Value))
                    finding.Cves.Add(cve);

                finding.Cves = finding.Cves.Distinct().ToList();

                // A low Quality-of-Detection means GVM itself is unsure; it is the single most
                // useful triage signal in a GVM report and would otherwise be lost.
                var qod = element.Element("qod")?.Element("value")?.Value;
                if (!string.IsNullOrWhiteSpace(qod))
                    finding.Evidence = $"Quality of detection: {qod}%\n\n{finding.Evidence}".Trim();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse result: {ex.Message}", reference, skipped: true);
            }
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// GVM packs NVT metadata into one string as <c>key=value|key=value</c>. Values may contain
    /// '=' (a CVSS vector does), so only the first separator is split on.
    /// </summary>
    internal static Dictionary<string, string> ParseTags(string? tags)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tags)) return parsed;

        foreach (var pair in tags.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            var key = pair.Substring(0, separator).Trim();
            var value = pair.Substring(separator + 1).Trim();
            if (key.Length > 0 && value.Length > 0) parsed[key] = value;
        }

        return parsed;
    }

    private static string? Cvss3Vector(XElement? nvt) =>
        nvt?.Element("severities")?.Elements("severity")
            .FirstOrDefault(s => s.Attribute("type")?.Value?.Contains("v3", StringComparison.OrdinalIgnoreCase) == true)
            ?.Element("value")?.Value;

    private static double? Cvss3Score(XElement? nvt) =>
        ParseDouble(nvt?.Element("severities")?.Elements("severity")
            .FirstOrDefault(s => s.Attribute("type")?.Value?.Contains("v3", StringComparison.OrdinalIgnoreCase) == true)
            ?.Element("score")?.Value);

    /// <summary>GVM writes ports as <c>443/tcp</c> or <c>https (443/tcp)</c>.</summary>
    private static (string? Port, string? Protocol) SplitPort(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var inner = raw;
        var open = raw.IndexOf('(');
        var close = raw.IndexOf(')');
        if (open >= 0 && close > open) inner = raw.Substring(open + 1, close - open - 1);

        var parts = inner.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (null, null);

        return int.TryParse(parts[0], out _) ? (parts[0], parts[1]) : (null, parts[1]);
    }

    private static string? ServiceName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var open = raw.IndexOf('(');
        return open > 0 ? raw.Substring(0, open).Trim() : null;
    }

    private static double? ParseDouble(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTime? ParseDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        return kept.Count == 0 ? null : string.Join("\n\n", kept);
    }
}
