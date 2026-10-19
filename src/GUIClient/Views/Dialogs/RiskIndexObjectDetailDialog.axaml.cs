using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;

namespace GUIClient.Views.Dialogs;

/// <summary>The read-only object detail of the Cyber Risk Overview (S40 §5.9.2; T255). Esc closes (DialogWindowBase).</summary>
public partial class RiskIndexObjectDetailDialog : DialogWindowBase<RiskIndexObjectDetailDialogResult>
{
    public RiskIndexObjectDetailDialog()
    {
        InitializeComponent();
    }
}
