using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Models;
using GUIClient.Tests.Tools.RiskIndex;
using GUIClient.Tools.Navigation;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.Authentication;
using Xunit;

namespace GUIClient.Tests.Tools.Navigation;

/// <summary>
/// T255 (S40 §10.3): <see cref="ModuleAccess"/> is the one rule a module's navigation-bar button, the object detail's
/// navigation buttons and <c>NavigateTo(NavigationTarget)</c> read. It must equal, for every module and user, the rule each
/// button was enabled by before (the bar's <c>UserPermissions.Contains(…) || IsAdmin</c> lines and the converter bindings
/// of the Vulnerabilities and Incidents buttons), and the bar must now read it.
/// </summary>
[TestSubject(typeof(ModuleAccess))]
public class ModuleAccessTest
{
    private static AuthenticatedUserInfo User(params string[] permissions) =>
        new() { UserName = "u", UserRole = "user", UserPermissions = new List<string>(permissions) };

    /// <summary>The pre-T255 per-button rules, copied from NavigationBarViewModel.cs and NavigationBar.axaml.</summary>
    private static bool PreviousRule(AvaliableViews view, AuthenticatedUserInfo user)
    {
        var permissions = user.UserPermissions!.ToList();
        var isAdmin = user.IsAdmin;
        // AuthenticatedUserHasPermissionConverter: admin → true; empty list → false; else the permission is listed.
        bool Converter(string p) => isAdmin || (permissions.Count != 0 && permissions.Find(x => x == p) != null);
        return view switch
        {
            AvaliableViews.Dashboard => true, // BtnDashboard has no IsEnabled binding
            AvaliableViews.Assessment => permissions.Contains("assessments") || isAdmin,
            AvaliableViews.Risk => permissions.Contains("riskmanagement") || isAdmin,
            AvaliableViews.Entities => permissions.Contains("asset") || isAdmin,
            AvaliableViews.Devices => permissions.Contains("hosts") || isAdmin,
            AvaliableViews.Vulnerabilities => Converter("vulnerabilities"),
            AvaliableViews.Incidents => Converter("incident_management"),
            AvaliableViews.RiskOverview => RiskIndexAccess.CanView(user),
            AvaliableViews.Users => false, // no button, no shell module: never a destination
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "A new module needs its navigation-bar rule here.")
        };
    }

    public static IEnumerable<object[]> Users()
    {
        string[] permissions =
        [
            "assessments", "riskmanagement", "asset", "hosts", "vulnerabilities", "incident_management", "risk_index_view",
            "reports", "RISKMANAGEMENT", "risk_index_configure"
        ];
        yield return ["none", User()];
        foreach (var p in permissions) yield return [p, User(p)];
        yield return ["all", User(permissions)];
        var admin = User();
        admin.IsAdmin = true;
        yield return ["admin flag", admin];
        var adminRole = User();
        adminRole.UserRole = "Admin";
        yield return ["Admin role", adminRole];
        var administrator = User("hosts");
        administrator.UserRole = "Administrator";
        yield return ["Administrator role", administrator];
    }

    [Theory]
    [MemberData(nameof(Users))]
    public void TestEveryModuleKeepsThePreviousButtonRule(string name, AuthenticatedUserInfo user)
    {
        foreach (var view in Enum.GetValues<AvaliableViews>())
            Assert.True(PreviousRule(view, user) == ModuleAccess.CanOpen(view, user), $"{view} for {name}");
    }

    [Fact]
    public void TestNoUserAndNoPermissionListOpenNothingButTheAdminFlagStillOpensTheModules()
    {
        Assert.All(Enum.GetValues<AvaliableViews>(), v => Assert.False(ModuleAccess.CanOpen(v, null)));

        var listless = new AuthenticatedUserInfo { UserName = "u", UserPermissions = null };
        Assert.All(Enum.GetValues<AvaliableViews>().Where(v => v != AvaliableViews.Dashboard),
            v => Assert.False(ModuleAccess.CanOpen(v, listless)));

        var admin = new AuthenticatedUserInfo { UserName = "a", UserPermissions = null, IsAdmin = true };
        Assert.True(ModuleAccess.CanOpen(AvaliableViews.Risk, admin));
        Assert.True(ModuleAccess.CanOpen(AvaliableViews.Vulnerabilities, admin));
        Assert.False(ModuleAccess.CanOpen(AvaliableViews.Users, admin));
    }

    [Fact]
    public void TestTheRequiredPermissionIsTheOneEachButtonChecks()
    {
        Assert.Equal("hosts", ModuleAccess.RequiredPermission(AvaliableViews.Devices));
        Assert.Equal("assessments", ModuleAccess.RequiredPermission(AvaliableViews.Assessment));
        Assert.Equal("riskmanagement", ModuleAccess.RequiredPermission(AvaliableViews.Risk));
        Assert.Equal("asset", ModuleAccess.RequiredPermission(AvaliableViews.Entities));
        Assert.Equal("vulnerabilities", ModuleAccess.RequiredPermission(AvaliableViews.Vulnerabilities));
        Assert.Equal("incident_management", ModuleAccess.RequiredPermission(AvaliableViews.Incidents));
        Assert.Equal("risk_index_view", ModuleAccess.RequiredPermission(AvaliableViews.RiskOverview));
        Assert.Null(ModuleAccess.RequiredPermission(AvaliableViews.Dashboard));

        // The permission alone opens the module; nothing else does.
        foreach (var view in Enum.GetValues<AvaliableViews>())
            if (ModuleAccess.RequiredPermission(view) is { } permission)
            {
                Assert.True(ModuleAccess.CanOpen(view, User(permission)), view.ToString());
                Assert.False(ModuleAccess.CanOpen(view, User("reports")), view.ToString());
            }
    }

    /// <summary>
    /// T294 retired the Master Dashboard, value 1. The surviving modules keep their numbers and 1 is never reused, so a
    /// value that once meant the Master Dashboard cannot come back meaning another module.
    /// </summary>
    [Fact]
    public void TestTheRetiredMasterDashboardValueIsNotReusedAndNoModuleWasRenumbered()
    {
        Assert.False(Enum.IsDefined(typeof(AvaliableViews), 1));
        Assert.DoesNotContain("MasterDashboard", Enum.GetNames<AvaliableViews>());
        Assert.Equal(0, (int)AvaliableViews.Dashboard);
        Assert.Equal(2, (int)AvaliableViews.Devices);
        Assert.Equal(3, (int)AvaliableViews.Assessment);
        Assert.Equal(4, (int)AvaliableViews.Risk);
        Assert.Equal(5, (int)AvaliableViews.Users);
        Assert.Equal(6, (int)AvaliableViews.Entities);
        Assert.Equal(7, (int)AvaliableViews.Vulnerabilities);
        Assert.Equal(8, (int)AvaliableViews.Incidents);
        Assert.Equal(9, (int)AvaliableViews.RiskOverview);
    }

    [Fact]
    public void TestEveryModuleNameResolvesInTheThreeResourceFiles()
    {
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = Enum.GetValues<AvaliableViews>().Select(ModuleAccess.NameKey).Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }

    /// <summary>The bar reads the rule: each module button binds its property, and the property is ModuleAccess's answer.</summary>
    [Theory]
    [InlineData("BtnRiskOverview", "HasRiskIndexViewPermission", "RiskOverview")]
    [InlineData("BtnAssessment", "HasAssessmentPermission", "Assessment")]
    [InlineData("BtnEntities", "HasEntitiesPermission", "Entities")]
    [InlineData("BtnRisk", "HasRiskPermission", "Risk")]
    [InlineData("BtnVulnerability", "HasVulnerabilitiesPermission", "Vulnerabilities")]
    [InlineData("BtnIncident", "HasIncidentsPermission", "Incidents")]
    [InlineData("BtnDevice", "HasHostsPermission", "Devices")]
    public void TestTheNavigationBarEnablesEachModuleButtonByModuleAccess(string button, string property, string view)
    {
        var client = RiskIndexTestText.ClientDirectory();
        var bar = File.ReadAllText(Path.Combine(client, "Views", "NavigationBar.axaml"));
        var viewModel = File.ReadAllText(Path.Combine(client, "ViewModels", "NavigationBarViewModel.cs"));

        var element = Regex.Match(bar, $@"<Button Name=""{button}""[^>]*>", RegexOptions.Singleline);
        Assert.True(element.Success, button);
        Assert.Contains($@"IsEnabled=""{{Binding {property}}}""", element.Value);
        Assert.Contains($"{property} = ModuleAccess.CanOpen(AvaliableViews.{view}, user);", viewModel);
    }
}
