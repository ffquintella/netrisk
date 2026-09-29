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
}
