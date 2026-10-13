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
    /// <summary>
    /// Regression: RequireThirdPartyRead, RequireDataCatalogueRead and RequireAiGovernanceRead accept the
    /// <c>Admin</c> role claim, which the server issues for the admin flag whatever the role is named. The
    /// risk register itself (RequireRiskmanagement) does not, and the admin flag must not open it.
    /// </summary>
    [Theory]
    [InlineData("Analyst")]
    [InlineData(null)]
    public void AdminFlagReadsEveryRegisterButNotTheRiskRegister(string? role)
    {
        var user = new AuthenticatedUserInfo { UserRole = role, IsAdmin = true, UserPermissions = [] };
        Assert.True(Track9WorkspaceAccess.CanOpen(user));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "third_party_manage"));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "data_catalogue_manage"));
        Assert.True(Track9WorkspaceAccess.CanReadRegister(user, "ai_governance_manage"));
        Assert.False(Track9WorkspaceAccess.CanReadRisk(user));
    }
    [Fact]
    public void WithoutAnyAudienceTheWorkspaceStaysClosed()
    {
        var user = new AuthenticatedUserInfo { UserRole = "Analyst", IsAdmin = false, UserPermissions = ["hosts"] };
        Assert.False(Track9WorkspaceAccess.CanOpen(user));
        Assert.False(Track9WorkspaceAccess.CanReadRegister(user, "third_party_manage"));
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
