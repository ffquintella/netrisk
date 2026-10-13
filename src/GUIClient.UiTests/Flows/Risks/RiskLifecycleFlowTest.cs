using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using GUIClient.Views;
using Material.Icons;
using Material.Icons.Avalonia;
using Model.DTO;
using NSubstitute;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// The rest of the risk screen's commands: the lifecycle stages that open their own dialogs (mitigation, management
/// review, incident response plan) — opened on the selected risk and dismissed without a change, which leaves the
/// register as it was — and the deletions that go through the confirmation pattern (a risk file, the incident
/// response plan). The dialogs' own forms are their own features and are not driven here.
/// </summary>
public class RiskLifecycleFlowTest
{
    [UiFact]
    public async Task TestPlanMitigationOpensTheMitigationEditorForTheSelectedRisk()
    {
        var server = Arrange();
        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        UiInput.Click(screen.Lifecycle(MaterialIconKind.Planner));
        var editor = await screen.WaitForDialogAsync<EditMitigationWindow>();

        UiInput.Press(editor, Key.Escape, PhysicalKey.Escape);
        await screen.WaitForDialogsToCloseAsync("the mitigation editor to close on Esc");
        await server.Risks.Received(1).GetAllRisksAsync(false);
        Assert.Equal(Leak, screen.ViewModel.SelectedRisk?.Id);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestAddReviewOpensTheReviewEditorForTheSelectedRisk()
    {
        var server = Arrange();
        server.Risks.GetRiskReviewLevel(Leak).Returns(new ReviewLevel { Id = 2, Value = 90, Name = "Medium" });
        var reviews = UiServices.Current.Double<IMgmtReviewsService>();
        reviews.GetReviewTypes().Returns([new Review { Value = 1, Name = "Aprovar" }]);
        reviews.GetNextSteps().Returns([new NextStep { Value = 2, Name = "Considerar para projeto" }]);

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        UiInput.Click(screen.Lifecycle(MaterialIconKind.RateReview));
        var editor = await screen.WaitForDialogAsync<EditMgmtReview>();

        UiInput.Press(editor, Key.Escape, PhysicalKey.Escape);
        await screen.WaitForDialogsToCloseAsync("the review editor to close on Esc");
        server.Risks.DidNotReceiveWithAnyArgs().SaveRisk(default!);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestAddPlanOpensTheIncidentResponsePlanEditor()
    {
        var server = Arrange();
        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        UiInput.ScrollToAndClick(IrpButton(screen, MaterialIconKind.Plus));
        var editor = await screen.WaitForDialogAsync<IncidentResponsePlanWindow>();

        UiInput.Press(editor, Key.Escape, PhysicalKey.Escape);
        await screen.WaitForDialogsToCloseAsync("the plan editor to close on Esc");
        await server.Risks.Received(1).GetAllRisksAsync(false);

        UiHost.AssertClean();
    }

    /// <summary>Deleting the plan asks first and, on Yes, deletes it and the block goes back to "no plan".</summary>
    [UiFact]
    public async Task TestDeletingThePlanAsksAndThenDeletesIt()
    {
        Arrange();
        UiServices.Current.Double<IRisksService>().GetIncidentResponsePlanAsync(Erp).Returns(_ =>
            Task.FromResult<IncidentResponsePlan?>(new IncidentResponsePlan
            {
                Id = 50, Name = "Plano de contingência do ERP", LastUpdate = new DateTime(2026, 9, 20)
            }));
        var plans = UiServices.Current.Double<IIncidentResponsePlansService>();

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(ErpSubject);
        await UiHost.WaitUntilAsync(() => !screen.DetailTexts.Contains(Localized("NoIRPFound")), "the plan to load");

        UiInput.ScrollToAndClick(IrpButton(screen, MaterialIconKind.Delete));
        var question = await UiMessageBoxes.WaitForAsync("the plan delete confirmation");
        Assert.Contains(string.Format(Localized("ConfirmDeleteMSG"), "Plano de contingência do ERP"),
            UiMessageBoxes.Text(question));
        await UiMessageBoxes.AnswerAsync(question, "No");
        await plans.DidNotReceiveWithAnyArgs().DeleteAsync(default);

        UiInput.ScrollToAndClick(IrpButton(screen, MaterialIconKind.Delete));
        await UiMessageBoxes.AnswerAsync(await UiMessageBoxes.WaitForAsync("the confirmation, again"), "Yes");

        await plans.Received(1).DeleteAsync(50);
        await UiHost.WaitUntilAsync(() => screen.DetailTexts.Contains(Localized("NoIRPFound")),
            "the block to go back to \"no plan\"");

        UiHost.AssertClean();
    }

    /// <summary>A risk's files are listed with their type icon; deleting one asks, naming it, and only Yes deletes.</summary>
    [UiFact]
    public async Task TestDeletingAFileAsksAndThenRemovesItFromTheList()
    {
        Arrange();
        UiServices.Current.Double<IRisksService>().GetRiskFilesAsync(Leak).Returns(_ => Task.FromResult(new List<FileListing>
        {
            new() { Name = "Relatório de auditoria", UniqueName = "f-001", Type = "application/pdf" }
        }));
        var files = UiServices.Current.Double<IFilesService>();

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);
        var list = screen.Detail.Find<ItemsControl>(i => i.Name == "LstDocs");
        await UiHost.WaitUntilAsync(() => UiInput.TextOf(list).Contains("Relatório de auditoria"), "the risk's file to be listed");
        Assert.Contains(list.GetVisualDescendants().OfType<MaterialIcon>(), i => i.Kind == MaterialIconKind.FilePdfBox);

        var delete = list.Find<Button>(b => b.Name == "BtDelete");
        UiInput.ScrollToAndClick(delete);
        var question = await UiMessageBoxes.WaitForAsync("the file delete confirmation");
        Assert.Contains(string.Format(Localized("ConfirmDeleteMSG"), "Relatório de auditoria"), UiMessageBoxes.Text(question));
        await UiMessageBoxes.AnswerAsync(question, "No");
        files.DidNotReceiveWithAnyArgs().DeleteFile(default!);

        UiInput.ScrollToAndClick(delete);
        await UiMessageBoxes.AnswerAsync(await UiMessageBoxes.WaitForAsync("the confirmation, again"), "Yes");

        files.Received(1).DeleteFile("f-001");
        await UiHost.WaitUntilAsync(() => !UiInput.TextOf(list).Contains("Relatório de auditoria"),
            "the deleted file to leave the list");

        UiHost.AssertClean();
    }

    private static Button IrpButton(RiskScreen screen, MaterialIconKind icon) =>
        ((Grid)screen.Detail.Find<TextBlock>(t => t.Text == Localized("IRP")).Parent!)
        .Find<Button>(b => b.IsEffectivelyVisible &&
                           b.GetVisualDescendants().OfType<MaterialIcon>().Any(i => i.Kind == icon));
}
