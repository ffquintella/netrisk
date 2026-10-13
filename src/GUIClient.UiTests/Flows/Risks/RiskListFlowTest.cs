using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.UiTests.Infrastructure;
using Material.Icons.Avalonia;
using Model.RiskFlags;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// The register list of <c>RiskView</c>: what it loads, the status filter buttons, the search row (Ctrl+F or the
/// magnifier, by subject or <c>id=</c>), the register-flag filter, the per-row score line and Reload.
/// </summary>
public class RiskListFlowTest
{
    [UiFact]
    public async Task TestTheRegisterListsTheOpenRisksWithTheirScoreLine()
    {
        var server = Arrange();

        var screen = await RiskScreen.OpenAsync(RiskUsers.Reader);

        // Open risks only — the closed one waits for the Closed filter — in the server's order.
        Assert.Equal([LeakSubject, ErpSubject, FraudSubject], screen.Subjects);
        await server.Risks.Received(1).GetAllRisksAsync(false);

        // Track 8 (8.2.3): inherent → residual with the delta, from one score-pair read for the whole register; a
        // risk with no residual shows its inherent score alone.
        await UiHost.WaitUntilAsync(() => screen.ScoreLine(LeakSubject).Length > 0, "the score line to fill");
        Assert.Contains("6", screen.ScoreLine(LeakSubject));
        Assert.Contains("4,5", screen.ScoreLine(LeakSubject));
        Assert.Contains("4", screen.ScoreLine(ErpSubject));

        // No flagged risk: every row says so rather than leaving the line blank.
        Assert.Equal(RiskScreen.Localized("Track9NoFlags"), screen.FlagLine(LeakSubject));

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestAUserOutsideTheRiskmanagementAudienceIsNotServedTheRegister()
    {
        var server = Arrange();
        var outsider = RiskUsers.With();
        outsider.UserPermissions = ["vulnerabilities"];

        var screen = await RiskScreen.OpenAsync(outsider, expectedRows: 0);
        UiHost.Settle(screen.Window);

        Assert.Empty(screen.Subjects);
        await server.Risks.DidNotReceiveWithAnyArgs().GetAllRisksAsync(default);

        UiHost.AssertClean();
    }

    /// <summary>
    /// The four status buttons each toggle one status in or out of the list. New, Mitigation Planned and Management
    /// Review start in (their icon blue); Closed starts out (white) and, switched on, re-reads the register with the
    /// closed risks.
    /// </summary>
    [UiFact]
    public async Task TestTheStatusFilterButtonsToggleTheirStatusInAndOut()
    {
        var server = Arrange();
        var screen = await RiskScreen.OpenAsync(RiskUsers.Reader);

        Assert.Equal(Brushes.DodgerBlue, FilterIcon(screen, "BtNewFilter").Foreground);
        Assert.Equal(Brushes.White, FilterIcon(screen, "BtClosedFilter").Foreground);

        UiInput.Click(screen.Bar("BtNewFilter"));
        Assert.Equal([ErpSubject, FraudSubject], screen.Subjects);
        Assert.Equal(Brushes.White, FilterIcon(screen, "BtNewFilter").Foreground);

        UiInput.Click(screen.Bar("BtMitigationFilter"));
        Assert.Equal([FraudSubject], screen.Subjects);

        UiInput.Click(screen.Bar("BtReviewFilter"));
        Assert.Empty(screen.Subjects);

        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.SequenceEqual([BackupSubject]),
            "the closed risk to be listed once the Closed filter is on");
        await server.Risks.Received(1).GetAllRisksAsync(true);
        Assert.Equal(Brushes.DodgerBlue, FilterIcon(screen, "BtClosedFilter").Foreground);

        UiInput.Click(screen.Bar("BtNewFilter"));
        Assert.Equal([LeakSubject, BackupSubject], screen.Subjects);

        UiHost.AssertClean();
    }

    /// <summary>
    /// IX-8: the magnifier — or Ctrl+F — reveals the search row with the caret in it; typing narrows the list by
    /// subject, case-insensitively, and <c>id=N</c> picks one risk by id.
    /// </summary>
    [UiFact]
    public async Task TestTheSearchRowFiltersBySubjectAndById()
    {
        Arrange();
        var screen = await RiskScreen.OpenAsync(RiskUsers.Reader);

        var search = screen.View.Find<TextBox>(t => t.Name == null && t.Text == "");
        Assert.False(search.IsEffectivelyVisible);

        UiInput.Click(screen.Bar("BtViewFilterRisk"));
        await UiHost.WaitUntilAsync(() => search.IsFocused, "the search box to take the caret when it appears");

        UiInput.Type(screen.Window, "erp");
        Assert.Equal([ErpSubject], screen.Subjects);

        UiInput.ClearByKeyboard(search);
        Assert.Equal([LeakSubject, ErpSubject, FraudSubject], screen.Subjects);

        UiInput.Type(screen.Window, "id=3");
        Assert.Equal([FraudSubject], screen.Subjects);

        // Ctrl+F toggles the row away again (and back), from anywhere in the screen.
        UiInput.Click(screen.Row(FraudSubject));
        UiInput.Press(screen.Window, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        Assert.False(search.IsEffectivelyVisible);
        UiInput.Press(screen.Window, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(search.IsEffectivelyVisible);

        UiHost.AssertClean();
    }

    /// <summary>
    /// Stage 9 register flags: the row's second line names the risk's flags, and the flag filter of the search row
    /// keeps only the risks carrying the chosen flag.
    /// </summary>
    [UiFact]
    public async Task TestTheRegisterFlagFilterKeepsTheRisksCarryingTheFlag()
    {
        Arrange();
        UiServices.Current.Double<IRiskFlagsService>().GetFlaggedAsync().ReturnsForAnyArgs(_ => Task.FromResult(
            new System.Collections.Generic.List<FlaggedRiskDto>
            {
                new() { RiskId = Leak, Subject = LeakSubject, Flags = [RiskFlagCode.SensitiveData], GateA = true }
            }));

        var screen = await RiskScreen.OpenAsync(RiskUsers.Reader);

        var sensitive = RiskScreen.Localized("Track9RiskFlagCodeSensitiveData");
        Assert.Equal(sensitive + " · " + RiskScreen.Localized("Track9GateA"), screen.FlagLine(LeakSubject));

        UiInput.Click(screen.Bar("BtViewFilterRisk"));
        var flagFilter = screen.View.Find<ComboBox>(c => c.IsEffectivelyVisible);
        Assert.True(flagFilter.IsEffectivelyEnabled);

        await UiInput.ChooseAsync(flagFilter, sensitive);
        Assert.Equal([LeakSubject], screen.Subjects);

        await UiInput.ChooseAsync(flagFilter, RiskScreen.Localized("Track9AllFlags"));
        Assert.Equal([LeakSubject, ErpSubject, FraudSubject], screen.Subjects);

        UiHost.AssertClean();
    }

    /// <summary>Reload re-reads the register and clears the search text.</summary>
    [UiFact]
    public async Task TestReloadRereadsTheRegisterAndClearsTheSearch()
    {
        var server = Arrange();
        var screen = await RiskScreen.OpenAsync(RiskUsers.Reader);

        UiInput.Click(screen.Bar("BtViewFilterRisk"));
        UiInput.Type(screen.Window, "fraude");
        Assert.Equal([FraudSubject], screen.Subjects);

        server.Rows.RemoveAll(r => r.Id == Erp);
        UiInput.Click(screen.Bar("BtReloadRisk"));

        await UiHost.WaitUntilAsync(() => screen.Subjects.SequenceEqual([LeakSubject, FraudSubject]),
            "the reloaded register without the risk the server no longer has");
        await server.Risks.Received(2).GetAllRisksAsync(false);
        Assert.Equal("", screen.View.Find<TextBox>(t => t.IsEffectivelyVisible && t.Name == null).Text);

        UiHost.AssertClean();
    }

    private static MaterialIcon FilterIcon(RiskScreen screen, string button) =>
        screen.Bar(button).GetVisualDescendants().OfType<MaterialIcon>().Single();
}
