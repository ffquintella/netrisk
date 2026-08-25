using System.Text;
using System.Text.RegularExpressions;
using DAL.Entities;
using Model.Integrations;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// Renders a finding into an issue title and body (Track 4 milestone 4.2.1/4.2.2).
///
/// Templates are <c>{{Placeholder}}</c> substitution rather than a real template engine on purpose:
/// the values are attacker-influenced finding text going into someone else's issue tracker, and a
/// template language with expressions in that position is a server-side injection surface for no
/// benefit — nobody needs a loop in an issue title.
///
/// An unknown placeholder is left as-is rather than blanked, so a typo shows up in the preview the
/// operator is looking at instead of silently producing an issue with a hole in it.
/// </summary>
internal static class IssueTemplate
{
    internal const string DefaultTitle = "[{{Severity}}] {{Title}}";

    internal const string DefaultDescription = """
        NetRisk finding **#{{FindingId}}** — {{Title}}

        | | |
        |---|---|
        | Severity | {{Severity}} |
        | Status | {{Status}} |
        | Asset | {{Asset}} |
        | Component | {{Component}} |
        | Location | {{Location}} |
        | CVE | {{Cves}} |
        | CVSS | {{Cvss}} |
        | First seen | {{FirstDetection}} |
        | SLA due | {{SlaDueDate}} |

        ### Description

        {{Description}}

        ### Evidence

        {{Evidence}}

        ---
        {{Link}}
        """;

    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// The values a template may reference. Built once per render so the title and the body cannot
    /// disagree about, say, which CVE list they show.
    /// </summary>
    internal static Dictionary<string, string> ValuesFor(Vulnerability finding, string? link,
        string? assetName)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FindingId"] = finding.Id.ToString(),
            ["Title"] = finding.Title ?? string.Empty,
            ["Severity"] = Capitalize(finding.Severity),
            ["RawSeverity"] = finding.RawSeverity ?? string.Empty,
            ["Status"] = finding.LifecycleStatus.ToString(),
            ["Description"] = finding.Description ?? string.Empty,
            // Truncated, and said so: a 4 MB scanner evidence blob posted into Jira is rejected by
            // Jira, and silently posting nothing would be worse than posting an excerpt.
            ["Evidence"] = Excerpt(finding.Solution ?? finding.Comments, 4000),
            ["Asset"] = assetName ?? string.Empty,
            ["Component"] = Join(finding.Component, finding.ComponentVersion),
            ["Location"] = finding.Location ?? string.Empty,
            ["Cves"] = CveLinks(finding.Cves),
            ["Cwes"] = finding.Cwes ?? string.Empty,
            // CVSS v3 preferred over v2 when both are present; a scanner that reports only v2 still
            // gets a number rather than a blank cell.
            ["Cvss"] = finding.Cvss3BaseScore is > 0
                ? finding.Cvss3BaseScore.Value.ToString("0.0")
                : finding.CvssBaseScore is > 0 ? finding.CvssBaseScore.Value.ToString("0.0") : string.Empty,
            ["FirstDetection"] = finding.FirstDetection.ToString("yyyy-MM-dd"),
            ["SlaDueDate"] = finding.SlaDueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["FixedInVersion"] = finding.FixedInVersion ?? string.Empty,
            ["RuleId"] = finding.RuleId ?? string.Empty,
            ["Link"] = link == null ? string.Empty : $"[Open finding #{finding.Id} in NetRisk]({link})"
        };
    }

    internal static string Render(string? template, IReadOnlyDictionary<string, string> values) =>
        Placeholder.Replace(template ?? string.Empty,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    /// <summary>
    /// CVE ids as Markdown links to NVD. Linking rather than listing is what makes the ticket useful
    /// to the developer who has to judge the finding without a NetRisk login.
    /// </summary>
    private static string CveLinks(string? cves)
    {
        if (string.IsNullOrWhiteSpace(cves)) return string.Empty;

        var ids = cves.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => id.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (ids.Count == 0) return cves.Trim();

        return string.Join(", ",
            ids.Select(id => $"[{id}](https://nvd.nist.gov/vuln/detail/{Uri.EscapeDataString(id)})"));
    }

    private static string Join(string? component, string? version) =>
        string.IsNullOrWhiteSpace(component) ? string.Empty
            : string.IsNullOrWhiteSpace(version) ? component
                : $"{component} {version}";

    private static string Excerpt(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = text.Trim();
        return clean.Length <= max ? clean : clean[..max] + $"\n\n_(truncated; {clean.Length - max} more characters in NetRisk)_";
    }

    private static string Capitalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown"
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
