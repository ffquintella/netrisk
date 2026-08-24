using System.Globalization;
using System.Xml;
using System.Xml.Serialization;
using Contracts.Importers;
using nessus_tools;
// System.Xml.Serialization also defines an ImportContext; alias the contract's so the file reads
// unambiguously without dropping the XmlSerializer import.
using ImportContext = Contracts.Importers.ImportContext;

namespace ServerServices.Importers;

/// <summary>
/// Tenable Nessus <c>.nessus</c> (NessusClientData_v2) importer, on the extensible contract.
///
/// This replaces the built-in Nessus path that walked the report and wrote to the database as it
/// went. Parsing and persistence are now separate: everything host/service/dedup related moved to
/// the ingestion pipeline, and what is left here is a pure translation from Tenable's XML to
/// <see cref="NormalizedFinding"/>. The parity gate the spec asks for is
/// <c>NessusReportImporterTest</c> plus <c>FindingIngestionServiceTest</c>, which together cover
/// every field the old path wrote.
/// </summary>
public class NessusReportImporter : IVulnerabilityReportImporter
{
    public string Name => "nessus";
    public string DisplayName => "Tenable Nessus";
    public string Version => "2.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".nessus", ".xml"];
    public IReadOnlyList<string> SupportedMimeTypes => ["text/xml", "application/xml"];

    /// <summary>
    /// Nessus reports severity as 0-4 in <c>severity</c> and as a word in <c>risk_factor</c>. The
    /// numeric scale is authoritative — it is what the Tenable UI sorts on — and the word is the
    /// fallback for the occasional item that omits it.
    /// </summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = NormalizedSeverity.None,
            ["1"] = NormalizedSeverity.Low,
            ["2"] = NormalizedSeverity.Medium,
            ["3"] = NormalizedSeverity.High,
            ["4"] = NormalizedSeverity.Critical,
            ["none"] = NormalizedSeverity.None,
            ["low"] = NormalizedSeverity.Low,
            ["medium"] = NormalizedSeverity.Medium,
            ["high"] = NormalizedSeverity.High,
            ["critical"] = NormalizedSeverity.Critical
        });

    public bool CanHandle(Stream sample) => ImporterHelpers.Sniff(sample, "NessusClientData_v2");

    public Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        var severities = Severities.WithOverrides(ctx.Options);

        // A .nessus file is a complete picture of everything the scan touched, so it is the one
        // format that may legitimately drive auto-close of findings it no longer reports.
        var result = new ImportResult { IsFullScan = true };

        NessusClientData_v2? data;
        var serializer = new XmlSerializer(typeof(NessusClientData_v2));

        // DTD processing off and no external resolver: a scan file is untrusted input, and XML
        // parsers that resolve entities are how an XXE turns a report upload into a file read.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using (var xmlReader = XmlReader.Create(report, settings))
        {
            data = (NessusClientData_v2?)serializer.Deserialize(xmlReader);
        }

        if (data?.Report?.ReportHosts == null)
            throw new InvalidDataException("Not a parseable Nessus report: no Report/ReportHost elements.");

        result.DetectedTool = "nessus";

        var hosts = new List<ReportHost>(data.Report.ReportHosts.Cast<ReportHost>());

        foreach (var host in hosts)
        {
            ct.ThrowIfCancellationRequested();

            var normalizedHost = BuildHost(host);
            var scanEnd = HostTag(host, "HOST_END_TIMESTAMP", "HOST_END");
            if (scanEnd != null && result.ScanDate == null) result.ScanDate = scanEnd;

            var itemIndex = -1;
            foreach (var item in host.ReportItems)
            {
                itemIndex++;
                var reference = $"{host.Name}/reportItem[{itemIndex}]";

                try
                {
                    var severity = severities.Map(item.Severity,
                        severities.Map(item.RiskFactor, NormalizedSeverity.None));

                    if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                    {
                        result.FilteredCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.PluginName))
                    {
                        result.AddWarning("Report item has no plugin name.", reference, skipped: true);
                        continue;
                    }

                    var finding = new NormalizedFinding
                    {
                        Tool = "nessus",
                        ToolUniqueId = null, // Nessus has no per-instance GUID; identity is plugin + asset + service.
                        RuleId = string.IsNullOrWhiteSpace(item.PluginId) ? null : item.PluginId,
                        Title = ImporterHelpers.Clip(item.PluginName, 250)!,
                        Description = ImporterHelpers.Clip(item.Description, 65500),
                        Solution = item.Solution,
                        Severity = severity,
                        RawSeverity = item.Severity,
                        Evidence = item.PluginOutput,
                        Host = CloneWithService(normalizedHost, item),
                        Location = item.Port > 0 ? $"{item.Protocol}/{item.Port}" : null,
                        FirstSeen = ctx.ImportedAt,
                        LastSeen = ctx.ImportedAt,
                        CvssVector = item.CVSSVector,
                        CvssBaseScore = item.CVSSBaseScore,
                        Cvss3Vector = item.CVSS3Vector,
                        Cvss3BaseScore = item.CVSS3BaseScore,
                        Cvss3TemporalScore = item.CVSS3TemporalScore,
                        Cvss3ImpactScore = item.CVSS3ImpactScore,
                        VprScore = item.VPRScore,
                        ExploitAvailable = item.ExploitAvailable,
                        ExploitCodeMaturity = item.ExploitCodeMaturity,
                        ExploitabilityEasy = item.ExploitabilityEasy,
                        ExploitedByScanner = item.ExploitedByNessus,
                        ThreatIntensity = item.ThreatIntensityLast28,
                        ThreatRecency = item.ThreatRecency,
                        ThreatSources = item.ThreatSourcesLast28,
                        VulnerabilityPublicationDate = ParseTenableDate(item.VulnerabilityPublicationDate),
                        PatchPublicationDate = ParseTenableDate(item.PatchPublicationDate)
                    };

                    // Part of the legacy import hash, and nothing else in the normalized model
                    // carries it — see NormalizedFinding.ToolFields.
                    if (!string.IsNullOrWhiteSpace(item.RiskFactor))
                        finding.ToolFields["riskFactor"] = item.RiskFactor;

                    foreach (var cve in item.CVEs.Where(c => !string.IsNullOrWhiteSpace(c)))
                        finding.Cves.Add(cve.Trim().ToUpperInvariant());

                    foreach (var xref in item.Xref.Concat(item.IAVA).Concat(item.Msft).Concat(item.Mskb)
                                 .Where(x => !string.IsNullOrWhiteSpace(x)))
                        finding.References.Add(xref.Trim());

                    if (!string.IsNullOrWhiteSpace(item.SeeAlso)) finding.References.Add(item.SeeAlso.Trim());

                    result.Findings.Add(finding);
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not parse report item: {ex.Message}", reference, skipped: true);
                }
            }
        }

        return Task.FromResult(result);
    }

    private static NormalizedHost BuildHost(ReportHost host)
    {
        var properties = string.Join("\n",
            host.HostProperties?.Tags?.Select(t => $"{t.Name}:{t.Value}") ?? []);

        return new NormalizedHost
        {
            Ip = host.IpAddress,
            HostName = host.Name,
            Fqdn = host.FQDN,
            MacAddress = ImporterHelpers.Clip(host.MacAddress, 254),
            OperatingSystem = host.OS,
            // The host-properties blob is stored in a TEXT column; a Nessus host with a long tag
            // list overruns it, and the truncation has to happen here rather than at insert time.
            Properties = ImporterHelpers.Clip(properties, 65000)
        };
    }

    /// <summary>
    /// Each report item sits on its own port/service, so the shared host record is copied per item
    /// rather than mutated — mutating it would leave every finding pointing at the last item's port.
    /// </summary>
    private static NormalizedHost CloneWithService(NormalizedHost host, ReportItem item) => new()
    {
        Ip = host.Ip,
        HostName = host.HostName,
        Fqdn = host.Fqdn,
        MacAddress = host.MacAddress,
        OperatingSystem = host.OperatingSystem,
        Properties = host.Properties,
        ServiceName = item.ServiceName,
        Port = item.Port.ToString(CultureInfo.InvariantCulture),
        Protocol = item.Protocol
    };

    /// <summary>Tenable writes dates as <c>yyyy/MM/dd</c>, which no standard parser accepts by default.</summary>
    private static DateTime? ParseTenableDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTime.TryParseExact(raw, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
            return exact;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose)
            ? loose
            : null;
    }

    private static DateTime? HostTag(ReportHost host, params string[] names)
    {
        var tags = host.HostProperties?.Tags;
        if (tags == null) return null;

        foreach (var name in names)
        {
            var tag = tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag?.Value == null) continue;

            if (long.TryParse(tag.Value, out var unix))
                return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

            if (DateTime.TryParse(tag.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        }

        return null;
    }
}
