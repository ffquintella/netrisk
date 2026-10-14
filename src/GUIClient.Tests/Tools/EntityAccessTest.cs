using System.Collections.Generic;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Model.DecisionCycle;
using Model.Entities;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// T241 — the Entities screen let every user add, edit and delete entities and change the risk-context inputs of the
/// index. <see cref="EntityAccess"/> mirrors <c>EntitiesController</c>: <c>entities_manage</c> (or the Admin role
/// claim) for the toolbar and the form, <c>entity_risk_context</c> on top for criticality, internet exposure and
/// classification — never the third line, nothing for an unknown user. Enabling only; the API decides.
/// </summary>
[TestSubject(typeof(EntityAccess))]
public class EntityAccessTest
{
    private static AuthenticatedUserInfo User(params string[] permissions) =>
        new() { UserName = "u", UserRole = "user", UserPermissions = new List<string>(permissions) };

    [Fact]
    public void TestManagingTheMapNeedsEntitiesManage()
    {
        Assert.False(EntityAccess.CanManage(null));
        Assert.False(EntityAccess.CanManage(User()));
        Assert.False(EntityAccess.CanManage(User(EntityPermissions.RiskContext)));
        Assert.False(EntityAccess.CanManage(User("riskmanagement")));
        Assert.True(EntityAccess.CanManage(User(EntityPermissions.Manage)));

        var admin = User();
        admin.IsAdmin = true;
        Assert.True(EntityAccess.CanManage(admin));
        Assert.True(EntityAccess.CanEditRiskContext(admin));
    }

    [Fact]
    public void TestWithoutEntitiesManageEveryFieldIsReadOnlyWithNoPerFieldReason()
    {
        var reader = User(EntityPermissions.RiskContext);

        Assert.True(EntityAccess.IsFieldReadOnly("name", reader));
        Assert.True(EntityAccess.IsFieldReadOnly(RiskContextProperties.Criticality, reader));
        Assert.False(EntityAccess.IsRiskContextLocked(RiskContextProperties.Criticality, reader));
    }

    [Theory]
    [InlineData(RiskContextProperties.Criticality)]
    [InlineData(RiskContextProperties.InternetFacing)]
    [InlineData(RiskContextProperties.SecurityClassification)]
    public void TestAManagerWithoutRiskContextGetsTheRiskContextFieldsLocked(string key)
    {
        var manager = User(EntityPermissions.Manage);

        Assert.False(EntityAccess.IsFieldReadOnly("name", manager));
        Assert.True(EntityAccess.IsFieldReadOnly(key, manager));
        Assert.True(EntityAccess.IsRiskContextLocked(key, manager));
    }

    [Fact]
    public void TestAManagerWithRiskContextEditsEverything()
    {
        var calibrator = User(EntityPermissions.Manage, EntityPermissions.RiskContext);

        Assert.False(EntityAccess.IsFieldReadOnly(RiskContextProperties.Criticality, calibrator));
        Assert.False(EntityAccess.IsRiskContextLocked(RiskContextProperties.Criticality, calibrator));
    }

    [Fact]
    public void TestTheThirdLineEditsNothing()
    {
        var auditor = User(EntityPermissions.Manage, EntityPermissions.RiskContext, ThirdLineAssurance.PermissionKey);
        auditor.IsAdmin = true;

        Assert.False(EntityAccess.CanManage(auditor));
        Assert.False(EntityAccess.CanEditRiskContext(auditor));
        Assert.True(EntityAccess.IsFieldReadOnly("name", auditor));
    }
}
