using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Aqua Trivy JSON report importer.
///
/// Trivy is three scanners in one output: package vulnerabilities, infrastructure
/// misconfigurations, and leaked secrets. All three are imported — a misconfigured security group
/// and a hard-coded key are findings a vulnerability register should carry, and importing only the
/// CVEs (the common shortcut) silently discards most of what Trivy found.
/// </summary>
public class TrivyImporter : IVulnerabilityReportImporter
{
    public string Name => "trivy";
    public string DisplayName => "Aqua Trivy";
    public string Version => "1.0";
    public int ContractVersion => ImporterContract.Version;
    public IReadOnlyList<string> SupportedFileExtensions => [".json"];
    public IReadOnlyList<string> SupportedMimeTypes => ["application/json"];

    private static readonly SeverityMapper Severities = new(SeverityMapper.CvssWords);

    public bool CanHandle(Stream sample) =>
        ImporterHelpers.Sniff(sample, "\"SchemaVersion\"", "\"Results\"") ||
        ImporterHelpers.Sniff(sample, "\"ArtifactType\"", "\"Results\"");

    public async Task<ImportResult> ImportAsync(Stream report, ImportContext ctx, CancellationToken ct)
    {
        using var doc = await ImporterHelpers.ReadJsonAsync(report, ct);
        var root = doc.RootElement;

        var severities = Severities.WithOverrides(ctx.Options);
        var artifact = ImporterHelpers.Text(root, "ArtifactName");

        var result = new ImportResult
        {
            DetectedTool = "trivy",
            // A Trivy scan of an image enumerates every package in it, so it is exhaustive for
            // that artifact and may drive auto-close for it.
            IsFullScan = true,
            ScanDate = ImporterHelpers.Date(root, "CreatedAt")
        };

        if (ImporterHelpers.Child(root, "Results") is not { ValueKind: JsonValueKind.Array } results)
            throw new InvalidDataException("Not a Trivy report: no 'Results' array.");

        var resultIndex = -1;
        foreach (var section in results.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            resultIndex++;

            var target = ImporterHelpers.Text(section, "Target");
            var targetClass = ImporterHelpers.Text(section, "Class");

            ParseVulnerabilities(section, target, artifact, severities, ctx, result, resultIndex);
            ParseMisconfigurations(section, target, artifact, severities, ctx, result, resultIndex);
            ParseSecrets(section, target, artifact, severities, ctx, result, resultIndex, targetClass);
        }

        return result;
    }

    private static void ParseVulnerabilities(JsonElement section, string? target, string? artifact,
        SeverityMapper severities, ImportContext ctx, ImportResult result, int resultIndex)
    {
        var index = -1;
        foreach (var vuln in ImporterHelpers.Array(section, "Vulnerabilities"))
        {
            index++;
            var reference = $"Results[{resultIndex}].Vulnerabilities[{index}]";

            try
            {
                var id = ImporterHelpers.Text(vuln, "VulnerabilityID");
                var pkg = ImporterHelpers.Text(vuln, "PkgName");
                var title = ImporterHelpers.Text(vuln, "Title") ??
                            (id != null && pkg != null ? $"{pkg}: {id}" : id);

                if (string.IsNullOrWhiteSpace(title))
                {
                    result.AddWarning("Vulnerability has neither an id nor a title.", reference, skipped: true);
                    continue;
                }

                var severity = severities.Map(ImporterHelpers.Text(vuln, "Severity"), NormalizedSeverity.None);
                var (vector, score) = BestCvss(vuln);

                // Trivy sometimes reports UNKNOWN severity while carrying a CVSS score; the score
                // is the better answer when it disagrees with a blank word.
                if (severity == NormalizedSeverity.None && score != null)
                    severity = SeverityMapper.FromCvssScore(score);

                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var finding = new NormalizedFinding
                {
                    Tool = "trivy",
                    RuleId = id,
                    ToolUniqueId = null,
                    Title = ImporterHelpers.Clip(title, 250)!,
                    Description = ImporterHelpers.Clip(ImporterHelpers.Text(vuln, "Description"), 65500),
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(vuln, "Severity"),
                    Component = pkg,
                    ComponentVersion = ImporterHelpers.Text(vuln, "InstalledVersion"),
                    FixedInVersion = ImporterHelpers.Text(vuln, "FixedVersion"),
                    // The dedup identity of a dependency finding is the package inside the target,
                    // not the target alone — two packages in one image are two findings.
                    Location = Location(target, pkg, artifact),
                    Cvss3Vector = vector,
                    Cvss3BaseScore = score,
                    CvssBaseScore = score,
                    VulnerabilityPublicationDate = ImporterHelpers.Date(vuln, "PublishedDate"),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt,
                    Solution = Remediation(ImporterHelpers.Text(vuln, "FixedVersion"), pkg)
                };

                if (id != null && id.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
                    finding.Cves.Add(id.ToUpperInvariant());

                foreach (var cwe in ImporterHelpers.Array(vuln, "CweIDs")
                             .Select(c => c.GetString())
                             .Where(c => !string.IsNullOrWhiteSpace(c)))
                    finding.Cwes.Add(cwe!.ToUpperInvariant());

                if (ImporterHelpers.Text(vuln, "PrimaryURL") is { } primary) finding.References.Add(primary);
                foreach (var r in ImporterHelpers.Array(vuln, "References")
                             .Select(r => r.GetString())
                             .Where(r => !string.IsNullOrWhiteSpace(r)))
                    finding.References.Add(r!);

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse vulnerability: {ex.Message}", reference, skipped: true);
            }
        }
    }

    private static void ParseMisconfigurations(JsonElement section, string? target, string? artifact,
        SeverityMapper severities, ImportContext ctx, ImportResult result, int resultIndex)
    {
        var index = -1;
        foreach (var misconf in ImporterHelpers.Array(section, "Misconfigurations"))
        {
            index++;
            var reference = $"Results[{resultIndex}].Misconfigurations[{index}]";

            try
            {
                // Trivy reports every check it ran, passes included. A PASS is not a finding.
                var status = ImporterHelpers.Text(misconf, "Status");
                if (string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase))
                {
                    result.FilteredCount++;
                    continue;
                }

                var id = ImporterHelpers.Text(misconf, "ID", "AVDID");
                var title = ImporterHelpers.Text(misconf, "Title") ?? id;
                if (string.IsNullOrWhiteSpace(title))
                {
                    result.AddWarning("Misconfiguration has neither an id nor a title.", reference, skipped: true);
                    continue;
                }

                var severity = severities.Map(ImporterHelpers.Text(misconf, "Severity"), NormalizedSeverity.Medium);
                if (ctx.IgnoreNegligible && severity == NormalizedSeverity.None)
                {
                    result.FilteredCount++;
                    continue;
                }

                var line = ImporterHelpers.Child(misconf, "CauseMetadata") is { } cause
                    ? ImporterHelpers.Number(cause, "StartLine")
                    : null;

                var location = Location(target, null, artifact);
                if (line is > 0) location = $"{location}:{(int)line.Value}";

                var finding = new NormalizedFinding
                {
                    Tool = "trivy",
                    RuleId = id,
                    Title = ImporterHelpers.Clip(title, 250)!,
                    Description = ImporterHelpers.Clip(
                        Join(ImporterHelpers.Text(misconf, "Description"), ImporterHelpers.Text(misconf, "Message")),
                        65500),
                    Solution = ImporterHelpers.Text(misconf, "Resolution"),
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(misconf, "Severity"),
                    Location = location,
                    Evidence = ImporterHelpers.Text(misconf, "Message"),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                };

                if (ImporterHelpers.Text(misconf, "PrimaryURL") is { } url) finding.References.Add(url);

                result.Findings.Add(finding);
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse misconfiguration: {ex.Message}", reference, skipped: true);
            }
        }
    }

    private static void ParseSecrets(JsonElement section, string? target, string? artifact,
        SeverityMapper severities, ImportContext ctx, ImportResult result, int resultIndex, string? targetClass)
    {
        var index = -1;
        foreach (var secret in ImporterHelpers.Array(section, "Secrets"))
        {
            index++;
            var reference = $"Results[{resultIndex}].Secrets[{index}]";

            try
            {
                var rule = ImporterHelpers.Text(secret, "RuleID");
                var title = ImporterHelpers.Text(secret, "Title") ?? rule;
                if (string.IsNullOrWhiteSpace(title))
                {
                    result.AddWarning("Secret finding has neither a rule id nor a title.", reference, skipped: true);
                    continue;
                }

                // A leaked credential is High at minimum whatever the rule's declared severity —
                // Trivy labels some of its secret rules Medium, which under-states a live key.
                var severity = severities.Map(ImporterHelpers.Text(secret, "Severity"), NormalizedSeverity.High);
                if (severity < NormalizedSeverity.High) severity = NormalizedSeverity.High;

                var line = ImporterHelpers.Number(secret, "StartLine");
                var location = Location(target, null, artifact);
                if (line is > 0) location = $"{location}:{(int)line.Value}";

                result.Findings.Add(new NormalizedFinding
                {
                    Tool = "trivy",
                    RuleId = rule,
                    Title = ImporterHelpers.Clip($"Exposed secret: {title}", 250)!,
                    Description = $"Trivy detected an exposed secret ({ImporterHelpers.Text(secret, "Category")}) " +
                                  $"in {targetClass ?? "the scanned artifact"}.",
                    Solution = "Revoke and rotate the exposed credential, then remove it from the artifact and " +
                               "from version-control history.",
                    Severity = severity,
                    RawSeverity = ImporterHelpers.Text(secret, "Severity"),
                    Location = location,
                    // The matched text is redacted by Trivy itself; storing it gives the triager the
                    // context to find the credential without the credential being in the database.
                    Evidence = ImporterHelpers.Text(secret, "Match"),
                    FirstSeen = ctx.ImportedAt,
                    LastSeen = ctx.ImportedAt
                });
            }
            catch (Exception ex)
            {
                result.AddWarning($"Could not parse secret finding: {ex.Message}", reference, skipped: true);
            }
        }
    }

    /// <summary>
    /// Trivy publishes CVSS scores per source (nvd, redhat, ghsa …). NVD is preferred when present
    /// because it is the one every other tool also uses, which keeps scores comparable; otherwise
    /// the first source that has a v3 score wins.
    /// </summary>
    private static (string? Vector, double? Score) BestCvss(JsonElement vuln)
    {
        if (ImporterHelpers.Child(vuln, "CVSS") is not { ValueKind: JsonValueKind.Object } cvss)
            return (null, null);

        string? vector = null;
        double? score = null;

        foreach (var source in cvss.EnumerateObject())
        {
            if (source.Value.ValueKind != JsonValueKind.Object) continue;

            var v = ImporterHelpers.Text(source.Value, "V3Vector", "V4Vector", "V2Vector");
            var s = ImporterHelpers.Number(source.Value, "V3Score", "V4Score", "V2Score");

            var isNvd = string.Equals(source.Name, "nvd", StringComparison.OrdinalIgnoreCase);
            if (isNvd) return (v, s);

            vector ??= v;
            score ??= s;
        }

        return (vector, score);
    }

    private static string? Location(string? target, string? pkg, string? artifact)
    {
        var basePart = target ?? artifact;
        if (string.IsNullOrWhiteSpace(basePart)) return pkg;
        return string.IsNullOrWhiteSpace(pkg) ? basePart : $"{basePart}#{pkg}";
    }

    private static string? Remediation(string? fixedVersion, string? pkg) =>
        string.IsNullOrWhiteSpace(fixedVersion)
            ? null
            : $"Upgrade {pkg ?? "the package"} to {fixedVersion} or later.";

    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        return kept.Count == 0 ? null : string.Join("\n\n", kept);
    }
}
