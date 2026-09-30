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
    /// Whether the Assets schema and object-type pickers may spend a live call against the tracker.
    ///
    /// The refresh button used to fire on <see cref="IsJira"/> alone, so unchecking "Enable Assets"
    /// did nothing to stop it: every click still spent the connection's credential against Jira's
    /// Assets API for a facet the operator had just switched off.
    /// </summary>
    public static bool CanLoadAssetSchemas(IssueTrackerProviderKind kind, bool assetsEnabled) =>
        IsJira(kind) && assetsEnabled;

    /// <summary>
    /// Whether the Assets object-type picker has a schema to load types from. Passing
    /// <see cref="CanLoadAssetSchemas"/> is necessary but not sufficient — the picker also needs the
    /// operator to have picked a schema from the dropdown. The object-type button used to fail this
    /// second check the same way as the first, by returning with no toast, so clicking it before a
    /// schema was selected looked exactly like a dead button.
    /// </summary>
    public static bool HasAssetsSchemaSelected(int? schemaId) => schemaId is not null;

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
