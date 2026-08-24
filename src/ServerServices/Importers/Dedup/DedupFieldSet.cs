using System.Globalization;
using Contracts.Importers;

namespace ServerServices.Importers.Dedup;

/// <summary>
/// The ordered field list a hash-based strategy digests, resolved from the per-scanner
/// configuration (Track 3 milestone 3.3.1).
///
/// Order is part of the key: hashing the same values in a different order produces a different key,
/// so the configuration stores a list rather than a set and this class preserves it.
/// </summary>
public class DedupFieldSet
{
    /// <summary>
    /// The default set: tool, rule id, asset, location, CVE. Chosen so the key survives the changes
    /// scanners make between runs — a reworded title, a shifted line number in a description, an
    /// updated CVSS score — while still separating two genuinely different findings.
    /// </summary>
    public static readonly string[] Default = ["tool", "ruleId", "asset", "location", "cve"];

    /// <summary>Every field a configuration may name. Anything else is rejected at save time so a
    /// typo cannot silently produce a key built from fewer fields than intended.</summary>
    public static readonly string[] Available =
    [
        "tool", "ruleId", "toolUniqueId", "title", "asset", "hostId", "serviceId",
        "location", "port", "cve", "cwe", "component", "componentVersion", "severity"
    ];

    private readonly List<string> _fields;

    public DedupFieldSet(IEnumerable<string>? fields)
    {
        var requested = (fields ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
        _fields = requested.Count == 0 ? Default.ToList() : requested;
    }

    /// <summary>Parses the comma-separated form stored in <c>scanner_dedup_configurations</c>.</summary>
    public static DedupFieldSet Parse(string? commaSeparated) =>
        new(commaSeparated?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public IReadOnlyList<string> Fields => _fields;

    public override string ToString() => string.Join(",", _fields);

    /// <summary>Field names that are not in <see cref="Available"/>.</summary>
    public IReadOnlyList<string> UnknownFields =>
        _fields.Where(f => !Available.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Resolves each named field to its value, in order. A field the finding does not carry
    /// contributes an empty string rather than being skipped, so two findings that differ only by
    /// one having a CVE still produce different keys.
    /// </summary>
    public IEnumerable<string> Resolve(DedupContext context)
    {
        var f = context.Finding;

        foreach (var field in _fields)
            yield return field.ToLowerInvariant() switch
            {
                "tool" => f.Tool ?? string.Empty,
                "ruleid" => f.RuleId ?? string.Empty,
                "tooluniqueid" => f.ToolUniqueId ?? string.Empty,
                "title" => f.Title ?? string.Empty,
                // The asset identity, preferring the stable network address over the name a DNS
                // change can alter.
                "asset" => f.Host?.Ip ?? f.Host?.Fqdn ?? f.Host?.HostName ?? string.Empty,
                "hostid" => context.HostId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                "serviceid" => context.HostServiceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                "location" => f.Location ?? string.Empty,
                "port" => f.Host?.Port ?? string.Empty,
                // Sorted, because scanners do not agree on the order they list CVEs and an order
                // change is not a new finding.
                "cve" => string.Join("|", f.Cves.Select(c => c.ToUpperInvariant()).OrderBy(c => c, StringComparer.Ordinal)),
                "cwe" => string.Join("|", f.Cwes.Select(c => c.ToUpperInvariant()).OrderBy(c => c, StringComparer.Ordinal)),
                "component" => f.Component ?? string.Empty,
                "componentversion" => f.ComponentVersion ?? string.Empty,
                // The normalized level, not the raw string: a vendor renaming "Moderate" to
                // "Medium" must not split one finding into two.
                "severity" => ((int)f.Severity).ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };
    }
}
