using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Xunit;

namespace GUIClient.Tests.Tools;

[TestSubject(typeof(AdminRoleClaim))]
public class AdminRoleClaimTest
{
    /// <summary>
    /// The server adds <c>ClaimTypes.Role = "Admin"</c> for the admin flag and a role claim with the role's name, so
    /// either one holds the claim. The role's name alone is what the desktop used to compare, and missed the flag.
    /// </summary>
    [Theory]
    [InlineData(true, "Administrator", true)]
    [InlineData(true, "Analyst", true)]
    [InlineData(true, null, true)]
    [InlineData(false, "Admin", true)]
    [InlineData(false, "Administrator", false)]
    [InlineData(false, "admin", false)]
    [InlineData(false, null, false)]
    public void TestIsHeldBy(bool isAdmin, string? role, bool expected)
    {
        Assert.Equal(expected, AdminRoleClaim.IsHeldBy(User(isAdmin, role)));
    }

    /// <summary><c>RequireAdminOnly</c>: the <c>Admin</c> claim or the <c>Administrator</c> role, nothing else.</summary>
    [Theory]
    [InlineData(true, "Analyst", true)]
    [InlineData(false, "Admin", true)]
    [InlineData(false, "Administrator", true)]
    [InlineData(false, "Analyst", false)]
    [InlineData(false, null, false)]
    public void TestInAdminOnlyAudience(bool isAdmin, string? role, bool expected)
    {
        Assert.Equal(expected, AdminRoleClaim.InAdminOnlyAudience(User(isAdmin, role)));
    }

    [Fact]
    public void TestNullUserHoldsNothing()
    {
        Assert.False(AdminRoleClaim.IsHeldBy(null));
        Assert.False(AdminRoleClaim.InAdminOnlyAudience(null));
    }

    /// <summary>A permission is not a role: <c>riskmanagement</c> does not make anyone an administrator.</summary>
    [Fact]
    public void TestPermissionsDoNotConferTheClaim()
    {
        var user = new AuthenticatedUserInfo { UserRole = "Analyst", UserPermissions = ["riskmanagement", "Admin"] };

        Assert.False(AdminRoleClaim.IsHeldBy(user));
        Assert.False(AdminRoleClaim.InAdminOnlyAudience(user));
    }

    private static AuthenticatedUserInfo User(bool isAdmin, string? role) =>
        new() { IsAdmin = isAdmin, UserRole = role, UserPermissions = [] };
}
