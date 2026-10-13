using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.UiTests.Infrastructure;
using GUIClient.Views;
using Material.Icons;
using Model.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GUIClient.UiTests.Flows.Risks.RiskScreen;
using static GUIClient.UiTests.Flows.Risks.RiskServer;

namespace GUIClient.UiTests.Flows.Risks;

/// <summary>
/// Closing a risk through <see cref="CloseRiskWindow"/> — from the bar under the list or from the lifecycle
/// toolbar — and reopening a closed one after a Yes/No confirmation.
/// </summary>
public class CloseRiskFlowTest
{
    [UiTheory]
    [InlineData("bar")]
    [InlineData("lifecycle")]
    public async Task TestClosingARiskRecordsTheReasonAndNotesAndDropsItFromTheList(string entryPoint)
    {
        var server = ArrangeClosure();
        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);

        UiInput.Click(entryPoint == "bar" ? screen.Bar("BtCloseRisk") : screen.Lifecycle(MaterialIconKind.Close));
        var dialog = await screen.WaitForDialogAsync<CloseRiskWindow>();
        var save = dialog.Find<Button>(b => b.Name == "BtSave");
        var inlineError = dialog.Find<TextBlock>(t => t.Classes.Contains("validationSummary"));

        // A closure needs a reason: until one is chosen Save is off and the window says so.
        Assert.False(save.IsEffectivelyEnabled);
        Assert.True(inlineError.IsEffectivelyVisible);
        Assert.Equal(Localized("PleaseSelectOneMSG"), inlineError.Text);

        await UiInput.ChooseAsync(dialog.Find<ComboBox>(), "Mitigado");
        UiInput.Click(dialog.Find<TextBox>(t => t.Name == "TxtReason"));
        UiInput.Type(dialog, "Controles de acesso revisados.");
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "Save to enable once a reason is chosen");
        Assert.False(inlineError.IsEffectivelyVisible);

        UiInput.Click(save);

        await screen.WaitForDialogsToCloseAsync("the close dialog to close after saving");
        server.Risks.Received(1).CloseRisk(Arg.Is<Closure>(c =>
            c.RiskId == Leak && c.CloseReason == 1 && c.Note == "Controles de acesso revisados." &&
            c.UserId == RiskUsers.AnalystId));
        await UiHost.WaitUntilAsync(() => !screen.Subjects.Contains(LeakSubject),
            "the closed risk to leave the list of open risks");
        Assert.Equal([ErpSubject, FraudSubject], screen.Subjects);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestCancellingTheCloseDialogClosesNothing()
    {
        var server = ArrangeClosure();
        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(ErpSubject);

        UiInput.Click(screen.Bar("BtCloseRisk"));
        var dialog = await screen.WaitForDialogAsync<CloseRiskWindow>();
        await UiInput.ChooseAsync(dialog.Find<ComboBox>(), "Mitigado");
        UiInput.Click(dialog.Find<Button>(b => b.Name == "BtCancel"));

        await screen.WaitForDialogsToCloseAsync("the close dialog to close on Cancel");
        server.Risks.DidNotReceiveWithAnyArgs().CloseRisk(default!);
        Assert.Equal([LeakSubject, ErpSubject, FraudSubject], screen.Subjects);
        await server.Risks.Received(1).GetAllRisksAsync(false);

        UiHost.AssertClean();
    }

    /// <summary>IX-4: a failed save says so and keeps the dialog open with the input intact, for a retry.</summary>
    [UiFact]
    public async Task TestAFailedCloseKeepsTheDialogOpenWithTheInput()
    {
        var server = ArrangeClosure();
        server.Risks.When(r => r.CloseRisk(Arg.Any<Closure>()))
            .Do(_ => throw new RestComunicationException("Error closing risk"));

        var screen = await OpenAsync(RiskUsers.Manager);
        await screen.SelectAsync(LeakSubject);
        UiInput.Click(screen.Bar("BtCloseRisk"));
        var dialog = await screen.WaitForDialogAsync<CloseRiskWindow>();
        await UiInput.ChooseAsync(dialog.Find<ComboBox>(), "Aceito pela diretoria");

        UiInput.Click(dialog.Find<Button>(b => b.Name == "BtSave"));

        var error = await UiMessageBoxes.WaitForAsync("the closing error");
        Assert.Contains(Localized("RiskClosingErrorMSG"), UiMessageBoxes.Text(error));
        await UiMessageBoxes.AnswerAsync(error, "OK");

        Assert.True(dialog.IsVisible);
        Assert.Equal("Aceito pela diretoria", UiInput.TextOf(dialog.Find<ComboBox>()));
        Assert.Contains(LeakSubject, screen.Subjects);

        UiInput.Click(dialog.Find<Button>(b => b.Name == "BtCancel"));
        await screen.WaitForDialogsToCloseAsync("the close dialog to close on Cancel");

        UiHost.AssertClean();
    }

    /// <summary>
    /// Reopen asks first, naming the risk; No changes nothing, Yes deletes the closure and the risk comes back to the
    /// open register, still selected.
    /// </summary>
    [UiFact]
    public async Task TestReopeningAClosedRiskAsksAndThenReopensIt()
    {
        var server = ArrangeClosure();
        server.Risks.ReopenRiskAsync(Backup).Returns(_ =>
        {
            server.Row(Backup).Status = "Mitigation Planned";
            return Task.CompletedTask;
        });

        var screen = await OpenAsync(RiskUsers.Manager);
        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains(BackupSubject), "the closed risk to be listed");
        await screen.SelectAsync(BackupSubject);
        var reopen = screen.Lifecycle(MaterialIconKind.DoorOpen);

        UiInput.Click(reopen);
        var question = await UiMessageBoxes.WaitForAsync("the reopen confirmation");
        Assert.Contains(string.Format(Localized("ReopenRiskConfirmMSG"), BackupSubject), UiMessageBoxes.Text(question));
        await UiMessageBoxes.AnswerAsync(question, "No");
        await server.Risks.DidNotReceiveWithAnyArgs().ReopenRiskAsync(default);

        UiInput.Click(reopen);
        question = await UiMessageBoxes.WaitForAsync("the reopen confirmation, again");
        await UiMessageBoxes.AnswerAsync(question, "Yes");

        await server.Risks.Received(1).ReopenRiskAsync(Backup);
        await UiHost.WaitUntilAsync(() => screen.ViewModel.SelectedRisk?.Id == Backup &&
                                          screen.Subjects.SequenceEqual([LeakSubject, ErpSubject, FraudSubject, BackupSubject]),
            "the reopened risk to be listed with the open ones and still selected");
        Assert.Equal("Mitigation Planned", screen.ViewModel.SelectedRisk!.Status);

        UiHost.AssertClean();
    }

    [UiFact]
    public async Task TestAFailedReopenIsReported()
    {
        var server = ArrangeClosure();
        server.Risks.ReopenRiskAsync(Backup).ThrowsAsync(new RestComunicationException("Error reopening risk"));

        var screen = await OpenAsync(RiskUsers.Manager);
        UiInput.Click(screen.Bar("BtClosedFilter"));
        await UiHost.WaitUntilAsync(() => screen.Subjects.Contains(BackupSubject), "the closed risk to be listed");
        await screen.SelectAsync(BackupSubject);

        UiInput.Click(screen.Lifecycle(MaterialIconKind.DoorOpen));
        await UiMessageBoxes.AnswerAsync(await UiMessageBoxes.WaitForAsync("the reopen confirmation"), "Yes");

        var error = await UiMessageBoxes.WaitForAsync("the reopening error");
        Assert.Contains(Localized("ErrorReopeningRiskMSG").Trim(), UiMessageBoxes.Text(error));
        await UiMessageBoxes.AnswerAsync(error, "OK");
        Assert.Contains(BackupSubject, screen.Subjects);

        UiHost.AssertClean();
    }

    private static RiskServer ArrangeClosure()
    {
        var server = RiskServer.Arrange();
        server.Risks.When(r => r.CloseRisk(Arg.Any<Closure>()))
            .Do(call => server.Row(call.Arg<Closure>().RiskId).Status = "Closed");
        return server;
    }
}
