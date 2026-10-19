using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Tests.Tools.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// T255 (S40 §5.7–§5.9.2, §10.3, IX-2, IX-3, IX-7): what the views and view-models wire, checked in the source because this
/// project does not load Avalonia — the two panels beside "Objetos", the sensitivity that never sorts, the detail dialog's
/// shape and result, every opening of the detail, and the modules that select a record without walking the visual tree.
/// </summary>
public class RiskOverviewDetailAndNavigationSourceTest
{
    private static string Client(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RiskIndexTestText.ClientDirectory() }.Concat(path).ToArray()));

    private static string[] Columns(string view) =>
        Regex.Matches(view, @"<DataGrid(?:Text|Template)Column(?=[\s/>])[^>]*>").Select(m => m.Value).ToArray();

    [Fact]
    public void TestWhatMovedAndTheContributionsSitBesideTheObjectsExpander()
    {
        var page = Client("Views", "RiskOverview", "RiskOverviewView.axaml");

        var changes = page.IndexOf("<riskOverview:RiskChangesPanel DataContext=\"{Binding Changes}\"/>", StringComparison.Ordinal);
        var contributions = page.IndexOf("<riskOverview:RiskContributionsPanel DataContext=\"{Binding Contributions}\"/>",
            StringComparison.Ordinal);
        var objects = page.IndexOf("<riskOverview:RiskObjectList DataContext=\"{Binding ObjectList}\"/>", StringComparison.Ordinal);
        Assert.True(changes > 0 && contributions > changes && objects > contributions, "§5.0: what moved, contributions, objects.");
        Assert.Contains("IsExpanded=\"{Binding Changes.IsExpanded}\"", page);
        Assert.Contains("IsExpanded=\"{Binding Contributions.IsExpanded}\"", page);
    }

    /// <summary>§5.8: Objeto, Categoria and Pontos sort; the sensitivity never does (S39 §1.3).</summary>
    [Fact]
    public void TestOnlyObjectCategoryAndPointsSortTheContributions()
    {
        var columns = Columns(Client("Views", "RiskOverview", "RiskContributionsPanel.axaml"));
        Assert.Equal(9, columns.Length);

        var sortable = columns.Where(c => !c.Contains(@"CanUserSort=""False""", StringComparison.Ordinal)).ToList();
        Assert.Equal(["ObjectSort", "CategoryOrder", "PointsValue"],
            sortable.Select(c => Regex.Match(c, @"SortMemberPath=""(?<p>[^""]+)""").Groups["p"].Value));
        var sensitivity = Assert.Single(columns, c => c.Contains("StrColSensitivity", StringComparison.Ordinal));
        Assert.Contains(@"CanUserSort=""False""", sensitivity);
    }

    /// <summary>§5.7: the attribution keeps the server's order of magnitude; no column re-sorts it.</summary>
    [Fact]
    public void TestWhatMovedNeverResorts()
    {
        var view = Client("Views", "RiskOverview", "RiskChangesPanel.axaml");

        Assert.Contains(@"CanUserSortColumns=""False""", view);
        Assert.All(Columns(view), c => Assert.Contains(@"CanUserSort=""False""", c));
    }

    [Fact]
    public void TestTheContributionsShowTheR1Notice()
    {
        var view = Client("Views", "RiskOverview", "RiskContributionsPanel.axaml");

        Assert.Matches(@"<Border Classes=""riskNotice"">\s*<StackPanel[^>]*>\s*<material:MaterialIcon[^>]*/>\s*<TextBlock[^>]*Text=""\{Binding StrR1Notice\}""", view);
        Assert.Contains("Selector=\"Border.riskNotice\"", Client("Styles", "WindowStyles.axaml"));
    }

    /// <summary>§5.9.2: DialogWindowBase over the navigation result, one centred "Fechar" (IX-3), destinations as type3.</summary>
    [Fact]
    public void TestTheDetailIsAReadOnlyDialogThatClosesWithADestination()
    {
        var dialog = Client("Views", "Dialogs", "RiskIndexObjectDetailDialog.axaml");
        var code = Client("Views", "Dialogs", "RiskIndexObjectDetailDialog.axaml.cs");
        var result = Client("ViewModels", "Dialogs", "Results", "RiskIndexObjectDetailDialogResult.cs");

        Assert.Contains("x:TypeArguments=\"results:RiskIndexObjectDetailDialogResult\"", dialog);
        Assert.Contains(": DialogWindowBase<RiskIndexObjectDetailDialogResult>", code);
        Assert.Contains("public NavigationTarget? Target", result);
        Assert.Single(Regex.Matches(dialog, @"Classes=""dialog\d"""));
        Assert.Matches(@"<Button Classes=""dialog2"" Command=""\{Binding BtCloseClicked\}"">", dialog);
        Assert.Matches(@"<Button Classes=""type3"" IsEnabled=""\{Binding IsEnabled\}""\s+Command=""\{Binding Open\}""", dialog);
        Assert.Contains("ToolTip.Tip=\"{Binding DisabledReason}\"", dialog);
    }

    /// <summary>Every row that names an object opens the detail, and the caller navigates to what it returns (IX-2).</summary>
    [Fact]
    public void TestTheModuleOpensTheDetailFromEveryPanelAndNavigatesToItsResult()
    {
        var module = Client("ViewModels", "RiskOverview", "RiskOverviewViewModel.cs");

        Assert.Contains("new RiskObjectListViewModel { OpenDetail = row => OpenObjectDetailAsync(row.Object) }", module);
        Assert.Contains("new RiskChangesViewModel(OpenObjectDetailAsync)", module);
        Assert.Contains("new RiskContributionsViewModel(OpenObjectDetailAsync)", module);
        Assert.Contains("ObjectList.ShowPresetAsync(link.ObjectFilter!, link.Text, null)", module);
        Assert.Contains("if (result?.Target is { } target) Navigation.NavigateTo(target);", module);
    }

    /// <summary>IX-7: each target module registers itself and selects through its service — no visual tree in the way.</summary>
    [Theory]
    [InlineData("RiskViewModel.cs", "Risk")]
    [InlineData("HostsViewModel.cs", "Devices")]
    [InlineData("IncidentsViewModel.cs", "Incidents")]
    [InlineData("AssessmentViewModel.cs", "Assessment")]
    [InlineData("VulnerabilitiesViewModel.cs", "Vulnerabilities")]
    [InlineData("EntitiesViewModel.cs", "Entities")]
    public void TestEachTargetModuleSelectsARecordThroughItsService(string file, string view)
    {
        var source = Client("ViewModels", file);

        Assert.Matches(@"class \w+\s*:\s*ViewModelBase, ISelectableModule", source);
        Assert.Contains($"RegisterSelectableModule(AvaliableViews.{view}, this);", source);

        var start = source.IndexOf("public async Task<bool> SelectRecordAsync(int id, string? subView)", StringComparison.Ordinal);
        Assert.True(start > 0, file);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.Contains("Toasts.Warning(Localizer[RecordNavigation.OutOfScopeKey])", body);
        Assert.DoesNotContain("FindControl", body);
        Assert.DoesNotContain("GetVisualDescendants", body);
        Assert.DoesNotContain("Task.Run(", body);
    }

    /// <summary>
    /// A module opened for the first time by a link starts its first load as it is built: the selection waits for that load
    /// (kept in <c>_firstLoad</c>), or the load lands after it, replaces the list and drops the selection.
    /// </summary>
    [Theory]
    [InlineData("RiskViewModel.cs")]
    [InlineData("HostsViewModel.cs")]
    [InlineData("IncidentsViewModel.cs")]
    [InlineData("AssessmentViewModel.cs")]
    [InlineData("VulnerabilitiesViewModel.cs")]
    [InlineData("EntitiesViewModel.cs")]
    public void TestEachTargetModuleSelectsOnlyAfterItsFirstLoad(string file)
    {
        var source = Client("ViewModels", file);

        Assert.Matches(@"_firstLoad = \w+Async\(\)", source);
        var start = source.IndexOf("public async Task<bool> SelectRecordAsync(int id, string? subView)", StringComparison.Ordinal);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.Matches(@"await RecordNavigation\.(FindAfterLoadAsync|AwaitLoadAsync)\(_firstLoad", source[start..end]);
    }

    [Fact]
    public void TestTheNavigationRefusesWhatTheBarWouldNotOpen()
    {
        var service = Client("Navigation", "NavigationService.cs");

        Assert.Contains("switch (RecordNavigation.Decide(target, user, faceId))", service);
        Assert.Contains("case RecordNavigationDecision.NotAllowed:", service);
        Assert.Contains("Services.GetRequiredService<SelectableModules>().Find(target.View)", service);
        Assert.Contains("void NavigateTo(NavigationTarget target);", Client("Navigation", "INavigationService.cs"));
    }
}
