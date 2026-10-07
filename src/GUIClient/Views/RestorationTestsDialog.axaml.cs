using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views;

/// <summary>The restoration tests of a process or IT service (Stage 9.3, S43 §7).</summary>
public partial class RestorationTestsDialog : DialogWindowBase<ContinuityDialogResult>
{
    public RestorationTestsDialog()
    {
        InitializeComponent();
    }
}
