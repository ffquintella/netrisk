using System;
using System.Linq;
using System.Text.RegularExpressions;
using GUIClient.Tests.Tools.Hosts;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The structure the Hosts view redesign (S38) promises, held as source scans like the other view
/// tests here — GUIClient.Tests does not reference GUIClient, because that would drag Avalonia into a
/// headless run. Each assertion is a defect the old view had or a layout decision the spec makes.
/// </summary>
public class HostsViewTests
{
    private static string View() => HostsTestFiles.Read("Views/HostsView.axaml");
    private static string ViewModel() => HostsTestFiles.Read("ViewModels/HostsViewModel.cs");

    [Fact]
    public void TheTabsAreVulnerabilitiesOverviewServicesHistoryComments_InThatOrder()
    {
        var headers = Regex.Matches(View(), @"<TabItem Header=""\{Binding (?<str>Str\w+)\}""")
            .Select(m => m.Groups["str"].Value)
            .ToArray();

        Assert.Equal(new[] { "StrVulnerabilities", "StrOverview", "StrServices", "StrHistory", "StrComments" }, headers);
    }

    [Fact]
    public void TheSelectedTabIsBoundTwoWaySoItCanBeRemembered()
    {
        Assert.Contains(@"SelectedIndex=""{Binding SelectedTabIndex, Mode=TwoWay}""", View());
    }

    [Fact]
    public void NoPaneIsPinnedToAFixedHeight()
    {
        // The old view pinned the grid to 250 px and the comments to 170 px.
        Assert.DoesNotMatch(new Regex(@"<Grid[^>]*\bHeight=""\d+"""), View());
    }

    [Fact]
    public void TheBusyRingUsesTheTokenBackedClassInsteadOfANamedColour()
    {
        var view = View();

        Assert.DoesNotContain("CornflowerBlue", view, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"<progRing:ProgressRing[^>]*Classes=""busy"""), view);
        Assert.Contains("progRing|ProgressRing.busy", HostsTestFiles.Read("Styles/WindowStyles.axaml"));
    }

    [Fact]
    public void SearchIsPermanentAndCtrlFFocusesIt()
    {
        var view = View();

        Assert.Contains(@"<KeyBinding Gesture=""Ctrl+F"" Command=""{Binding BtFocusSearchClicked}""/>", view);
        Assert.Contains(@"behaviors:FocusOnSignal.Signal=""{Binding SearchFocusSignal}""", view);
        Assert.DoesNotContain("Kind=\"Magnify\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowHostsFilter", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSearchIsDebouncedAtHalfASecond()
    {
        Assert.Contains("Throttle(TimeSpan.FromMilliseconds(500))", ViewModel());
    }

    [Fact]
    public void EveryToolbarAndPagingButtonCarriesATooltip()
    {
        var buttons = Regex.Matches(View(), @"<Button\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(tag => tag.Contains("subButton") || tag.Contains("detailButton"))
            .ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, tag => Assert.Contains("ToolTip.Tip=\"{Binding Str", tag));
    }

    [Fact]
    public void BothDividersAreRealSplitters()
    {
        Assert.Equal(2, Regex.Matches(View(), @"<GridSplitter[^>]*Classes=""horizontalSplitter""").Count);
    }

    [Fact]
    public void TheGridsAreSortableAndResizable()
    {
        var grids = Regex.Matches(View(), @"<DataGrid\s[^>]*>", RegexOptions.Singleline).Select(m => m.Value).ToList();

        Assert.Equal(2, grids.Count);
        Assert.All(grids, tag =>
        {
            Assert.Contains(@"CanUserSortColumns=""True""", tag);
            Assert.Contains(@"CanUserResizeColumns=""True""", tag);
            Assert.Contains(@"AutoGenerateColumns=""False""", tag);
        });
    }

    [Fact]
    public void LowUseVulnerabilityColumnsStartHidden()
    {
        var view = View();

        foreach (var header in new[] { "StrId", "StrFirstDetection", "StrDetectionCount", "StrAnalyst" })
            Assert.Matches(new Regex($@"Header=""{{Binding {header}}}""[^>]*IsVisible=""False"""), view);

        foreach (var header in new[] { "StrTitle", "StrScore", "StrLastDetection", "StrFixTeam" })
            Assert.DoesNotMatch(new Regex($@"Header=""{{Binding {header}}}""[^>]*IsVisible=""False"""), view);
    }

    [Fact]
    public void TheSeverityColumnSortsNumerically()
    {
        // Severity is stored as "0"–"4"; sorting the label would put "High" before "Low" by spelling.
        Assert.Contains(@"SortMemberPath=""SeverityRank""", View());
    }

    [Fact]
    public void NoColumnResolvesItsCellsThroughABlockingConverter()
    {
        var view = View();

        Assert.DoesNotContain("TeamIdToTeamNameConverter", view, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalystIdToAnalystNameConverter", view, StringComparison.Ordinal);
        Assert.DoesNotContain("IntStatusToColorConverter", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeaderLiteralIdIsGone()
    {
        Assert.DoesNotContain(@"Header=""ID""", View(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression this redesign fixed: the SelectedHost setter started a fire-and-forget
    /// <c>Task.Run</c> that assigned the detail collections from the thread pool, and a slow
    /// response for a host the user had left overwrote the one now selected.
    /// </summary>
    [Fact]
    public void TheViewModelLoadsDetailWithoutTaskRunAndDropsStaleResponses()
    {
        var source = ViewModel();

        Assert.DoesNotContain("Task.Run(", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.InvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (version != _selectionVersion) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (version != _listVersion) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListAsksTheServerForTheTotalAndTheSort()
    {
        Assert.Contains("GetFilteredAsync(PageSize, page, filter, IHostsService.DefaultSort)", ViewModel());
    }

    /// <summary>
    /// A page change must not move <c>_page</c> before the response: a failed load would leave it
    /// advanced and a double-click would skip a page. It is set only in the version-guarded block,
    /// and the paging buttons are disabled while a load is in flight.
    /// </summary>
    [Fact]
    public void ThePageIsAppliedOnlyWithTheResponseAndPagingIsDisabledWhileLoading()
    {
        var source = ViewModel();

        Assert.DoesNotContain("_page++", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_page--", source, StringComparison.Ordinal);
        Assert.Contains("targetPage: _page + 1", source, StringComparison.Ordinal);
        Assert.Contains("targetPage: _page - 1", source, StringComparison.Ordinal);

        Assert.Matches(@"if \(version != _listVersion\) return;\s+_page = page;", source);

        Assert.Contains("IsEnabled=\"{Binding IsPagingEnabled}\"", View(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEditDialogExposesCriticalityEnvironmentAndOwner()
    {
        var dialog = HostsTestFiles.Read("Views/EditHostDialog.axaml");

        Assert.Contains(@"SelectedItem=""{Binding SelectedCriticality}""", dialog);
        Assert.Contains(@"Text=""{Binding Environment}""", dialog);
        Assert.Contains(@"Text=""{Binding Owner}""", dialog);
        Assert.DoesNotContain(">Host Edit<", dialog, StringComparison.Ordinal);

        var viewModel = HostsTestFiles.Read("ViewModels/EditHostDialogViewModel.cs");
        Assert.Contains("Host.Criticality = SelectedCriticality?.Level;", viewModel);
    }
}
