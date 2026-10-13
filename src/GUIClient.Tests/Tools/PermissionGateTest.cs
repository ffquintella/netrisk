using System.Collections.Generic;
using System.Collections.ObjectModel;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Model.DecisionCycle;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// T326 — the findings toolbar's Add, Edit, Import and Delete buttons are gated by <c>HasPermissionConverter</c>,
/// whose decision is <see cref="PermissionGate"/>. It used to accept only an <c>ObservableCollection&lt;string&gt;</c>
/// while the view bound <c>AuthenticatedUserInfo.UserPermissions</c>, a <c>List&lt;string&gt;</c>: the conversion
/// failed, the binding fell back to <c>IsEnabled</c>'s default, and every user got every button. A null input threw,
/// with the same result. These tests pin the opposite: a recognised input is decided like
/// <c>[PermissionAuthorize]</c> decides it on the server, and anything else is "no".
/// </summary>
[TestSubject(typeof(PermissionGate))]
public class PermissionGateTest
{
    private const string Create = "vulnerabilities_create";
    private const string Delete = "vulnerabilities_delete";

    /// <summary>The defect: the client's permission list is a <c>List&lt;string&gt;</c>.</summary>
    [Fact]
    public void TestAPermissionListAsTheClientReceivesItIsHonoured()
    {
        var granted = new List<string> { "vulnerabilities", Create };

        Assert.True(PermissionGate.Allows(granted, Create));
        Assert.False(PermissionGate.Allows(granted, Delete));
    }

    [Fact]
    public void TestAnyCollectionOfPermissionsIsHonoured()
    {
        Assert.True(PermissionGate.Allows(new ObservableCollection<string> { Create }, Create));
        Assert.True(PermissionGate.Allows(new[] { Create }, Create));
        Assert.False(PermissionGate.Allows(new List<string>(), Create));
    }

    /// <summary>Fails closed: no user yet, or an input the gate does not understand, denies instead of throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    [InlineData(Create)]
    [InlineData(true)]
    public void TestAnUnrecognisedSubjectIsDenied(object? subject)
    {
        Assert.False(PermissionGate.Allows(subject, Create));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(7)]
    public void TestAMissingOrUnusablePermissionNameIsDenied(object? permission)
    {
        Assert.False(PermissionGate.Allows(new List<string> { Create, "" }, permission));
        Assert.False(PermissionGate.Allows(User(isAdmin: true), permission));
    }

    /// <summary>The server compares claim values ordinally (<c>PermissionAuthorizationHandler</c>).</summary>
    [Fact]
    public void TestPermissionNamesAreCaseSensitive()
    {
        Assert.False(PermissionGate.Allows(new List<string> { "Vulnerabilities_Create" }, Create));
    }

    [Fact]
    public void TestAUserIsDecidedByTheirPermissions()
    {
        Assert.True(PermissionGate.Allows(User(permissions: [Create]), Create));
        Assert.False(PermissionGate.Allows(User(permissions: ["vulnerabilities"]), Create));
        Assert.False(PermissionGate.Allows(User(permissions: null), Create));
    }

    /// <summary>
    /// <c>PermissionAuthorizationHandler</c> admits the <c>Admin</c> role claim for any permission, so the gate does
    /// too (<see cref="AdminRoleClaim.IsHeldBy"/>): the admin flag, or a role literally named "Admin".
    /// </summary>
    [Fact]
    public void TestTheAdminRoleClaimIsAdmittedWithoutTheExplicitPermission()
    {
        Assert.True(PermissionGate.Allows(User(isAdmin: true), Delete));
        Assert.True(PermissionGate.Allows(User(role: "Admin"), Delete));
        Assert.False(PermissionGate.Allows(User(role: "Administrator"), Delete));
    }

    /// <summary>
    /// <c>ThirdLineReadOnlyRequirement</c> refuses every write to the third line, administrators included, and every
    /// button this gate enables is a write.
    /// </summary>
    [Fact]
    public void TestTheThirdLineIsDeniedEvenWithThePermissionOrTheAdminFlag()
    {
        Assert.False(PermissionGate.Allows(User(permissions: [Create, ThirdLineAssurance.PermissionKey]), Create));
        Assert.False(PermissionGate.Allows(User(isAdmin: true, permissions: [ThirdLineAssurance.PermissionKey]), Create));
        Assert.False(PermissionGate.Allows(new List<string> { Create, ThirdLineAssurance.PermissionKey }, Create));
    }

    private static AuthenticatedUserInfo User(bool isAdmin = false, string? role = "Analyst",
        List<string>? permissions = null) =>
        new() { UserId = 9, UserName = "Pessoa de Teste", UserRole = role, IsAdmin = isAdmin, UserPermissions = permissions };
}
