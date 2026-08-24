using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Snyk Open Source / Container importer (<c>snyk test --json</c>).
///
/// Snyk emits either one project object or an array of them (a monorepo scan with
/// <c>--all-projects</c>); both shapes are accepted. Snyk Code emits SARIF instead, which is
/// handed to <see cref="SarifImporter"/>.
/// </summary>
public class SnykImporter : IVulnerabilityReportImporter
{
    public string Name => "snyk";
    public string DisplayName => "Snyk";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json", ".sarif"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json", "application/sarif+json"];

    private static readonly SeverityMapper Severities = new(SeverityMapper.CvssWords);

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "\"vulnerabilities\"", "\"packageManager\"") ||
        ImporterHelpers.Sniff(sample, "\"vulnerabilities\"", "\"displayTargetFile\"") ||
        ImporterHelpers.Sniff(sample, "\"SNYK-");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("runs", out _))
            return SarifImporter.Parse(root, ctx, toolOverride: "snyk", ct);

        var severities = Severities.WithOverrides(ctx.Options);
        var result = new ImportResult
        {
            DetectedTool = "snyk",
            // A dependency scan enumerates the whole manifest, so it is exhaustive for that project.
            IsFullScan = true
        };

        var projects = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : [root];

        var sawVulnerabilitiesArray = false;

        var projectIndex = -1;
        foreach (var project in projects)
        {
            projectIndex++;
            if (project.ValueKind != JsonValueKind.Object) continue;

            var target = ImporterHelpers.Text(project, "displayTargetFile", "targetFile") ??
                         ImporterHelpers.Text(project, "projectName");

            if (ImporterHelpers.Child(project, "vulnerabilities") is not { ValueKind: JsonValueKind.Array } vulns)
                continue;

            sawVulnerabilitiesArray = true;

            var index = -1;
            foreach (var vuln in vulns.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                index++;
                var reference = $"[{projectIndex}].vulnerabilities[{index}]";

                try
                {
                    var id = ImporterHelpers.Text(vuln, "id");
                    var pkg = ImporterHelpers.Text(vuln, "packageName", "name");
                    var title = ImporterHelpers.Text(vuln, "title");
                    var display = title != null && pkg != null ? $"{pkg}: {title}" : title ?? id;

                    if (string.IsNullOrWhiteSpace(display))
                    {
                        result.AddWarning("Vulnerability has neither an id nor a title.", reference, skipped: true);
                        continue;
                    }

                    var score = ImporterHelpers.Number(vuln, "cvssScore");
                    var severity = severities.Map(ImporterHelpers.Text(vuln, "severity"),
                        SeverityMapper.FromCvssScore(score));

                    if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                    {
                        result.FilteredCount++;
                        continue;
                    }

                    var fixedIn = ImporterHelpers.Array(vuln, "fixedIn")
                        .Select(f => f.GetString())
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .ToList();

                    var finding = new NormalizedFinding
                    {
                        Tool = "snyk",
                        // Snyk's issue id is stable across scans and is exactly what the
                        // UniqueIdFromTool dedup strategy exists for.
                        ToolUniqueId = id,
                        RuleId = id,
                        Title = ImporterHelpers.Clip(display, 250)!,
                        Description = ImporterHelpers.Clip(ImporterHelpers.Text(vuln, "description"), 65500),
                        Severity = severity,
                        RawSeverity = ImporterHelpers.Text(vuln, "severity"),
                        Component = pkg,
                        ComponentVersion = ImporterHelpers.Text(vuln, "version"),
                        FixedInVersion = fixedIn.Count == 0 ? null : string.Join(", ", fixedIn),
                        Location = target == null ? pkg : (pkg == null ? target : $"{target}#{pkg}"),
                        Cvss3Vector = ImporterHelpers.Text(vuln, "CVSSv3"),
                        Cvss3BaseScore = score,
                        CvssBaseScore = score,
                        VulnerabilityPublicationDate = ImporterHelpers.Date(vuln, "publicationTime", "disclosureTime"),
                        FirstSeen = ctx.ImportedAt,
                        LastSeen = ctx.ImportedAt,
                        Solution = BuildRemediation(vuln, pkg, fixedIn),
                        // The dependency chain is what a developer needs to act: a transitive
                        // vulnerability is fixed at the direct dependency that pulls it in.
                        Evidence = DependencyPath(vuln)
                    };

                    if (ImporterHelpers.Child(vuln, "identifiers") is { } identifiers)
                    {
                        foreach (var cve in ImporterHelpers.Array(identifiers, "CVE")
                                     .Select(c => c.GetString()))
                        foreach (var normalized in ImporterHelpers.ExtractCves(cve))
                            finding.Cves.Add(normalized);

                        foreach (var cwe in ImporterHelpers.Array(identifiers, "CWE")
                                     .Select(c => c.GetString()))
                        foreach (var normalized in ImporterHelpers.ExtractCwes(cwe))
                            finding.Cwes.Add(normalized);
                    }

                    foreach (var r in ImporterHelpers.Array(vuln, "references")
                                 .Select(r => r.ValueKind == JsonValueKind.Object
                                     ? ImporterHelpers.Text(r, "url")
                                     : r.GetString())
                                 .Where(r => !string.IsNullOrWhiteSpace(r)))
                        finding.References.Add(r!);

                    finding.Cves = finding.Cves.Distinct().ToList();
                    finding.Cwes = finding.Cwes.Distinct().ToList();

                    result.Findings.Add(finding);
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not parse vulnerability: {ex.Message}", reference, skipped: true);
                }
            }
        }

        // "ok": true with no vulnerabilities array is a clean scan, not a broken file — but a
        // document with neither is not a Snyk report at all.
        if (!sawVulnerabilitiesArray && ImporterHelpers.Bool(root, "ok") == null)
            throw new InvalidDataException("Not a Snyk report: no 'vulnerabilities' array.");

        return result;
    }

    private static string? BuildRemediation(JsonElement vuln, string? pkg, List<string?> fixedIn)
    {
        if (fixedIn.Count > 0)
            return $"Upgrade {pkg ?? "the package"} to {fixedIn[0]} or later.";

        if (ImporterHelpers.Bool(vuln, "isPatchable") == true)
            return "No upgrade is available; Snyk reports a patch is applicable.";

        return null;
    }

    private static string? DependencyPath(JsonElement vuln)
    {
        var from = ImporterHelpers.Array(vuln, "from")
            .Select(f => f.GetString())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        return from.Count == 0 ? null : "Dependency path: " + string.Join(" › ", from);
    }
}
