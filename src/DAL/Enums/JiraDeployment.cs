namespace DAL.Enums;

/// <summary>
/// Which Jira product family a connection talks to (Track 4 milestone 4.6), persisted in
/// <c>jira_connection_settings.deployment</c>.
///
/// This is not cosmetic. Jira Software's issue API is close enough between Cloud and Data Center that
/// milestone 4.2 never needed the distinction, but Assets is a different product on each: Cloud
/// serves it from <c>api.atlassian.com/jsm/assets/workspace/{id}/v1</c> behind a workspace id, while
/// Data Center serves Insight from <c>/rest/insight/1.0/</c> on the site itself with a different
/// object model. Guessing wrong produces 404s that read as "your credentials are wrong", which is
/// why the value is derived from the connection's provider kind rather than chosen.
/// </summary>
public enum JiraDeployment
{
    /// <summary>Atlassian-hosted (<c>*.atlassian.net</c>): Service Management and Assets.</summary>
    Cloud = 1,

    /// <summary>
    /// Self-hosted Jira Data Center (<c>IssueTrackerProviderKind.JiraDataCenter</c>): Assets from the
    /// instance itself; Service Management is refused with a reason.
    /// </summary>
    DataCenter = 2
}
