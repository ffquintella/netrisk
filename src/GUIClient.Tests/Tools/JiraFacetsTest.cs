using DAL.Enums;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Integrations;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>What the Jira screen offers per provider kind; must agree with the server's facet gate.</summary>
[TestSubject(typeof(JiraFacets))]
public class JiraFacetsTest
{
    [Theory]
    [InlineData(IssueTrackerProviderKind.Jira, true, true, true)]
    [InlineData(IssueTrackerProviderKind.JiraDataCenter, true, false, false)]
    [InlineData(IssueTrackerProviderKind.GitHub, false, false, false)]
    [InlineData(IssueTrackerProviderKind.GitLab, false, false, false)]
    [InlineData(IssueTrackerProviderKind.AzureDevOps, false, false, false)]
    public void EachKindGetsOnlyTheFacetsItSupports(IssueTrackerProviderKind kind, bool jira,
        bool serviceManagement, bool workspace)
    {
        Assert.Equal(jira, JiraFacets.IsJira(kind));
        Assert.Equal(serviceManagement, JiraFacets.SupportsServiceManagement(kind));
        Assert.Equal(workspace, JiraFacets.UsesAssetsWorkspace(kind));
    }

    [Fact]
    public void DataCenterExplainsAssetsWithoutTheCloudPlanAndWorkspaceNote()
    {
        Assert.Equal("JiraAssetsDataCenterNoteMSG",
            JiraFacets.AssetsNoteKey(IssueTrackerProviderKind.JiraDataCenter));
        Assert.Equal("JiraAssetsPlanNoteMSG", JiraFacets.AssetsNoteKey(IssueTrackerProviderKind.Jira));
    }

    [Theory]
    [InlineData(IssueTrackerProviderKind.JiraDataCenter, false)]
    [InlineData(IssueTrackerProviderKind.Jira, true)]
    public void TheAssetsSaveClearsServiceManagementOnlyWhereItIsUnsupported(
        IssueTrackerProviderKind kind, bool expected)
    {
        var settings = new JiraConnectionSettingsView { JsmEnabled = true, AssetsEnabled = true };

        var saved = JiraFacets.ForAssetsSave(settings, kind);

        Assert.Equal(expected, saved.JsmEnabled);
        Assert.True(saved.AssetsEnabled);
    }

    /// <summary>
    /// The schema/object-type refresh buttons used to fire on <c>IsJira</c> alone, so unchecking
    /// "Enable Assets" did not stop a click from spending the connection's credential against Jira's
    /// live Assets API for a facet the operator had just switched off.
    /// </summary>
    [Theory]
    [InlineData(IssueTrackerProviderKind.Jira, true, true)]
    [InlineData(IssueTrackerProviderKind.Jira, false, false)]
    [InlineData(IssueTrackerProviderKind.JiraDataCenter, true, true)]
    [InlineData(IssueTrackerProviderKind.JiraDataCenter, false, false)]
    [InlineData(IssueTrackerProviderKind.GitHub, true, false)]
    public void AssetSchemasLoadOnlyForAJiraConnectionWithAssetsEnabled(IssueTrackerProviderKind kind,
        bool assetsEnabled, bool expected)
    {
        Assert.Equal(expected, JiraFacets.CanLoadAssetSchemas(kind, assetsEnabled));
    }

    /// <summary>
    /// The object-type button called into the live API only when a schema id was already selected,
    /// but returned silently otherwise — no toast, no log line — so it looked identical to a dead
    /// button when clicked before a schema was chosen.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(1, true)]
    public void ObjectTypesLoadOnlyOnceASchemaIsSelected(int? schemaId, bool expected)
    {
        Assert.Equal(expected, JiraFacets.HasAssetsSchemaSelected(schemaId));
    }
}
