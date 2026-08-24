using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Anchore Grype JSON importer (<c>grype -o json</c>).
///
/// Grype reports one "match" per (vulnerability, artifact) pair, which is already the granularity
/// NetRisk wants — the same CVE in two packages is two pieces of work.
/// </summary>
public class GrypeImporter : IVulnerabilityReportImporter
{
    public string Name => "grype";
    public string DisplayName => "Anchore Grype";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json"];

    private static readonly SeverityMapper Severities = new(SeverityMapper.CvssWords);

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "\"matches\"", "\"artifact\"") ||
        ImporterHelpers.Sniff(sample, "\"matches\"", "\"grype\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        var severities = Severities.WithOverrides(ctx.Options);

        var descriptor = ImporterHelpers.Child(root, "descriptor");
        var result = new ImportResult
        {
            DetectedTool = "grype",
            DetectedToolVersion = descriptor != null ? ImporterHelpers.Text(descriptor.Value, "version") : null,
            IsFullScan = true
        };

        var target = SourceTarget(root);

        if (ImporterHelpers.Child(root, "matches") is not { ValueKind: JsonValueKind.Array } matches)
            throw new InvalidDataException("Not a Grype report: no 'matches' array.");

        var index = -1;
        foreach (var match in matches.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"matches[{index}]";

            try
            {
                var vuln = ImporterHelpers.Child(match, "vulnerability");
                if (vuln == null)
                {
                    result.AddWarning("Match has no 'vulnerability' block.", reference, skipped: true);
                    continue;
                }

                var artifact = ImporterHelpers.Child(match, "artifact");
                var id = ImporterHelpers.Text(vuln.Value, "id");
                var pkg = artifact != null ? ImporterHelpers.Text(artifact.Value, "name") : null;

                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddWarning("Match has no vulnerability id.", reference, skipped: true);
                    continue;
                }

                var (vector, score, isV3) = BestCvss(vuln.Value);
                var severity = severities.Map(ImporterHelpers.Text(vuln.Value, "severity"),
                    SeverityMapper.FromCvssScore(score));

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var fix = ImporterHelpers.Child(vuln.Value, "fix");
                var fixVersions = fix != null
                    ? ImporterHelpers.Array(fix.Value, "versions").Select(v => v.GetString())
                        .Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                    : [];

                var finding = new NormalizedFinding
                {
                    Tool = "grype",
                    ToolVersion = result.DetectedToolVersion,
                    RuleId = id,
                    Title = ImporterHelpers.Clip(pkg == null ? id : $"{pkg}: {id}", 250)!,
                    Description = ImporterHelpers.Clip(ImporterHelpers.Text(vuln.Value, "description"), 65500),
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(vuln.Value, "severity"),
                    Component = pkg,
                    ComponentVersion = artifact != null ? ImporterHelpers.Text(artifact.Value, "version") : null,
                    FixedInVersion = fixVersions.Count == 0 ? null : string.Join(", ", fixVersions),
                    Location = Location(target, artifact, pkg),
                    CvssVector = isV3 ? null : vector,
                    Cvss3Vector = isV3 ? vector : null,
                    Cvss3BaseScore = isV3 ? score : null,
                    CvssBaseScore = score,
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt,
                    Solution = fixVersions.Count == 0
                        ? null
                        : $"Upgrade {pkg ?? "the package"} to {fixVersions[0]} or later.",
                    // The purl pins exactly which build of the package matched, which is what a
                    // maintainer needs to reproduce the match.
                    Evidence = artifact != null ? ImporterHelpers.Text(artifact.Value, "purl") : null
                };

                foreach (var cve in ImporterHelpers.ExtractCves(id)) finding.Cves.Add(cve);

                // Grype often matches on a GHSA and lists the CVE as a related vulnerability; the
                // CVE is what everything else in the register keys on.
                foreach (var related in ImporterHelpers.Array(vuln.Value, "relatedVulnerabilities")
                             .Concat(ImporterHelpers.Array(match, "relatedVulnerabilities")))
                foreach (var cve in ImporterHelpers.ExtractCves(ImporterHelpers.Text(related, "id")))
                    finding.Cves.Add(cve);

                foreach (var url in ImporterHelpers.Array(vuln.Value, "urls")
                             .Select(u => u.GetString())
                             .Where(u => !string.IsNullOrWhiteSpace(u)))
                    finding.References.Add(url!);

                if (ImporterHelpers.Text(vuln.Value, "dataSource") is { } dataSource)
                    finding.References.Add(dataSource);

                finding.Cves = finding.Cves.Distinct().ToList();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse match: {ex.Message}", reference, skipped: true);
            }
        }

        return result;
    }

    /// <summary>
    /// Grype lists every CVSS record it has. v3 is preferred over v2 because it is the scale the
    /// rest of NetRisk stores and reports on.
    /// </summary>
    private static (string? Vector, double? Score, bool IsV3) BestCvss(JsonElement vuln)
    {
        string? fallbackVector = null;
        double? fallbackScore = null;

        foreach (var cvss in ImporterHelpers.Array(vuln, "cvss"))
        {
            var version = ImporterHelpers.Text(cvss, "version");
            var vector = ImporterHelpers.Text(cvss, "vector");
            var metrics = ImporterHelpers.Child(cvss, "metrics");
            var score = metrics != null ? ImporterHelpers.Number(metrics.Value, "baseScore") : null;

            if (version != null && version.StartsWith("3", StringComparison.Ordinal))
                return (vector, score, true);

            fallbackVector ??= vector;
            fallbackScore ??= score;
        }

        return (fallbackVector, fallbackScore, false);
    }

    private static string? SourceTarget(JsonElement root)
    {
        if (ImporterHelpers.Child(root, "source") is not { } source) return null;

        if (ImporterHelpers.Child(source, "target") is { } target)
        {
            if (target.ValueKind == JsonValueKind.String) return target.GetString();
            return ImporterHelpers.Text(target, "userInput", "name", "path");
        }

        return null;
    }

    private static string? Location(string? target, JsonElement? artifact, string? pkg)
    {
        var path = artifact != null
            ? ImporterHelpers.Array(artifact.Value, "locations")
                .Select(l => ImporterHelpers.Text(l, "path"))
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            : null;

        var basePart = target ?? path;
        if (string.IsNullOrWhiteSpace(basePart)) return pkg;
        return string.IsNullOrWhiteSpace(pkg) ? basePart : $"{basePart}#{pkg}";
    }
}
