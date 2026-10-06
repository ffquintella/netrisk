using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

/// <summary>
/// Edits a risk's linkage chain (Stage 9.1, S41 §7). A <see cref="DialogWindowBase{TResult}"/> like
/// every other modal, so it inherits Esc-to-dismiss and parent dimming.
/// </summary>
public partial class EditRiskChainDialog : DialogWindowBase<EditRiskChainDialogResult>
{
    public EditRiskChainDialog()
    {
        InitializeComponent();
    }
}
