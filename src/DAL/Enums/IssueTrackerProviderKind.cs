namespace DAL.Enums;

/// <summary>
/// Which issue tracker a connection talks to (Track 4 milestone 4.2.2), persisted in
/// <c>issue_tracker_connections.provider</c>.
/// </summary>
public enum IssueTrackerProviderKind
{
    /// <summary>Jira Cloud REST v3, email + API token as basic auth.</summary>
    Jira = 1,

    /// <summary>GitHub Issues, PAT or GitHub App installation token.</summary>
    GitHub = 2,

    /// <summary>GitLab Issues, project or personal access token.</summary>
    GitLab = 3,

    /// <summary>Azure DevOps Work Items, PAT as basic auth.</summary>
    AzureDevOps = 4
}
