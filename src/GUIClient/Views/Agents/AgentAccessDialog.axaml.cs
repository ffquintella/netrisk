using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.Agents;
using GUIClient.ViewModels.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Agents;

public partial class AgentAccessDialog : DialogWindowBase<AgentAccessDialogResult>
{
    public AgentAccessDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Deny takes the focus and Grant arms late, so a stray keystroke cannot grant access.</summary>
    protected override void OnOpened()
    {
        this.FindControl<Button>("BtDeny")?.Focus();
        (DataContext as AgentAccessDialogViewModel)?.StartArming(AgentApprovalTiming.ArmDelay);
    }
}
