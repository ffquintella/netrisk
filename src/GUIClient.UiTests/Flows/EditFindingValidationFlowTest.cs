using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.UiTests.Infrastructure;
using GUIClient.UiTests.Smoke;
using GUIClient.ViewModels;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.Views;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Globalization;
using NSubstitute;
using Xunit;

namespace GUIClient.UiTests.Flows;

/// <summary>
/// The interactive flow of S54 §5.1 item 3, on the finding editor (<see cref="EditVulnerabilitiesDialog"/>,
/// a <see cref="DialogWindowBase{TResult}"/> dialog with an inline validation summary): type an
/// invalid value, see the error inline, correct it, see Save enable — all through simulated mouse and
/// keyboard input, never by setting the view-model.
/// </summary>
public class EditFindingValidationFlowTest
{
    private const string LoadedTitle = "OpenSSL desatualizado";
    private const string CorrectedTitle = "OpenSSL 1.0.2 sem suporte";

    [UiFact]
    public async Task TestClearingTheTitleShowsTheInlineErrorAndRetypingItEnablesSave()
    {
        var services = UiServices.Current;
        var finding = ArrangeFinding(services);

        var owner = UiHost.Host(new TextBlock());
        services.Double<IMainWindowProvider>().GetActiveWindow().Returns(owner);

        // Opened exactly as the findings screen opens it: through the dialog service, by view-model name.
        var pending = services.GetRequiredService<IDialogService>()
            .ShowDialogAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>(
                nameof(EditVulnerabilitiesDialogViewModel),
                new VulnerabilityDialogParameter { Operation = OperationType.Edit, Vulnerability = finding });

        await UiHost.WaitUntilAsync(() => owner.OwnedWindows.Count == 1, "the finding editor to open");
        var dialog = Assert.IsType<EditVulnerabilitiesDialog>(owner.OwnedWindows[0]);

        var save = dialog.Find<Button>(b => b.Name == "BtSave");
        var inlineError = dialog.Find<TextBlock>(t => t.Classes.Contains("validationSummary"));

        // A complete finding loads valid: Save enabled, no inline error.
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "the loaded finding to pass validation");
        Assert.False(inlineError.IsEffectivelyVisible);

        // 1. Invalid value: delete the title with the keyboard.
        var title = dialog.Find<TextBox>(t => t.Text == LoadedTitle);
        UiInput.ClearByKeyboard(title);

        // 2. The error appears inline, in the window, and Save is disabled.
        await UiHost.WaitUntilAsync(() => inlineError.IsEffectivelyVisible, "the inline validation error to appear");
        Assert.True(string.IsNullOrEmpty(title.Text));
        Assert.Equal(Localized("PleaseEnterAValueMSG"), inlineError.Text);
        Assert.False(save.IsEffectivelyEnabled);

        // 3. Correct it by typing.
        UiInput.Type(dialog, CorrectedTitle);

        // 4. The error goes and Save enables.
        await UiHost.WaitUntilAsync(() => save.IsEffectivelyEnabled, "Save to enable once the title is valid again");
        Assert.Equal(CorrectedTitle, title.Text);
        Assert.False(inlineError.IsEffectivelyVisible);

        // Leave with Esc — DialogWindowBase's keyboard contract dismisses the dialog with no result —
        // and nothing is saved.
        UiInput.Press(dialog, Key.Escape, PhysicalKey.Escape);

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(result);
        await services.Double<IVulnerabilitiesService>().DidNotReceiveWithAnyArgs().UpdateAsync(default!);

        UiHost.AssertClean();
    }

    /// <summary>A finding with every field the editor's rules require, and the lists it is chosen from.</summary>
    private static Vulnerability ArrangeFinding(UiServices services)
    {
        var finding = new Vulnerability
        {
            Id = 10, Title = LoadedTitle, Description = "Versão do OpenSSL fora de suporte no servidor.",
            Severity = "3", Score = 7.5, Technology = "Linux", FixTeamId = 5, AnalystId = 2, Status = 1,
            FirstDetection = new DateTime(2026, 9, 1), LastDetection = new DateTime(2026, 10, 1)
        };

        services.Double<IVulnerabilitiesService>().GetOne(10).Returns(finding);
        services.Double<ITechnologiesService>().GetAllAsync().Returns([new Technology { Value = 1, Name = "Linux" }]);
        services.Double<IImpactsService>().GetAllAsync().Returns(
            [new LocalizableListItem { Key = 3, Value = "High", LocalizedValue = "Alto" }]);
        services.Double<ITeamsService>().GetAllAsync().Returns([new Team { Value = 5, Name = "Infraestrutura" }]);
        services.Double<IUsersService>().GetAllAsync(Arg.Any<bool>()).Returns(
            [new UserListing { Id = 2, Name = "Analista de Teste", Username = "analista.teste" }]);

        return finding;
    }

    private static string Localized(string key) =>
        UiServices.Current.GetRequiredService<ILocalizationService>().GetLocalizer(typeof(GUIClient.App).Assembly)[key];
}
