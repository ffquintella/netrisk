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
    AzureDevOps = 4,

    /// <summary>
    /// Jira Server / Data Center: REST v2, and a Personal Access Token sent as a bearer.
    ///
    /// A separate kind rather than a flag on <see cref="Jira"/> because the two deployments disagree
    /// on all three things a request is made of. Data Center has no <c>/rest/api/3</c> at all, its
    /// description and comment bodies are wiki markup where Cloud's are Atlassian Document Format, and
    /// a PAT there must travel as <c>Authorization: Bearer</c> — sent as a basic-auth password it
    /// fails, and Seraph answers the retries with 403 rather than 401, which reads as a permission
    /// problem on a credential that was never accepted.
    /// </summary>
    JiraDataCenter = 5
}
