using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.Agents;
using GUIClient.ViewModels.Agents;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Agents;

public partial class AgentWriteDialog : DialogWindowBase<DialogResultBase>
{
    public AgentWriteDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Deny takes the focus and Approve arms late, so a stray keystroke cannot approve a change (T357).</summary>
    protected override void OnOpened()
    {
        this.FindControl<Button>("BtDeny")?.Focus();
        (DataContext as AgentWriteDialogViewModel)?.StartArming(AgentApprovalTiming.ArmDelay);
    }
}
