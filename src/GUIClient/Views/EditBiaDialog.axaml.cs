using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

/// <summary>
/// Declares or replaces a business impact analysis (Stage 9.3, S43 §7). A
/// <see cref="DialogWindowBase{TResult}"/> like every other modal, so it inherits Esc-to-dismiss and
/// parent dimming.
/// </summary>
public partial class EditBiaDialog : DialogWindowBase<ContinuityDialogResult>
{
    public EditBiaDialog()
    {
        InitializeComponent();
    }
}
