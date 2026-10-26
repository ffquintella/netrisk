using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.Authentication;
using Model.DecisionCycle;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T251 (S40 §5.13 "Sem permissão", §10.2): the Cyber Risk Overview's button is enabled for <c>risk_index_view</c> or the
/// Admin role claim — the mirror of <c>[PermissionAuthorize(risk_index_view)]</c> — and for nothing else. The shell wires
/// the button and the navigation guard to this one rule.
/// </summary>
[TestSubject(typeof(RiskIndexAccess))]
public class RiskIndexAccessTest
{
    private static AuthenticatedUserInfo User(params string[] permissions) =>
        new() { UserName = "u", UserRole = "user", UserPermissions = new List<string>(permissions) };

    [Fact]
    public void TestTheViewPermissionOpensTheModule()
    {
        Assert.True(RiskIndexAccess.CanView(User(RiskIndexPermissions.View)));
        Assert.True(RiskIndexAccess.CanView(User("riskmanagement", RiskIndexPermissions.View)));
    }

    [Fact]
    public void TestTheAdministratorClaimOpensTheModule()
    {
        var flagged = User();
        flagged.IsAdmin = true;
        var role = User();
        role.UserRole = "Admin";

        Assert.True(RiskIndexAccess.CanView(flagged));
        Assert.True(RiskIndexAccess.CanView(role));
    }

    [Fact]
    public void TestEverythingElseKeepsTheButtonDisabled()
    {
        Assert.False(RiskIndexAccess.CanView(null));
        Assert.False(RiskIndexAccess.CanView(User()));
        Assert.False(RiskIndexAccess.CanView(new AuthenticatedUserInfo { UserName = "u", UserPermissions = null }));
        Assert.False(RiskIndexAccess.CanView(User("riskmanagement", "reports")));
        // Configuring or approving comes with view in the seed (S40 §11.1), but neither is view.
        Assert.False(RiskIndexAccess.CanView(User(RiskIndexPermissions.Configure, RiskIndexPermissions.Approve)));
        // Permission names compare ordinally, as the server compares claim values.
        Assert.False(RiskIndexAccess.CanView(User("RISK_INDEX_VIEW")));
        // The "Administrator" role name is not the Admin claim PermissionAuthorizationHandler admits.
        var administratorRole = User();
        administratorRole.UserRole = "Administrator";
        Assert.False(RiskIndexAccess.CanView(administratorRole));
    }

    [Fact]
    public void TestTheThirdLineReadsTheIndexWhenItHoldsView()
    {
        // A read: ThirdLineReadOnlyRequirement refuses writes only, so the gate does not refuse the third line.
        Assert.True(RiskIndexAccess.CanView(User(ThirdLineAssurance.PermissionKey, RiskIndexPermissions.View)));
    }

    [Fact]
    public void TestTheShellUsesThisRuleForTheButtonAndTheNavigation()
    {
        var client = RiskIndexTestText.ClientDirectory();
        var navigationBar = File.ReadAllText(Path.Combine(client, "Views", "NavigationBar.axaml"));
        var navigationBarViewModel = File.ReadAllText(Path.Combine(client, "ViewModels", "NavigationBarViewModel.cs"));
        var mainWindowViewModel = File.ReadAllText(Path.Combine(client, "ViewModels", "MainWindowViewModel.cs"));

        var button = Regex.Match(navigationBar, @"<Button Name=""BtnRiskOverview""[^>]*>", RegexOptions.Singleline);
        Assert.True(button.Success, "NavigationBar has no BtnRiskOverview.");
        Assert.Contains(@"IsEnabled=""{Binding HasRiskIndexViewPermission}""", button.Value);
        Assert.Contains(@"Command=""{Binding BtRiskOverviewClicked}""", button.Value);
        Assert.Contains("Kind=\"Gauge\"", navigationBar);

        // T255: the bar reads ModuleAccess, whose Cyber Risk Overview rule is this one (ModuleAccessTest pins the equality).
        Assert.Contains("HasRiskIndexViewPermission = ModuleAccess.CanOpen(AvaliableViews.RiskOverview, user)", navigationBarViewModel);
        var moduleAccess = File.ReadAllText(Path.Combine(client, "Tools", "Navigation", "ModuleAccess.cs"));
        Assert.Contains("AvaliableViews.RiskOverview => RiskIndex.RiskIndexAccess.CanView(user)", moduleAccess);
        Assert.Contains("RiskIndexAccess.CanView(AuthenticationService.AuthenticatedUserInfo)", mainWindowViewModel);
    }

    [Fact]
    public void TestTheModuleIsAppendedAtTheEndOfTheViewEnum()
    {
        var source = File.ReadAllText(Path.Combine(RiskIndexTestText.ClientDirectory(), "Models", "AvaliableViews.cs"));
        // Members may carry an explicit value: T294 pinned them when it retired the Master Dashboard (value 1).
        var members = Regex.Matches(Regex.Replace(source, @"///[^\n]*", ""), @"^\s*(?<name>[A-Z]\w*)\s*(?:=\s*\d+\s*)?,?\s*$",
            RegexOptions.Multiline);

        Assert.Equal("RiskOverview", members[^1].Groups["name"].Value);
        Assert.Equal("Incidents", members[^2].Groups["name"].Value);
    }
}
