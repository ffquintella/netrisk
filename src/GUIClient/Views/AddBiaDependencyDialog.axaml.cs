using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

/// <summary>Declares a continuity dependency (Stage 9.3, S43 §7).</summary>
public partial class AddBiaDependencyDialog : DialogWindowBase<ContinuityDialogResult>
{
    public AddBiaDependencyDialog()
    {
        InitializeComponent();
    }
}
