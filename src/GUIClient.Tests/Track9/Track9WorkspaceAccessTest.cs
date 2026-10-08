using GUIClient.Tools.Track9;
using Model.Authentication;
using Xunit;
namespace GUIClient.Tests.Track9;
public class Track9WorkspaceAccessTest
{
    [Fact]
    public void NullUserCannotOpenWorkspace() => Assert.False(Track9WorkspaceAccess.CanOpen(null));
    [Fact]
    public void VendorManagerCanReadSuppliersWithoutReadingRiskRegister()
    {
        var user = new AuthenticatedUserInfo { UserPermissions = ["third_party_manage"] };
        Assert.True(Track9WorkspaceAccess.CanOpen(user));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "third_party_manage"));
        Assert.False(Track9WorkspaceAccess.CanReadRisk(user));
        Assert.False(Track9WorkspaceAccess.CanReadRegister(user, "ai_governance_manage"));
    }
    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Admin", false)]
    public void RiskReadMatchesExactServerRole(string role, bool expected)
    {
        var user = new AuthenticatedUserInfo { UserRole = role, IsAdmin = true };
        Assert.Equal(expected, Track9WorkspaceAccess.CanReadRisk(user));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "data_catalogue_manage"));
    }
    [Fact]
    public void RiskReadersIncludingThirdLineCanReadEveryGovernanceRegister()
    {
        var user = new AuthenticatedUserInfo { UserPermissions = ["riskmanagement", "assurance_read"] };
        Assert.True(Track9WorkspaceAccess.CanOpen(user));
        Assert.True(Track9WorkspaceAccess.CanReadRisk(user));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "data_catalogue_manage"));
    }
}
