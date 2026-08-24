using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// GitHub Dependabot alerts importer.
///
/// Consumes the JSON body of <c>GET /repos/{owner}/{repo}/dependabot/alerts</c> — the only way to
/// get Dependabot's findings out of GitHub, since it has no report file of its own. A SARIF export
/// (from GitHub's code-scanning API) is accepted too and delegated to <see cref="SarifImporter"/>.
/// </summary>
public class DependabotImporter : IVulnerabilityReportImporter
{
    public string Name => "dependabot";
    public string DisplayName => "GitHub Dependabot";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json", ".sarif"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json", "application/sarif+json"];

    private static readonly SeverityMapper Severities = new(SeverityMapper.CvssWords);

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "\"security_advisory\"") ||
        ImporterHelpers.Sniff(sample, "\"security_vulnerability\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("runs", out _))
            return SarifImporter.Parse(root, ctx, toolOverride: "dependabot", ct);

        var severities = Severities.WithOverrides(ctx.Options);
        var result = new ImportResult
        {
            DetectedTool = "dependabot",
            // The alerts endpoint returns the repository's whole open set (subject to paging, which
            // the caller is responsible for), so it is exhaustive for that repository.
            IsFullScan = true
        };

        // The endpoint returns a bare array. A single alert object is accepted too, since that is
        // what the per-alert endpoint returns and pasting one into the import is a natural thing
        // for someone to do.
        var alerts = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            JsonValueKind.Object when root.TryGetProperty("security_advisory", out _) => [root],
            _ => throw new InvalidDataException(
                "Not a Dependabot alerts payload: expected an array of alerts.")
        };

        var index = -1;
        foreach (var alert in alerts)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"[{index}]";

            try
            {
                var state = ImporterHelpers.Text(alert, "state");

                // Dismissed and fixed alerts are history, not open work. Importing them would
                // re-open a finding a maintainer already closed on GitHub's side.
                if (state != null && !string.Equals(state, "open", StringComparison.OrdinalIgnoreCase))
                {
                    result.FilteredCount++;
                    continue;
                }

                var advisory = ImporterHelpers.Child(alert, "security_advisory");
                if (advisory == null)
                {
                    result.AddWarning("Alert has no 'security_advisory' block.", reference, skipped: true);
                    continue;
                }

                var vulnerability = ImporterHelpers.Child(alert, "security_vulnerability");
                var dependency = ImporterHelpers.Child(alert, "dependency");

                var package = dependency != null && ImporterHelpers.Child(dependency.Value, "package") is { } depPkg
                    ? ImporterHelpers.Text(depPkg, "name")
                    : null;

                if (package == null && vulnerability != null &&
                    ImporterHelpers.Child(vulnerability.Value, "package") is { } vulnPkg)
                    package = ImporterHelpers.Text(vulnPkg, "name");

                var summary = ImporterHelpers.Text(advisory.Value, "summary");
                var ghsa = ImporterHelpers.Text(advisory.Value, "ghsa_id");
                var cve = ImporterHelpers.Text(advisory.Value, "cve_id");

                var title = summary != null && package != null ? $"{package}: {summary}" : summary ?? ghsa ?? cve;
                if (string.IsNullOrWhiteSpace(title))
                {
                    result.AddWarning("Alert advisory has no summary, GHSA id, or CVE id.", reference, skipped: true);
                    continue;
                }

                var cvss = ImporterHelpers.Child(advisory.Value, "cvss");
                var score = cvss != null ? ImporterHelpers.Number(cvss.Value, "score") : null;

                var severity = severities.Map(
                    ImporterHelpers.Text(advisory.Value, "severity") ??
                    (vulnerability != null ? ImporterHelpers.Text(vulnerability.Value, "severity") : null),
                    SeverityMapper.FromCvssScore(score));

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var manifest = dependency != null ? ImporterHelpers.Text(dependency.Value, "manifest_path") : null;
                var patched = vulnerability != null &&
                              ImporterHelpers.Child(vulnerability.Value, "first_patched_version") is { } fp
                    ? ImporterHelpers.Text(fp, "identifier")
                    : null;

                var alertNumber = ImporterHelpers.Number(alert, "number");

                var finding = new NormalizedFinding
                {
                    Tool = "dependabot",
                    // The alert number is GitHub's own stable per-repository identity, and the
                    // strongest dedup key available for this source.
                    ToolUniqueId = alertNumber == null ? null : $"{ghsa ?? "alert"}#{(long)alertNumber.Value}",
                    RuleId = ghsa ?? cve,
                    Title = ImporterHelpers.Clip(title, 250)!,
                    Description = ImporterHelpers.Clip(ImporterHelpers.Text(advisory.Value, "description"), 65500),
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(advisory.Value, "severity"),
                    Component = package,
                    ComponentVersion = vulnerability != null
                        ? ImporterHelpers.Text(vulnerability.Value, "vulnerable_version_range")
                        : null,
                    FixedInVersion = patched,
                    Location = manifest == null ? package : (package == null ? manifest : $"{manifest}#{package}"),
                    Cvss3Vector = cvss != null ? ImporterHelpers.Text(cvss.Value, "vector_string") : null,
                    Cvss3BaseScore = score,
                    CvssBaseScore = score,
                    FirstSeen = ImporterHelpers.Date(alert, "created_at") ?? ctx.ImportedAt,
                    LastSeen = ImporterHelpers.Date(alert, "updated_at") ?? ctx.ImportedAt,
                    VulnerabilityPublicationDate = ImporterHelpers.Date(advisory.Value, "published_at"),
                    RawStatus = state,
                    Solution = patched == null
                        ? null
                        : $"Upgrade {package ?? "the dependency"} to {patched} or later."
                };

                if (!string.IsNullOrWhiteSpace(cve)) finding.Cves.Add(cve.ToUpperInvariant());

                foreach (var identifier in ImporterHelpers.Array(advisory.Value, "identifiers"))
                foreach (var extracted in ImporterHelpers.ExtractCves(ImporterHelpers.Text(identifier, "value")))
                    finding.Cves.Add(extracted);

                foreach (var cwe in ImporterHelpers.Array(advisory.Value, "cwes")
                             .Select(c => ImporterHelpers.Text(c, "cwe_id"))
                             .Where(c => !string.IsNullOrWhiteSpace(c)))
                    finding.Cwes.Add(cwe!.ToUpperInvariant());

                foreach (var r in ImporterHelpers.Array(advisory.Value, "references")
                             .Select(r => ImporterHelpers.Text(r, "url"))
                             .Where(r => !string.IsNullOrWhiteSpace(r)))
                    finding.References.Add(r!);

                if (ImporterHelpers.Text(alert, "html_url") is { } htmlUrl) finding.References.Add(htmlUrl);

                finding.Cves = finding.Cves.Distinct().ToList();
                finding.Cwes = finding.Cwes.Distinct().ToList();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse alert: {ex.Message}", reference, skipped: true);
            }
        }

        return result;
    }
}
