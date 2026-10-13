using System.Collections.Generic;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Model.DecisionCycle;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// T328 — the bar under the desktop risk list enabled Add and Edit for everyone and gated Close (and Reopen) on
/// <c>delete_risk</c>. <see cref="RiskRecordAccess"/> now mirrors the policies of the <c>RisksController</c> actions
/// those buttons end in: <c>RequireSubmitRisk</c> (the <c>submit_risks</c> permission only), <c>RequireCloseRisk</c>
/// and <c>RequireDeleteRisk</c> (the Admin role claim or the permission) — never the third line, and nothing for an
/// unknown user. The on-screen half is <c>GUIClient.UiTests/Flows/Risks/RiskToolbarFlowTest</c> (on demand, S54).
/// </summary>
[TestSubject(typeof(RiskRecordAccess))]
public class RiskRecordAccessTest
{
    [Fact]
    public void TestEachWriteFollowsItsOwnPermission()
    {
        var submitter = User("submit_risks");
        Assert.True(RiskRecordAccess.CanSubmit(submitter));
        Assert.False(RiskRecordAccess.CanClose(submitter));
        Assert.False(RiskRecordAccess.CanDelete(submitter));

        var closer = User("close_risks");
        Assert.False(RiskRecordAccess.CanSubmit(closer));
        Assert.True(RiskRecordAccess.CanClose(closer));
        Assert.False(RiskRecordAccess.CanDelete(closer));

        var deleter = User("delete_risk");
        Assert.False(RiskRecordAccess.CanSubmit(deleter));
        Assert.False(RiskRecordAccess.CanClose(deleter));
        Assert.True(RiskRecordAccess.CanDelete(deleter));
    }

    /// <summary>The defects: reading the register granted Add/Edit, and <c>delete_risk</c> granted Close.</summary>
    [Fact]
    public void TestReadingTheRegisterGrantsNoWrite()
    {
        var reader = User();

        Assert.False(RiskRecordAccess.CanSubmit(reader));
        Assert.False(RiskRecordAccess.CanClose(reader));
        Assert.False(RiskRecordAccess.CanDelete(reader));
    }

    /// <summary>
    /// The Admin role claim admits Close and Delete, as RequireCloseRisk and RequireDeleteRisk do, but not Add/Edit:
    /// RequireSubmitRisk is a claims requirement on <c>submit_risks</c> alone.
    /// </summary>
    [Theory]
    [InlineData(true, "Analyst")]
    [InlineData(false, "Admin")]
    public void TestTheAdminClaimAdmitsCloseAndDeleteButNotSubmit(bool isAdmin, string role)
    {
        var admin = User();
        admin.IsAdmin = isAdmin;
        admin.UserRole = role;

        Assert.False(RiskRecordAccess.CanSubmit(admin));
        Assert.True(RiskRecordAccess.CanClose(admin));
        Assert.True(RiskRecordAccess.CanDelete(admin));
    }

    /// <summary><c>Administrator</c> is a role name, not the Admin claim: it admits nothing here on its own.</summary>
    [Fact]
    public void TestTheAdministratorRoleNameAloneAdmitsNoWrite()
    {
        var user = User();
        user.UserRole = "Administrator";

        Assert.False(RiskRecordAccess.CanSubmit(user));
        Assert.False(RiskRecordAccess.CanClose(user));
        Assert.False(RiskRecordAccess.CanDelete(user));
    }

    [Fact]
    public void TestTheThirdLineIsRefusedEveryWrite()
    {
        var auditor = User("submit_risks", "close_risks", "delete_risk", ThirdLineAssurance.PermissionKey);
        auditor.IsAdmin = true;

        Assert.False(RiskRecordAccess.CanSubmit(auditor));
        Assert.False(RiskRecordAccess.CanClose(auditor));
        Assert.False(RiskRecordAccess.CanDelete(auditor));
    }

    [Fact]
    public void TestNoUserOrNoPermissionListIsNo()
    {
        Assert.False(RiskRecordAccess.CanSubmit(null));
        Assert.False(RiskRecordAccess.CanClose(null));
        Assert.False(RiskRecordAccess.CanDelete(null));

        var empty = new AuthenticatedUserInfo { UserPermissions = null };
        Assert.False(RiskRecordAccess.CanSubmit(empty));
        Assert.False(RiskRecordAccess.CanClose(empty));
        Assert.False(RiskRecordAccess.CanDelete(empty));
    }

    private static AuthenticatedUserInfo User(params string[] permissions)
    {
        var granted = new List<string> { "riskmanagement" };
        granted.AddRange(permissions);
        return new AuthenticatedUserInfo { UserId = 2, UserRole = "Analyst", UserPermissions = granted };
    }
}
