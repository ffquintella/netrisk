using System.Collections.Generic;
using GUIClient.Tools.RiskIndex;
using Model.Authentication;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55 (S40 §6.0, §12.4): who opens the Administration window and which Risk index tab each permission opens, reads only
/// or closes — the table of §6.0, including that every tab reading profiles also needs <c>risk_index_view</c>.
/// </summary>
public class RiskIndexAdminAccessTest
{
    private static RiskIndexAdminRights Rights(params string[] permissions) =>
        RiskIndexAdminRights.From(new AuthenticatedUserInfo { UserName = "u", UserPermissions = new List<string>(permissions) });

    [Fact]
    public void TestNoUserAndNoPermissionOpenNothing()
    {
        Assert.False(RiskIndexAdminRights.From(null).CanOpenRiskIndex);
        Assert.False(RiskIndexAdminAccess.CanOpenAdministration(Rights("riskmanagement")));
        Assert.False(RiskIndexAdminAccess.For(RiskIndexAdminTab.Profiles, Rights()).Enabled);
    }

    [Theory]
    [InlineData("risk_index_configure")]
    [InlineData("risk_index_approve")]
    [InlineData("entity_risk_context")]
    public void TestEachIndexPermissionOpensTheWindowWithoutAdmin(string permission)
    {
        Assert.True(RiskIndexAdminAccess.CanOpenAdministration(Rights(permission)));
    }

    [Fact]
    public void TestTheAdminRoleClaimOpensEveryTab()
    {
        var admin = RiskIndexAdminRights.From(new AuthenticatedUserInfo { UserName = "a", IsAdmin = true, UserPermissions = [] });
        foreach (var tab in System.Enum.GetValues<RiskIndexAdminTab>())
        {
            var access = RiskIndexAdminAccess.For(tab, admin);
            Assert.True(access.Enabled, tab.ToString());
            Assert.False(access.ReadOnly, tab.ToString());
        }
    }

    [Fact]
    public void TestConfigureWithViewEditsDraftsAndReadsTheApproval()
    {
        var rights = Rights("risk_index_view", "risk_index_configure");

        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Profiles, rights));
        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Draft, rights));
        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Preview, rights));
        Assert.Equal(new RiskIndexTabAccess(true, true, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Approval, rights));
        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Sources, rights));
        Assert.Equal(new RiskIndexTabAccess(true, true, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.RiskContext, rights));
        Assert.Equal(RiskIndexAdminTab.Profiles, RiskIndexAdminAccess.FirstOpen(rights));
    }

    [Fact]
    public void TestApproveWithViewApprovesAndReadsTheDraft()
    {
        var rights = Rights("risk_index_view", "risk_index_approve");

        Assert.True(RiskIndexAdminAccess.For(RiskIndexAdminTab.Draft, rights).ReadOnly);
        Assert.True(RiskIndexAdminAccess.For(RiskIndexAdminTab.Preview, rights).ReadOnly);
        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Approval, rights));
        Assert.True(RiskIndexAdminAccess.For(RiskIndexAdminTab.Sources, rights).ReadOnly);
    }

    [Fact]
    public void TestWithoutViewTheProfileTabsCloseWithTheReason()
    {
        var rights = Rights("risk_index_configure", "risk_index_approve");

        foreach (var tab in new[] { RiskIndexAdminTab.Profiles, RiskIndexAdminTab.Draft, RiskIndexAdminTab.Preview,
                     RiskIndexAdminTab.Approval, RiskIndexAdminTab.Sources })
            Assert.Equal(new RiskIndexTabAccess(false, true, RiskIndexAdminAccess.RequiresView), RiskIndexAdminAccess.For(tab, rights));

        // The context tab reads only /Entities (§6.0 table).
        Assert.True(RiskIndexAdminAccess.For(RiskIndexAdminTab.RiskContext, rights).Enabled);
        Assert.Equal(RiskIndexAdminTab.RiskContext, RiskIndexAdminAccess.FirstOpen(rights));
    }

    [Fact]
    public void TestRiskContextAloneEditsOnlyTheContextAndReadsTheProfilesWithView()
    {
        var alone = Rights("entity_risk_context");
        Assert.Equal(new RiskIndexTabAccess(true, false, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.RiskContext, alone));
        Assert.False(RiskIndexAdminAccess.For(RiskIndexAdminTab.Profiles, alone).Enabled);

        var withView = Rights("entity_risk_context", "risk_index_view");
        Assert.Equal(new RiskIndexTabAccess(true, true, null), RiskIndexAdminAccess.For(RiskIndexAdminTab.Profiles, withView));
        Assert.False(RiskIndexAdminAccess.For(RiskIndexAdminTab.Draft, withView).Enabled);
        Assert.Equal(RiskIndexAdminAccess.RequiresConfigure, RiskIndexAdminAccess.For(RiskIndexAdminTab.Draft, withView).DisabledReasonKey);
        Assert.Equal(RiskIndexAdminAccess.RequiresApprove, RiskIndexAdminAccess.For(RiskIndexAdminTab.Approval, withView).DisabledReasonKey);
    }

    [Fact]
    public void TestRightsCarryEntitiesManageForNewActivities()
    {
        Assert.True(Rights("entities_manage", "entity_risk_context").EntitiesManage);
        Assert.False(Rights("entity_risk_context").EntitiesManage);
    }
}
