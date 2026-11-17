using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.Agents;
using GUIClient.ViewModels.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Agents;

public partial class AgentRegistrationDialog : DialogWindowBase<DialogResultBase>
{
    public AgentRegistrationDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Deny takes the focus and Approve arms late, so a stray keystroke cannot approve.</summary>
    protected override void OnOpened()
    {
        this.FindControl<Button>("BtDeny")?.Focus();
        (DataContext as AgentRegistrationDialogViewModel)?.StartArming(AgentApprovalTiming.ArmDelay);
    }
}
