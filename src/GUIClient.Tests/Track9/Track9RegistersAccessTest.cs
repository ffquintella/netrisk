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

    /// <summary>
    /// Regression: the admin flag is what makes the server issue the <c>Admin</c> role claim, which
    /// <c>PermissionAuthorizationHandler</c> admits for every <c>[PermissionAuthorize]</c> — the three register
    /// writes included. The desktop compared only the role's name with "Admin", so an administrator whose
    /// role is named anything else saw the registers locked while the API would accept the write.
    /// </summary>
    [Theory]
    [InlineData("Administrator")]
    [InlineData("Analyst")]
    [InlineData(null)]
    public void TestAdminFlagManagesEveryRegisterWhateverTheRoleIsNamed(string? role)
    {
        var admin = Admin(role);

        Assert.True(Track9RegistersAccess.CanManageThirdParties(admin));
        Assert.True(Track9RegistersAccess.CanManageDataCatalogue(admin));
        Assert.True(Track9RegistersAccess.CanManageAiGovernance(admin));
        Assert.True(Track9RegistersAccess.CanEditLegalRequirement(admin, thirdPartyHidden: false));
    }

    [Fact]
    public void TestAdminFlagNeverOutranksTheThirdLine()
    {
        var auditor = Admin("Administrator", ThirdLineAssurance.PermissionKey);

        Assert.False(Track9RegistersAccess.CanManageThirdParties(auditor));
        Assert.False(Track9RegistersAccess.CanManageDataCatalogue(auditor));
        Assert.False(Track9RegistersAccess.CanManageAiGovernance(auditor));
        Assert.False(Track9RegistersAccess.CanEditRiskLinks(auditor));
    }

    /// <summary>Risk links sit under <c>RequireRiskmanagement</c>, which does not accept the <c>Admin</c> claim.</summary>
    [Fact]
    public void TestAdminFlagAloneDoesNotEditRiskLinks()
    {
        Assert.False(Track9RegistersAccess.CanEditRiskLinks(Admin("Analyst")));
        Assert.False(Track9RegistersAccess.CanEditRiskLinks(User("Admin")));
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

    private static AuthenticatedUserInfo Admin(string? role, params string[] permissions) => new()
    {
        UserRole = role,
        IsAdmin = true,
        UserPermissions = new List<string>(permissions)
    };
}
