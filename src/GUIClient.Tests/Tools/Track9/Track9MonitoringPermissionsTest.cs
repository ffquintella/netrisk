using GUIClient.Tools.Track9;
using Model.Authentication;
using Model.DecisionCycle;
using Xunit;

namespace GUIClient.Tests.Tools.Track9;

public class Track9MonitoringPermissionsTest
{
    [Fact]
    public void ThirdLineReadsButCannotUseAnyWriteSurface()
    {
        var user = User("Administrator", true, "riskmanagement", "submit_risks", "close_risks",
            "review_veryhigh", ThirdLineAssurance.PermissionKey);

        Assert.True(Track9MonitoringPermissions.CanRead(user));
        Assert.False(Track9MonitoringPermissions.CanDefineKriOrCommittee(user));
        Assert.False(Track9MonitoringPermissions.CanSubmitRiskData(user));
        Assert.False(Track9MonitoringPermissions.CanCloseRisk(user));
        Assert.False(Track9MonitoringPermissions.CanReviewRisk(user));
        Assert.False(Track9MonitoringPermissions.CanVote(user, true));
    }

    [Fact]
    public void PermissionsMirrorTheApiPolicies()
    {
        Assert.True(Track9MonitoringPermissions.CanRead(User("Administrator", false)));
        Assert.False(Track9MonitoringPermissions.CanRead(User("Admin", true)));
        Assert.True(Track9MonitoringPermissions.CanDefineKriOrCommittee(User("Admin", false)));
        Assert.True(Track9MonitoringPermissions.CanSubmitRiskData(User("Analyst", false, "submit_risks")));
        Assert.True(Track9MonitoringPermissions.CanCloseRisk(User("Admin", false)));
        Assert.True(Track9MonitoringPermissions.CanReviewRisk(User("Reviewer", false, "review_medium")));
    }

    [Fact]
    public void VoteRequiresCurrentCommitteeMembership()
    {
        var user = User("Analyst", false);

        Assert.False(Track9MonitoringPermissions.CanVote(user, false));
        Assert.True(Track9MonitoringPermissions.CanVote(user, true));
    }

    private static AuthenticatedUserInfo User(string role, bool isAdmin, params string[] permissions) => new()
    {
        UserId = 42,
        UserRole = role,
        IsAdmin = isAdmin,
        UserPermissions = [.. permissions]
    };
}
