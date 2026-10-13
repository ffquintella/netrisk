using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Tools;
using GUIClient.UiTests.Infrastructure;
using GUIClient.ViewModels;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.Views;
using Microsoft.Extensions.DependencyInjection;
using Model.Authentication;
using NSubstitute;
using Material.Icons;
using Material.Icons.Avalonia;
using Semi.Avalonia;
using Xunit;

namespace GUIClient.UiTests.Smoke;

/// <summary>
/// The view smoke catalogue (S54 §5.1 item 2): the shell and its navigation, a dialog on
/// <see cref="DialogWindowBase{TResult}"/>, and two list screens built on a grid — the hosts list
/// (DataGrid) and the findings list (TreeDataGrid). Each opens with its real view-model and the
/// harness doubles and fails on an exception, a standing binding error, or an exception escaping a
/// ReactiveCommand while it is up.
///
/// The catalogue is explicit and deliberately small (S54 §5.2: covering every view is future work).
/// A view joins it by adding a test here.
/// </summary>
public class ViewSmokeTest
{
    /// <summary>
    /// The harness itself: the Application under test is the client's own, so the views below are
    /// styled by the client's theme, tokens and style sheets — not by a stand-in.
    /// </summary>
    [UiFact]
    public void TestApplicationLoadsTheClientStylesAndTokens()
    {
        var app = Assert.IsType<TestApp>(Application.Current);
        Assert.Contains(app.Styles, s => s is SemiTheme);
        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);

        // A class defined in Styles/WindowStyles.axaml, coloured from Styles/Tokens.axaml: if either
        // file were missing the text would keep the theme's default foreground.
        Assert.True(app.TryGetResource("NrStatusErrorText", ThemeVariant.Dark, out var errorBrush));
        var summary = new TextBlock { Text = "x", Classes = { "validationSummary" } };
        UiHost.Host(summary, 200, 100);

        Assert.Equal(errorBrush, summary.Foreground);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, summary.TextWrapping);

        UiHost.AssertClean();
    }

    /// <summary>
    /// The shell: menu, navigation bar, toast host and every content view it hosts (dashboard,
    /// master dashboard, hosts, assessments, risks, entities, findings, incidents), signed in as an
    /// administrator so each content view also runs its first load against the empty-server doubles.
    /// Then a simulated click on the navigation bar's Risks button must swap the dashboard for the
    /// risk register.
    /// </summary>
    [UiFact]
    public async Task TestMainWindowShellLoadsAndNavigates()
    {
        var services = UiServices.Current;
        var auth = services.Double<IAuthenticationService>();
        auth.IsAuthenticated.Returns(true);
        auth.AuthenticatedUserInfo.Returns(Users.Administrator);

        var window = new MainWindow();
        services.Double<IMainWindowProvider>().GetMainWindow().Returns(window);
        services.Double<IMainWindowProvider>().GetActiveWindow().Returns(window);

        UiHost.Show(window);

        var shell = Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.True(window.Find<DashboardView>().IsVisible);
        Assert.False(window.Find<RiskView>().IsVisible);

        auth.AuthenticationSucceeded += Raise.Event();
        var riskButton = window.Find<Button>(b => b.Name == "BtnRisk");
        await UiHost.WaitUntilAsync(() => riskButton.IsEffectivelyEnabled, "the Risks button to enable for an administrator");

        UiInput.Click(riskButton);

        await UiHost.WaitUntilAsync(() => shell.RiskIsVisible, "the shell to navigate to the risk register");
        Assert.True(window.Find<RiskView>().IsVisible);
        Assert.False(window.Find<DashboardView>().IsVisible);

        // T326: an administrator is admitted to the findings toolbar the way the API admits one — through the
        // Admin role claim, without holding vulnerabilities_create/_delete explicitly.
        // The findings view is hidden (not templated), so the button is looked up by name, not in the visual tree.
        var delete = window.Find<VulnerabilitiesView>().FindControl<Button>("BtnDelete")!;
        await UiHost.WaitUntilAsync(() => delete.IsEnabled, "the administrator to be admitted to the findings toolbar");

        UiHost.AssertClean();
    }

    /// <summary>
    /// A modal on <see cref="DialogWindowBase{TResult}"/>, opened the way the client opens every
    /// dialog — through <see cref="IDialogService"/>, which finds the view by naming convention,
    /// resolves the view-model from the container and activates it with its parameter.
    /// </summary>
    [UiFact]
    public async Task TestCloseRiskDialogOpensThroughTheDialogService()
    {
        var services = UiServices.Current;
        services.Double<IRisksService>().GetRiskCloseReasons().Returns(
        [
            new CloseReason { Value = 1, Name = "Mitigado" },
            new CloseReason { Value = 2, Name = "Aceito" }
        ]);

        var owner = UiHost.Host(new TextBlock());
        services.Double<IMainWindowProvider>().GetActiveWindow().Returns(owner);

        var pending = services.GetRequiredService<IDialogService>()
            .ShowDialogAsync<CloseRiskDialogResult, CloseRiskDialogParameter>(
                nameof(CloseRiskViewModel), new CloseRiskDialogParameter { Risk = new Risk { Id = 7 } });

        await UiHost.WaitUntilAsync(() => owner.OwnedWindows.Count == 1, "the close-risk dialog to open");
        var dialog = Assert.IsType<CloseRiskWindow>(owner.OwnedWindows[0]);
        UiHost.Settle(dialog);

        Assert.Equal(2, dialog.Find<ComboBox>().ItemCount);
        Assert.False(dialog.Find<Button>(b => b.Name == "BtSave").IsEffectivelyEnabled);

        UiInput.Click(dialog.Find<Button>(b => b.Name == "BtCancel"));

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ResultActions.Cancel, result!.Action);

        UiHost.AssertClean();
    }

    /// <summary>
    /// Grid list 1 — the hosts screen. The host list loads, a simulated click selects a host, and
    /// the host's findings fill the detail DataGrid.
    /// </summary>
    [UiFact]
    public async Task TestHostsViewListsHostsAndShowsTheSelectedHostsFindings()
    {
        var services = UiServices.Current;
        var auth = services.Double<IAuthenticationService>();
        auth.AuthenticatedUserInfo.Returns(Users.WithPermissions("hosts"));

        var hosts = services.Double<IHostsService>();
        hosts.GetFilteredAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((Fixtures.Hosts(), 2));
        hosts.GetAllHostVulnerabilitiesAsync(1).Returns(Fixtures.Findings(hostId: 1));

        var view = new HostsView();
        var window = UiHost.Host(view);

        auth.AuthenticationSucceeded += Raise.Event();

        var list = view.Find<ListBox>(l => l.ItemsSource is IEnumerable<Host>);
        await UiHost.WaitUntilAsync(() => list.ItemCount == 2, "the host list to load");

        var firstHost = list.GetVisualDescendants().OfType<ListBoxItem>().First();
        UiInput.Click(firstHost);

        var findingsGrid = view.Find<DataGrid>(g => g.IsEffectivelyVisible);
        await UiHost.WaitUntilAsync(
            () => findingsGrid.GetVisualDescendants().OfType<DataGridRow>().Count() == 2,
            "the selected host's two findings to fill the grid");

        Assert.Equal("srv-app-01", ((HostsViewModel)view.DataContext!).SelectedHost?.HostName);
        Assert.Contains(findingsGrid.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "OpenSSL desatualizado");

        // T325: the findings' status icon (IntStatus.New) is drawn; it used to stay empty.
        Assert.Contains(findingsGrid.GetVisualDescendants().OfType<MaterialIcon>(),
            i => i.Kind == MaterialIconKind.NewReleases);

        UiHost.AssertClean();
    }

    /// <summary>Grid list 2 — the findings screen, on a TreeDataGrid.</summary>
    [UiFact]
    public async Task TestVulnerabilitiesViewListsFindingsInItsGrid()
    {
        var services = UiServices.Current;
        var auth = services.Double<IAuthenticationService>();
        auth.AuthenticatedUserInfo.Returns(Users.WithPermissions("vulnerabilities"));

        services.Double<IVulnerabilitiesService>()
            .GetFilteredAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(Tuple.Create(Fixtures.Findings(hostId: 1), 2, true));

        var view = new VulnerabilitiesView();
        UiHost.Host(view);

        auth.AuthenticationSucceeded += Raise.Event();

        var grid = view.Find<TreeDataGrid>();
        await UiHost.WaitUntilAsync(
            () => grid.GetVisualDescendants().OfType<TreeDataGridRow>().Count() == 2,
            "the two findings to be realized as grid rows");

        Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "OpenSSL desatualizado");

        // T326: this analyst may read findings but holds neither vulnerabilities_create nor _delete, so the
        // write buttons are disabled — they used to be enabled for everyone.
        foreach (var name in new[] { "BtnAdd", "BtnEdit", "BtnDelete", "BtnImport" })
            Assert.False(view.Find<Button>(b => b.Name == name).IsEnabled, name + " should be disabled");

        UiHost.AssertClean();
    }
}

/// <summary>Synthetic users (S54 §9: no production data in tests).</summary>
internal static class Users
{
    public static AuthenticatedUserInfo Administrator => new()
    {
        UserId = 1,
        UserName = "Administradora de Teste",
        UserAccount = "admin.teste",
        UserEmail = "admin.teste@example.invalid",
        UserRole = "Administrator",
        IsAdmin = true,
        UserPermissions = ["riskmanagement", "asset", "assessments", "reports", "hosts", "vulnerabilities", "incidents"]
    };

    public static AuthenticatedUserInfo WithPermissions(params string[] permissions) => new()
    {
        UserId = 2,
        UserName = "Analista de Teste",
        UserAccount = "analista.teste",
        UserEmail = "analista.teste@example.invalid",
        UserRole = "Analyst",
        UserPermissions = permissions.ToList()
    };
}

/// <summary>Synthetic hosts and findings (S54 §9).</summary>
internal static class Fixtures
{
    public static List<Host> Hosts() =>
    [
        new() { Id = 1, HostName = "srv-app-01", Ip = "10.0.0.11", Status = 1, Source = "manual", Criticality = 4 },
        new() { Id = 2, HostName = "srv-db-01", Ip = "10.0.0.12", Status = 1, Source = "manual" }
    ];

    public static List<Vulnerability> Findings(int hostId) =>
    [
        new()
        {
            Id = 10, HostId = hostId, Title = "OpenSSL desatualizado", Severity = "3", Score = 7.5,
            Status = 1, FirstDetection = new DateTime(2026, 9, 1), LastDetection = new DateTime(2026, 10, 1),
            Technology = "Linux"
        },
        new()
        {
            Id = 11, HostId = hostId, Title = "TLS 1.0 habilitado", Severity = "2", Score = 5.0,
            Status = 1, FirstDetection = new DateTime(2026, 9, 2), LastDetection = new DateTime(2026, 10, 2),
            Technology = "Linux"
        }
    ];
}
