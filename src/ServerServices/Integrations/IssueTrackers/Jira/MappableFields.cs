using DAL.Enums;
using Model.Integrations;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// The NetRisk fields a mapping may write, and the ones a Jira field mapping may read
/// (Track 4 milestone 4.6).
///
/// Published to the client through an endpoint rather than duplicated in the GUI, so the picker cannot
/// offer a target the projector does not understand. That drift is not hypothetical: it is exactly how
/// a mapping screen ends up letting an operator configure something that silently does nothing.
/// </summary>
public static class MappableFields
{
    /// <summary>
    /// The four fields this milestone exists to import, available for every target kind: the name, who
    /// answers for the thing, which environment it is in, and whether it is still live.
    /// </summary>
    public const string Name = "Name";
    public const string Owner = "Owner";
    public const string Environment = "Environment";
    public const string Active = "Active";

    private static readonly MappableFieldView[] Common =
    [
        new() { Name = Name, Label = "Name", Description = "The record's name. Required — a mapping with no name target cannot match or create anything." },
        new() { Name = Owner, Label = "Responsible", Description = "Who answers for it. Free text on a host; matched against a person entity for an application." },
        new() { Name = Environment, Label = "Environment", Description = "Production, homolog, development." },
        new() { Name = Active, Label = "Active state", Description = "Whether the register still considers it live. Maps onto the record's status." }
    ];

    private static readonly MappableFieldView[] HostOnly =
    [
        new() { Name = "HostName", Label = "Host name", AppliesTo = [JiraAssetTargetKind.Host] },
        new() { Name = "Fqdn", Label = "FQDN", AppliesTo = [JiraAssetTargetKind.Host] },
        new() { Name = "Ip", Label = "IP address", AppliesTo = [JiraAssetTargetKind.Host] },
        new() { Name = "MacAddress", Label = "MAC address", AppliesTo = [JiraAssetTargetKind.Host] },
        new() { Name = "Os", Label = "Operating system", AppliesTo = [JiraAssetTargetKind.Host] },
        new() { Name = "OsVersion", Label = "OS version", AppliesTo = [JiraAssetTargetKind.Host] },
        new()
        {
            Name = "Criticality", Label = "Criticality (1–5)", AppliesTo = [JiraAssetTargetKind.Host],
            Description = "Business criticality. Clamped to 1–5 rather than refused, since every CMDB has its own number of bands."
        },
        new() { Name = "Comment", Label = "Comment", AppliesTo = [JiraAssetTargetKind.Host] }
    ];

    private static readonly MappableFieldView[] ApplicationOnly =
    [
        new()
        {
            Name = "Technology", Label = "Technology", AppliesTo = [JiraAssetTargetKind.ApplicationEntity],
            Description = "Free text on the application definition."
        },
        new()
        {
            Name = "SecurityClassification", Label = "Security classification",
            AppliesTo = [JiraAssetTargetKind.ApplicationEntity],
            Description = "Matched by name against the security-classification-level entities; unmatched values are reported, not created."
        }
    ];

    /// <summary>Everything an Assets attribute mapping may target, for the given kind.</summary>
    public static List<MappableFieldView> ForAssetTarget(JiraAssetTargetKind kind) =>
        Common
            .Concat(kind == JiraAssetTargetKind.Host ? HostOnly : ApplicationOnly)
            .ToList();

    /// <summary>Every target, for a picker that has not chosen a kind yet.</summary>
    public static List<MappableFieldView> AllAssetTargets() =>
        Common.Concat(HostOnly).Concat(ApplicationOnly).ToList();

    /// <summary>
    /// Whether a target field is valid for a kind. The save path's guard: a mapping row that targets
    /// <c>MacAddress</c> on an application would otherwise be stored and then quietly skipped.
    /// </summary>
    public static bool IsValidAssetTarget(JiraAssetTargetKind kind, string? field) =>
        !string.IsNullOrWhiteSpace(field)
        && ForAssetTarget(kind).Any(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The values a Jira *field* mapping may send, which is the same vocabulary as the title and
    /// description templates. One list rather than two, so an operator learns the placeholder names
    /// once and can use them in either place.
    /// </summary>
    public static readonly string[] IssueSourceFields =
    [
        "FindingId", "Title", "Severity", "RawSeverity", "Status", "Description", "Evidence", "Asset",
        "Component", "Location", "Cves", "Cwes", "Cvss", "FirstDetection", "SlaDueDate",
        "FixedInVersion", "RuleId", "Link"
    ];

    public static bool IsValidIssueSource(string? field) =>
        // An empty source is legal and means "write the constant instead", which is how a connection
        // stamps every issue it files with a fixed team or component.
        string.IsNullOrWhiteSpace(field)
        || IssueSourceFields.Contains(field, StringComparer.OrdinalIgnoreCase);
}
