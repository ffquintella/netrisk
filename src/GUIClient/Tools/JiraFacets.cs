using DAL.Enums;
using Model.Integrations;

namespace GUIClient.Tools;

/// <summary>
/// Which parts of the Jira screen a connection's provider kind can use. Mirrors the server's facet
/// gate: Assets on Cloud and Data Center, Service Management and the Assets workspace on Cloud only.
/// Free of Avalonia so <c>GUIClient.Tests</c> can compile it directly.
/// </summary>
public static class JiraFacets
{
    public static bool IsJira(IssueTrackerProviderKind kind) =>
        kind is IssueTrackerProviderKind.Jira or IssueTrackerProviderKind.JiraDataCenter;

    public static bool SupportsServiceManagement(IssueTrackerProviderKind kind) =>
        kind == IssueTrackerProviderKind.Jira;

    /// <summary>Data Center serves Assets from the instance itself, so it has no workspace to show.</summary>
    public static bool UsesAssetsWorkspace(IssueTrackerProviderKind kind) =>
        kind == IssueTrackerProviderKind.Jira;

    /// <summary>
    /// The settings the Assets tab saves. Service Management is switched off where it is not
    /// supported, so a Data Center row stored with it on — whose tab offers no way to untick it —
    /// does not trip the server's save-time refusal on every Assets save.
    /// </summary>
    public static JiraConnectionSettingsView ForAssetsSave(JiraConnectionSettingsView settings,
        IssueTrackerProviderKind kind)
    {
        if (!SupportsServiceManagement(kind)) settings.JsmEnabled = false;

        return settings;
    }

    /// <summary>The localization key of the note under the Assets settings, per deployment.</summary>
    public static string AssetsNoteKey(IssueTrackerProviderKind kind) =>
        kind == IssueTrackerProviderKind.JiraDataCenter
            ? "JiraAssetsDataCenterNoteMSG"
            : "JiraAssetsPlanNoteMSG";
}
