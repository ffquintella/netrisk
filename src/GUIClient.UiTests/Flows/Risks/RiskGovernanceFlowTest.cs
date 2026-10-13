using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using GUIClient.Views;
using Material.Icons;
using Model.Governance;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Track 8: the lifecycle toolbar's governance button opens <see cref="RiskGovernanceWindow"/> for the selected risk.
/// Covered here as far as the risk screen is concerned — the window opens on the right risk with its scores, an
/// acceptance recorded there makes the register re-read on close, and nothing else does; the window's own tabs
/// (simulation, audit trail, task evidence) are its own feature and are only smoke-tested through the task list.
/// </summary>
public class RiskGovernanceFlowTest
{
    [UiFact]
    public async Task TestAnAcceptanceRecordedInTheGovernanceRecordReloadsTheRegister()
    {
        var server = Arrange();
        var governance = UiServices.Current.Double<IRiskGovernanceService>();
        governance.CreateAcceptanceAsync(Leak, Arg.Any<RiskAcceptanceRequest>())
            .Returns(_ => Task.FromResult(new RiskAcceptance { Id = 1, Name = "Aceite", RiskId = Leak }));

        var screen = await OpenAsync(RiskUsers.With("review_high"));
        await screen.SelectAsync(LeakSubject);

        UiInput.Click(screen.Lifecycle(MaterialIconKind.ScaleBalance));
        var dialog = await screen.WaitForDialogAsync<RiskGovernanceWindow>();

        // The record of the selected risk, with both scores from the score-pair read.
        await UiHost.WaitUntilAsync(() => Texts(dialog).Contains(LeakSubject), "the record to name the risk");
        Assert.Contains("6,0", Texts(dialog));
        Assert.Contains("4,5", Texts(dialog));
        await governance.Received().GetAcceptancesAsync(Leak);

        var accept = dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("AcceptRisk"));
        Assert.True(accept.IsEffectivelyEnabled);

        var justification = Field<TextBox>(dialog, Localized("BusinessJustification"));
        UiInput.Click(justification);
        UiInput.Type(dialog, "Custo do controle supera a perda esperada.");
        UiInput.Click(accept);

        await governance.Received(1).CreateAcceptanceAsync(Leak, Arg.Is<RiskAcceptanceRequest>(r =>
            r.BusinessJustification == "Custo do controle supera a perda esperada." &&
            r.ExpiresAt > DateTime.UtcNow.AddDays(80)));

        UiInput.Click(dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("Close")));
        await screen.WaitForDialogsToCloseAsync("the governance record to close");

        // A new acceptance changes the risk's status, so the register is re-read rather than left stale.
        await UiHost.WaitUntilAsync(() => server.Risks.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == nameof(IRisksService.GetAllRisksAsync)) == 2,
            "the register to be re-read after the acceptance");

        UiHost.AssertClean();
    }

    /// <summary>Without a review permission the acceptance is off; closing an unchanged record re-reads nothing.</summary>
    [UiFact]
    public async Task TestWithoutAReviewPermissionNothingCanBeAcceptedAndCloseChangesNothing()
    {
        var server = Arrange();
        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(ErpSubject);

        UiInput.Click(screen.Lifecycle(MaterialIconKind.ScaleBalance));
        var dialog = await screen.WaitForDialogAsync<RiskGovernanceWindow>();
        await UiHost.WaitUntilAsync(() => Texts(dialog).Contains(ErpSubject), "the record to name the risk");

        Assert.False(dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("AcceptRisk")).IsEffectivelyEnabled);

        UiInput.Click(dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("Close")));
        await screen.WaitForDialogsToCloseAsync("the governance record to close");
        await server.Risks.Received(1).GetAllRisksAsync(false);

        UiHost.AssertClean();
    }

    /// <summary>The mitigation tasks tab lists the risk's tasks; choosing one shows its detail panel.</summary>
    [UiFact]
    public async Task TestChoosingAMitigationTaskShowsItsDetail()
    {
        Arrange();
        UiServices.Current.Double<IRiskGovernanceService>().GetTasksByRiskAsync(Erp).Returns(_ =>
            Task.FromResult(new List<MitigationTask>
            {
                new()
                {
                    Id = 1, MitigationId = 20, Title = "Contratar ERP em alta disponibilidade",
                    DueDate = new DateTime(2026, 12, 15), AcceptanceCriterion = "Failover testado",
                    Owner = new User { Value = RiskUsers.ManagerId, Name = "Gestora de Riscos" }
                }
            }));

        var screen = await OpenAsync(RiskUsers.Reader);
        await screen.SelectAsync(ErpSubject);
        UiInput.Click(screen.Lifecycle(MaterialIconKind.ScaleBalance));
        var dialog = await screen.WaitForDialogAsync<RiskGovernanceWindow>();

        UiInput.Click(dialog.Find<TabItem>(t => t.Header as string == Localized("MitigationTasks")));
        var selectedTask = dialog.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == Localized("Track9SelectedTask"));
        Assert.False(selectedTask.IsEffectivelyVisible);

        var row = await WaitForRowAsync(dialog, "Contratar ERP em alta disponibilidade");
        UiInput.Click(row);

        await UiHost.WaitUntilAsync(() => selectedTask.IsEffectivelyVisible, "the selected task's panel to appear");
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "Failover testado");

        UiInput.Click(dialog.Find<Button>(b => UiInput.TextOf(b) == Localized("Close")));
        await screen.WaitForDialogsToCloseAsync("the governance record to close");

        UiHost.AssertClean();
    }

    private static async Task<DataGridRow> WaitForRowAsync(Window dialog, string title)
    {
        DataGridRow? row = null;
        await UiHost.WaitUntilAsync(() => (row = dialog.GetVisualDescendants().OfType<DataGridRow>()
            .FirstOrDefault(r => r.IsEffectivelyVisible && (r.DataContext as MitigationTask)?.Title == title)) != null,
            $"the task \"{title}\" to be listed");
        return row!;
    }

    private static List<string> Texts(Window dialog) =>
        dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Text ?? "").ToList();

    private static T Field<T>(Window dialog, string label) where T : Control
    {
        var labelBlock = dialog.Find<TextBlock>(t => t.Text == label && t.IsEffectivelyVisible);
        var panel = (Panel)labelBlock.Parent!;
        return panel.Children.SkipWhile(c => !ReferenceEquals(c, labelBlock)).Skip(1).OfType<T>().First();
    }
}
