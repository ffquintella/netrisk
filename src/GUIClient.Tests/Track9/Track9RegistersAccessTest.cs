using System.Collections.Generic;
using GUIClient.Tools.Track9;
using Model.Authentication;
using Model.DecisionCycle;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9RegistersAccessTest
{
    [Fact]
    public void TestThirdLineCannotManageEvenWithAdminRoleAndAllPermissions()
    {
        var user = User("Admin", ThirdLineAssurance.PermissionKey, "third_party_manage",
            "data_catalogue_manage", "ai_governance_manage", "riskmanagement");

        Assert.False(Track9RegistersAccess.CanManageThirdParties(user));
        Assert.False(Track9RegistersAccess.CanManageDataCatalogue(user));
        Assert.False(Track9RegistersAccess.CanManageAiGovernance(user));
        Assert.False(Track9RegistersAccess.CanEditRiskLinks(user));
    }

    [Fact]
    public void TestEachWriteAudienceMatchesItsApiPolicy()
    {
        Assert.True(Track9RegistersAccess.CanManageThirdParties(User("User", "third_party_manage")));
        Assert.False(Track9RegistersAccess.CanManageThirdParties(User("Administrator")));
        Assert.True(Track9RegistersAccess.CanManageDataCatalogue(User("Admin")));
        Assert.False(Track9RegistersAccess.CanManageDataCatalogue(User("User", "third_party_manage")));
        Assert.True(Track9RegistersAccess.CanManageAiGovernance(User("User", "ai_governance_manage")));
        Assert.True(Track9RegistersAccess.CanEditRiskLinks(User("Administrator")));
        Assert.True(Track9RegistersAccess.CanEditRiskLinks(User("User", "riskmanagement")));
    }

    [Fact]
    public void TestHiddenSupplierRequirementCannotBeEditedWithoutItsVisibleAssociation()
    {
        var manager = User("User", "data_catalogue_manage");

        Assert.True(Track9RegistersAccess.CanEditLegalRequirement(manager, thirdPartyHidden: false));
        Assert.False(Track9RegistersAccess.CanEditLegalRequirement(manager, thirdPartyHidden: true));
    }

    private static AuthenticatedUserInfo User(string role, params string[] permissions) => new()
    {
        UserRole = role,
        UserPermissions = new List<string>(permissions)
    };
}
