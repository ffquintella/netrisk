namespace DAL.Enums;

/// <summary>
/// Which way a Jira field mapping moves data (Track 4 milestone 4.6), persisted in
/// <c>jira_field_mappings.direction</c>.
/// </summary>
public enum JiraFieldMappingDirection
{
    /// <summary>NetRisk writes the field when it creates or updates the issue.</summary>
    Outbound = 1,

    /// <summary>NetRisk reads the field off the issue during a sync. Recorded, never written back.</summary>
    Inbound = 2
}
