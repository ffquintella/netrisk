using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Semgrep importer, accepting both its native JSON (<c>semgrep --json</c>) and its SARIF output.
///
/// The native format is preferred where available because it carries Semgrep's own
/// <c>fingerprint</c> — a stable per-finding identity that survives line drift — plus the
/// impact/likelihood metadata its SARIF output flattens away. When handed SARIF, this delegates to
/// <see cref="SarifImporter"/> rather than reimplementing it.
/// </summary>
public class SemgrepImporter : IVulnerabilityReportImporter
{
    public string Name => "semgrep";
    public string DisplayName => "Semgrep";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json", ".sarif"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json", "application/sarif+json"];

    /// <summary>
    /// Semgrep's three-level scale. WARNING maps to Medium rather than Low: its security rules at
    /// WARNING are ordinary injection and crypto findings, and grading them Low buries them.
    /// </summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["error"] = NormalizedSeverity.High,
            ["warning"] = NormalizedSeverity.Medium,
            ["info"] = NormalizedSeverity.Low,
            ["critical"] = NormalizedSeverity.Critical,
            ["high"] = NormalizedSeverity.High,
            ["medium"] = NormalizedSeverity.Medium,
            ["low"] = NormalizedSeverity.Low
        });

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "\"check_id\"") ||
        ImporterHelpers.Sniff(sample, "\"results\"", "\"semgrep\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        // SARIF and Semgrep-native both have a top-level array, under different names. Detect on
        // the SARIF-only "runs" key and hand off; anything else is treated as native.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("runs", out _))
            return SarifImporter.Parse(root, ctx, toolOverride: "semgrep", ct);

        var severities = Severities.WithOverrides(ctx.Options);

        var result = new ImportResult
        {
            DetectedTool = "semgrep",
            DetectedToolVersion = ImporterHelpers.Text(root, "version"),
            // Semgrep scans the paths it was pointed at; that is not the whole codebase.
            IsFullScan = false
        };

        if (ImporterHelpers.Child(root, "results") is not { ValueKind: JsonValueKind.Array } results)
            throw new InvalidDataException("Not a Semgrep report: no 'results' array.");

        // Semgrep reports its own parse failures in "errors". They are the reason a rule found
        // nothing in a file, so they belong in the warning list rather than being dropped.
        foreach (var error in ImporterHelpers.Array(root, "errors"))
        {
            var message = ImporterHelpers.Text(error, "message", "long_msg", "short_msg");
            var path = ImporterHelpers.Text(error, "path");
            if (message != null) result.AddWarning($"Semgrep reported an error: {message}", path);
        }

        var index = -1;
        foreach (var item in results.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var reference = $"results[{index}]";

            try
            {
                var extra = ImporterHelpers.Child(item, "extra");
                if (extra == null)
                {
                    result.AddWarning("Result has no 'extra' block.", reference, skipped: true);
                    continue;
                }

                // A rule the developer suppressed with a nosemgrep comment must stay suppressed.
                if (ImporterHelpers.Bool(extra.Value, "is_ignored") == true)
                {
                    result.FilteredCount++;
                    continue;
                }

                var checkId = ImporterHelpers.Text(item, "check_id");
                var message = ImporterHelpers.Text(extra.Value, "message");
                var title = message ?? checkId;

                if (string.IsNullOrWhiteSpace(title))
                {
                    result.AddWarning("Result has neither a check id nor a message.", reference, skipped: true);
                    continue;
                }

                var metadata = ImporterHelpers.Child(extra.Value, "metadata");

                var severity = severities.Map(ImporterHelpers.Text(extra.Value, "severity"), NormalizedSeverity.Medium);

                // Semgrep Pro grades findings on impact separately from rule severity. A HIGH
                // impact finding reported at WARNING is a High, and using the higher of the two is
                // what keeps the register's severities comparable with the other scanners'.
                if (metadata != null)
                {
                    var impact = severities.Map(ImporterHelpers.Text(metadata.Value, "impact"), NormalizedSeverity.None);
                    if (impact > severity) severity = impact;
                }

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var path = ImporterHelpers.Text(item, "path");
                var line = ImporterHelpers.Child(item, "start") is { } start
                    ? ImporterHelpers.Number(start, "line")
                    : null;

                var finding = new NormalizedFinding
                {
                    Tool = "semgrep",
                    ToolVersion = result.DetectedToolVersion,
                    ToolUniqueId = ImporterHelpers.Text(extra.Value, "fingerprint"),
                    RuleId = checkId,
                    // The message is the human-readable finding; the check id is the stable
                    // identity. Both matter, so the title carries the message and the rule id is
                    // kept for dedup.
                    Title = ImporterHelpers.Clip(title, 250)!,
                    Description = message,
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(extra.Value, "severity"),
                    Location = path == null ? null : (line is > 0 ? $"{path}:{(int)line.Value}" : path),
                    Evidence = ImporterHelpers.Text(extra.Value, "lines"),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                };

                if (metadata != null)
                {
                    foreach (var cwe in ImporterHelpers.Array(metadata.Value, "cwe")
                                 .Select(c => c.ToString()))
                    foreach (var normalized in ImporterHelpers.ExtractCwes(cwe))
                        finding.Cwes.Add(normalized);

                    foreach (var cve in ImporterHelpers.Array(metadata.Value, "cve").Select(c => c.ToString())
                                 .Concat([ImporterHelpers.Text(metadata.Value, "cve") ?? string.Empty]))
                    foreach (var normalized in ImporterHelpers.ExtractCves(cve))
                        finding.Cves.Add(normalized);

                    foreach (var r in ImporterHelpers.Array(metadata.Value, "references")
                                 .Select(r => r.GetString())
                                 .Where(r => !string.IsNullOrWhiteSpace(r)))
                        finding.References.Add(r!);

                    if (ImporterHelpers.Text(metadata.Value, "shortlink") is { } shortlink)
                        finding.References.Add(shortlink);
                }

                if (ImporterHelpers.Child(extra.Value, "fix") is { } fix && fix.ValueKind == JsonValueKind.String)
                    finding.Solution = $"Semgrep suggests the following replacement:\n\n{fix.GetString()}";

                finding.Cves = finding.Cves.Distinct().ToList();
                finding.Cwes = finding.Cwes.Distinct().ToList();

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse result: {ex.Message}", reference, skipped: true);
            }
        }

        return result;
    }
}
