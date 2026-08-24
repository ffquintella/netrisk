using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Generic SARIF 2.1.0 importer.
///
/// SARIF is the interoperability format the code-scanning world converged on, so this one importer
/// covers dozens of tools that would otherwise each need their own — CodeQL, Semgrep, ESLint,
/// Bandit, Checkov, gitleaks, and anything else with a SARIF exporter. Tool-specific importers
/// exist where a tool's native format carries more than its SARIF output does; where it does not,
/// the tool rides on this.
/// </summary>
public class SarifImporter : IVulnerabilityReportImporter
{
    public string Name => "sarif";
    public string DisplayName => "SARIF 2.1 (generic)";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".sarif", ".json"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/sarif+json", "application/json"];

    /// <summary>
    /// SARIF's own level vocabulary. It has four values and none of them is "critical", so the
    /// mapping tops out at High unless the run carries a <c>security-severity</c> score — which is
    /// what GitHub's code scanning emits and what makes Critical reachable.
    /// </summary>
    private static readonly SeverityMapper Severities = new(
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["error"] = NormalizedSeverity.High,
            ["warning"] = NormalizedSeverity.Medium,
            ["note"] = NormalizedSeverity.Low,
            ["none"] = NormalizedSeverity.None
        });

    public bool CanHandle(Stream sample)
    {
        // "$schema" alone is not enough — every JSON scanner report may carry one. The version
        // string plus the runs array is what makes it SARIF specifically.
        var text = ImporterHelpers.PeekText(sample);
        if (text.Length == 0) return false;
        return text.Contains("\"runs\"", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("\"2.1.0\"", StringComparison.Ordinal) ||
                text.Contains("sarif", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        return Parse(doc.RootElement, ctx, toolOverride: null, ct);
    }

    /// <summary>
    /// Shared with the tool-specific importers that delegate here (Semgrep, Dependabot).
    /// <paramref name="toolOverride"/> forces the recorded tool name so findings imported through a
    /// named importer are attributed to that scanner rather than to whatever the SARIF driver
    /// happened to call itself.
    /// </summary>
    internal static ImportResult Parse(JsonElement root, ImportContext ctx, string? toolOverride,
        CancellationToken ct = default)
    {
        var severities = Severities.WithOverrides(ctx.Options);
        var result = new ImportResult { IsFullScan = false };

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Not a SARIF document: no 'runs' array.");

        var runIndex = -1;
        foreach (var run in runs.EnumerateArray())
        {
            runIndex++;
            var driver = ImporterHelpers.Child(run, "tool") is { } tool
                ? ImporterHelpers.Child(tool, "driver")
                : null;

            var toolName = toolOverride ?? (driver != null ? ImporterHelpers.Text(driver.Value, "name") : null) ?? "sarif";
            var toolVersion = driver != null
                ? ImporterHelpers.Text(driver.Value, "semanticVersion", "version")
                : null;

            result.DetectedTool ??= toolName;
            result.DetectedToolVersion ??= toolVersion;

            // Result objects reference their rule by index or id; the metadata (description, tags,
            // default level) lives only in the driver's rule list, so it has to be indexed first.
            var rulesById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var rulesByIndex = new List<JsonElement>();
            if (driver != null)
                foreach (var rule in ImporterHelpers.Array(driver.Value, "rules"))
                {
                    rulesByIndex.Add(rule);
                    var id = ImporterHelpers.Text(rule, "id");
                    if (id != null) rulesById[id] = rule;
                }

            if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                result.AddWarning("Run carries no 'results' array.", $"runs[{runIndex}]");
                continue;
            }

            var resultIndex = -1;
            foreach (var sarifResult in results.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                resultIndex++;
                var reference = $"runs[{runIndex}].results[{resultIndex}]";

                try
                {
                    var finding = ParseResult(sarifResult, rulesById, rulesByIndex, toolName, toolVersion,
                        severities, ctx, result, reference);

                    if (finding == null) continue;

                    if (ctx.IgnoreNegligible && finding.Severity == NormalizedSeverity.None)
                    {
                        result.FilteredCount++;
                        continue;
                    }

                    result.Findings.Add(finding);
                }
                catch (Exception ex)
                {
                    result.AddWarning($"Could not parse result: {ex.Message}", reference, skipped: true);
                }
            }
        }

        return result;
    }

    private static NormalizedFinding? ParseResult(JsonElement sarifResult,
        IReadOnlyDictionary<string, JsonElement> rulesById, IReadOnlyList<JsonElement> rulesByIndex,
        string toolName, string? toolVersion, SeverityMapper severities, ImportContext ctx,
        ImportResult result, string reference)
    {
        // A suppressed SARIF result is one the tool has been told to ignore. Importing it as an
        // active finding would re-surface exactly what the developer suppressed in code.
        if (sarifResult.TryGetProperty("suppressions", out var suppressions) &&
            suppressions.ValueKind == JsonValueKind.Array && suppressions.GetArrayLength() > 0)
        {
            result.FilteredCount++;
            return null;
        }

        var ruleId = ImporterHelpers.Text(sarifResult, "ruleId");
        JsonElement? rule = null;
        if (ruleId != null && rulesById.TryGetValue(ruleId, out var byId)) rule = byId;
        else if (ImporterHelpers.Number(sarifResult, "ruleIndex") is { } idx && idx >= 0 && idx < rulesByIndex.Count)
        {
            rule = rulesByIndex[(int)idx];
            ruleId ??= ImporterHelpers.Text(rule.Value, "id");
        }

        var message = ImporterHelpers.Child(sarifResult, "message") is { } msg
            ? ImporterHelpers.Text(msg, "text", "markdown")
            : null;

        var shortDescription = rule != null && ImporterHelpers.Child(rule.Value, "shortDescription") is { } sd
            ? ImporterHelpers.Text(sd, "text")
            : null;
        var fullDescription = rule != null && ImporterHelpers.Child(rule.Value, "fullDescription") is { } fd
            ? ImporterHelpers.Text(fd, "text")
            : null;
        var help = rule != null && ImporterHelpers.Child(rule.Value, "help") is { } hp
            ? ImporterHelpers.Text(hp, "text", "markdown")
            : null;

        var title = shortDescription ?? message ?? ruleId;
        if (string.IsNullOrWhiteSpace(title))
        {
            result.AddWarning("Result has neither a rule id, a message, nor a rule description.",
                reference, skipped: true);
            return null;
        }

        var location = FormatLocation(sarifResult);

        // Severity precedence: the result's own level, then the rule's default, then GitHub's
        // security-severity score. The score is checked last but wins when it implies Critical,
        // because SARIF's own vocabulary cannot express that band at all.
        var level = ImporterHelpers.Text(sarifResult, "level");
        if (level == null && rule != null &&
            ImporterHelpers.Child(rule.Value, "defaultConfiguration") is { } config)
            level = ImporterHelpers.Text(config, "level");

        var severity = severities.Map(level, NormalizedSeverity.Medium);

        var securitySeverity = SecuritySeverity(sarifResult) ?? (rule != null ? SecuritySeverity(rule.Value) : null);
        if (securitySeverity is { } score)
        {
            var banded = SeverityMapper.FromCvssScore(score);
            if (banded > severity) severity = banded;
        }

        var tags = rule != null && ImporterHelpers.Child(rule.Value, "properties") is { } ruleProps
            ? string.Join(" ", ImporterHelpers.Array(ruleProps, "tags").Select(t => t.ToString()))
            : null;

        var descriptionParts = new[] { message, fullDescription, help }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();

        var finding = new NormalizedFinding
        {
            Tool = toolName,
            ToolVersion = toolVersion,
            ToolUniqueId = ImporterHelpers.Text(sarifResult, "guid", "correlationGuid"),
            RuleId = ruleId,
            Title = ImporterHelpers.Clip(title, 250)!,
            Description = string.Join("\n\n", descriptionParts),
            Solution = help,
            Severity = severity,
            RawSeverity = level ?? securitySeverity?.ToString(),
            Location = location,
            Evidence = Snippet(sarifResult),
            FirstSeen = ctx.ImportedAt,
            LastSeen = ctx.ImportedAt,
            CvssBaseScore = securitySeverity
        };

        foreach (var cve in ImporterHelpers.ExtractCves($"{ruleId} {title} {tags} {message}").Distinct())
            finding.Cves.Add(cve);
        foreach (var cwe in ImporterHelpers.ExtractCwes($"{ruleId} {tags} {fullDescription}").Distinct())
            finding.Cwes.Add(cwe);

        if (rule != null && ImporterHelpers.Text(rule.Value, "helpUri") is { } helpUri)
            finding.References.Add(helpUri);

        return finding;
    }

    /// <summary>
    /// GitHub's convention for carrying a CVSS-like score through SARIF, as
    /// <c>properties["security-severity"]</c>. Not part of the standard, but universal in practice.
    /// </summary>
    private static double? SecuritySeverity(JsonElement element)
    {
        if (ImporterHelpers.Child(element, "properties") is not { } props) return null;
        return ImporterHelpers.Number(props, "security-severity", "securitySeverity");
    }

    /// <summary>
    /// <c>path:line</c> — the shape the dedup engine fingerprints on. Column is deliberately left
    /// out: it drifts with reformatting while the line and path do not.
    /// </summary>
    private static string? FormatLocation(JsonElement sarifResult)
    {
        foreach (var loc in ImporterHelpers.Array(sarifResult, "locations"))
        {
            if (ImporterHelpers.Child(loc, "physicalLocation") is not { } physical) continue;

            var uri = ImporterHelpers.Child(physical, "artifactLocation") is { } artifact
                ? ImporterHelpers.Text(artifact, "uri")
                : null;

            var line = ImporterHelpers.Child(physical, "region") is { } region
                ? ImporterHelpers.Number(region, "startLine")
                : null;

            if (uri == null) continue;
            return line == null ? uri : $"{uri}:{(int)line.Value}";
        }

        // Some tools report only a logical location (a fully-qualified symbol name).
        foreach (var loc in ImporterHelpers.Array(sarifResult, "locations"))
            foreach (var logical in ImporterHelpers.Array(loc, "logicalLocations"))
                if (ImporterHelpers.Text(logical, "fullyQualifiedName", "name") is { } name)
                    return name;

        return null;
    }

    private static string? Snippet(JsonElement sarifResult)
    {
        foreach (var loc in ImporterHelpers.Array(sarifResult, "locations"))
        {
            if (ImporterHelpers.Child(loc, "physicalLocation") is not { } physical) continue;
            if (ImporterHelpers.Child(physical, "region") is not { } region) continue;
            if (ImporterHelpers.Child(region, "snippet") is not { } snippet) continue;
            if (ImporterHelpers.Text(snippet, "text") is { } text) return text;
        }

        return null;
    }
}
