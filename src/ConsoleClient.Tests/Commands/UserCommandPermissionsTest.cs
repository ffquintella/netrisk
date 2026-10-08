using System.Collections.Generic;
using System.Linq;
using ConsoleClient.Commands;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DecisionCycle;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace ConsoleClient.Tests.Commands;

/// <summary>
/// Stage 9.9 (S50 R10) — the first administrator is created here with "every permission". From schema 98 on, every
/// permission includes the third-line marker, which restricts rather than grants: swept up with the rest it would make
/// that administrator read-only on every write of the API. The bulk grant leaves it out; it is given through the role.
/// </summary>
[TestSubject(typeof(UserCommand))]
public class UserCommandPermissionsTest
{
    private static IPermissionsService Permissions()
    {
        var service = Substitute.For<IPermissionsService>();
        service.GetAllPermissions().Returns(new List<Permission>
        {
            new() { Id = 16, Key = "riskmanagement", Name = "Risk management", Description = "" },
            new() { Id = 17, Key = "submit_risks", Name = "Submit risks", Description = "" },
            new() { Id = 99, Key = ThirdLineAssurance.PermissionKey, Name = "Third line", Description = "" }
        });
        service.GetDefaultPermissions().Returns(new List<Permission>
        {
            new() { Id = 17, Key = "submit_risks", Name = "Submit risks", Description = "" }
        });
        return service;
    }

    /// <summary>An administrator gets every permission but the third-line marker.</summary>
    [Fact]
    public void TestAnAdministratorGetsEverythingButTheThirdLineMarker()
    {
        var granted = UserCommand.InitialPermissions(admin: true, Permissions());

        Assert.Equal(new[] { "riskmanagement", "submit_risks" }, granted.Select(p => p.Key));
    }

    /// <summary>Anyone else gets the defaults, as before.</summary>
    [Fact]
    public void TestEveryoneElseGetsTheDefaults() =>
        Assert.Equal(new[] { "submit_risks" }, UserCommand.InitialPermissions(admin: false, Permissions()).Select(p => p.Key));

    /// <summary>The rule itself: the marker is the only key no bulk grant may include.</summary>
    [Fact]
    public void TestOnlyTheMarkerIsExcludedFromBulkGrants()
    {
        Assert.False(ThirdLineAssurance.IsBulkGrantable(ThirdLineAssurance.PermissionKey));
        Assert.True(ThirdLineAssurance.IsBulkGrantable("riskmanagement"));
        Assert.True(ThirdLineAssurance.IsBulkGrantable(null));
    }
}
