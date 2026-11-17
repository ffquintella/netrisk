using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GUIClient.Tools;
using GUIClient.ViewModels.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Agents;

/// <summary>
/// Shows the approval dialogs (T346, T348, and the per-write one, T357). The coordinator calls one at a time; a cancelled token (the request
/// expired, the server stopped) closes the dialog with no decision. Closing the dialog any other way is a refusal.
/// </summary>
public sealed class AvaloniaAgentApprovalPrompter : IAgentApprovalPrompter
{
    public Task<bool> ConfirmRegistrationAsync(RegistrationPrompt prompt, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            BringShellForward();
            var viewModel = new AgentRegistrationDialogViewModel();
            using var dismiss = cancellationToken.Register(() => Dispatcher.UIThread.Post(viewModel.Dismiss));
            var result = await Dialogs.ShowDialogAsync<DialogResultBase, AgentRegistrationDialogParameter,
                AgentRegistrationDialogViewModel>(viewModel, new AgentRegistrationDialogParameter { Prompt = prompt });
            return result?.Action == ResultActions.Ok && !cancellationToken.IsCancellationRequested;
        });

    public Task<AccessDecision> ConfirmAccessAsync(AccessPrompt prompt, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            BringShellForward();
            var viewModel = new AgentAccessDialogViewModel();
            using var dismiss = cancellationToken.Register(() => Dispatcher.UIThread.Post(viewModel.Dismiss));
            var result = await Dialogs.ShowDialogAsync<AgentAccessDialogResult, AgentAccessDialogParameter,
                AgentAccessDialogViewModel>(viewModel, new AgentAccessDialogParameter { Prompt = prompt });
            return result is { Action: ResultActions.Ok } && !cancellationToken.IsCancellationRequested
                ? AccessDecision.Grant(result.Blocks, result.Mode, result.ConfirmEachWrite)
                : AccessDecision.Deny;
        });

    /// <summary>T357: one edit, its exact change shown; only an explicit, armed Approve writes it.</summary>
    public Task<bool> ConfirmWriteAsync(WritePrompt prompt, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            BringShellForward();
            var viewModel = new AgentWriteDialogViewModel();
            using var dismiss = cancellationToken.Register(() => Dispatcher.UIThread.Post(viewModel.Dismiss));
            var result = await Dialogs.ShowDialogAsync<DialogResultBase, AgentWriteDialogParameter,
                AgentWriteDialogViewModel>(viewModel, new AgentWriteDialogParameter { Prompt = prompt });
            return result?.Action == ResultActions.Ok && !cancellationToken.IsCancellationRequested;
        });

    private static IDialogService Dialogs => Program.ServiceProvider.GetRequiredService<IDialogService>();

    /// <summary>A request nobody sees cannot be decided: restore the shell from the tray or the dock first.</summary>
    private static void BringShellForward()
    {
        var shell = Program.ServiceProvider.GetRequiredService<IMainWindowProvider>().GetMainWindow();
        if (!shell.IsVisible) shell.Show();
        if (shell.WindowState == WindowState.Minimized) shell.WindowState = WindowState.Normal;
        shell.Activate();
    }
}
